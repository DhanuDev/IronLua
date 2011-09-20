using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using IronLua.Compiler;
using IronLua.Util;
using Expr = System.Linq.Expressions.Expression;

namespace IronLua.Runtime.Binder
{
    class LuaInvokeBinder : InvokeBinder
    {
        readonly Context context;

        public LuaInvokeBinder(Context context, CallInfo callInfo)
            : base(callInfo)
        {
            this.context = context;
        }

        public override DynamicMetaObject FallbackInvoke(DynamicMetaObject target, DynamicMetaObject[] args, DynamicMetaObject errorSuggestion)
        {
            if (!target.HasValue || args.Any(a => !a.HasValue))
                return Defer(target, args);

            if (!target.LimitType.IsSubclassOf(typeof(Delegate)))
                return MetamethodFallback(target, args);

            var restrictions =
                RuntimeHelpers.MergeTypeRestrictions(target, args).Merge(
                RuntimeHelpers.MergeInstanceRestrictions(target));

            List<Expr> sideEffects;
            Expr failExpr;
            var function = (Delegate)target.Value;
            var mappedArgs = MapArguments(args, function.Method.GetParameters(), ref restrictions, out sideEffects, out failExpr);

            if (failExpr != null)
                return new DynamicMetaObject(Expr.Block(failExpr, Expr.Default(typeof(object))), restrictions);

            var invokeExpr =
                Expr.Convert(
                    Expr.Invoke(
                        Expr.Convert(target.Expression, target.LimitType),
                        mappedArgs),
                    typeof(object));

            // Execute overflowing arguments for side effects
            Expr expr;
            if (sideEffects.Count == 0)
            {
                expr = invokeExpr;
            }
            else
            {
                var tempVar = Expr.Variable(typeof(object));
                var assign = Expr.Assign(tempVar, invokeExpr);
                sideEffects.Insert(0, assign);
                sideEffects.Add(tempVar);
                expr = Expr.Block(new[] {tempVar}, sideEffects);
            }

            return new DynamicMetaObject(expr, restrictions);
        }

        DynamicMetaObject MetamethodFallback(DynamicMetaObject target, DynamicMetaObject[] args)
        {
            var expression = Expr.Invoke(
                Expr.Constant((Func<Context, object, object[], object>)LuaOps.CallMetamethod),
                Expr.Constant(context),
                Expr.Convert(target.Expression, typeof(object)),
                Expr.NewArrayInit(
                    typeof(object),
                    args.Select(arg => Expr.Convert(arg.Expression, typeof(object)))));

            return new DynamicMetaObject(expression, RuntimeHelpers.MergeTypeRestrictions(target));
        }

        IEnumerable<Expr> MapArguments(DynamicMetaObject[] args, ParameterInfo[] parameterInfos, ref BindingRestrictions restrictions, out List<Expr> sideEffects, out Expr failExpr)
        {
            var arguments = args.Select(arg => new Argument(arg.Expression, arg.LimitType)).ToList();

            // Remove closure
            ParameterInfo[] parameters;
            if (parameterInfos.Length > 0 && parameterInfos[0].ParameterType == typeof(Closure))
            {
                parameters = new ParameterInfo[parameterInfos.Length - 1];
                Array.Copy(parameterInfos, 1, parameters, 0, parameters.Length);
            }
            else
            {
                parameters = parameterInfos;
            }

            ExpandLastArg(arguments, args, ref restrictions);
            DefaultParamValues(arguments, parameters);
            OverflowIntoParams(arguments, parameters);
            TrimArguments(arguments, parameters, out sideEffects);
            ConvertArgumentToParamType(arguments, parameters, out failExpr);
            if (failExpr == null)
                CheckNumberOfArguments(arguments, parameters, out failExpr);

            return arguments.Select(arg => arg.Expression);
        }

        // TODO: Add support for object[] as an alternative for Varargs?
        void ExpandLastArg(List<Argument> arguments, DynamicMetaObject[] args, ref BindingRestrictions restrictions)
        {
            if (args.Length == 0)
                return;

            var lastArg = args.Last();

            if (lastArg.LimitType != typeof(Varargs))
                return;

            // TODO: Only restrict on Varargs.Count and add arguments by indexing into Varargs value
            restrictions = restrictions.Merge(RuntimeHelpers.MergeInstanceRestrictions(lastArg));

            var varargs = (Varargs)lastArg.Value;
            arguments.RemoveAt(arguments.Count - 1);
            arguments.AddRange(varargs.Select(value => new Argument(Expr.Constant(value), value.GetType())));
        }

        void DefaultParamValues(List<Argument> arguments, ParameterInfo[] parameters)
        {
            var defaultArgs = parameters
                .Skip(arguments.Count)
                .Where(param => param.IsOptional)
                .Select(param => new Argument(Expr.Constant(param.DefaultValue), param.ParameterType));

            arguments.AddRange(defaultArgs);
        }

        void OverflowIntoParams(List<Argument> arguments, ParameterInfo[] parameters)
        {
            if (arguments.Count == 0 || parameters.Length == 0)
                return;

            var overflowingArgs = arguments.Skip(parameters.Length - 1).ToList();
            var lastParam = parameters.Last();

            if (overflowingArgs.Count == 1 && overflowingArgs[0].Type == lastParam.ParameterType)
                return;

            Expr argExpr;
            if (lastParam.IsParams())
            {
                var elementType = lastParam.ParameterType.GetElementType();
                if (overflowingArgs.Any(arg => arg.Type != elementType && !arg.Type.IsSubclassOf(elementType)))
                    return;

                argExpr = Expr.NewArrayInit(
                    elementType,
                    overflowingArgs.Select(arg => Expr.Convert(arg.Expression, elementType)));
            }
            else if (lastParam.ParameterType == typeof(Varargs))
            {
                argExpr = Expr.New(
                    Methods.NewVarargs,
                    Expr.NewArrayInit(
                        typeof(object),
                        overflowingArgs.Select(arg => Expr.Convert(arg.Expression, typeof(object)))));
            }
            else
            {
                return;
            }

            arguments.RemoveRange(arguments.Count - overflowingArgs.Count, overflowingArgs.Count);
            arguments.Add(new Argument(argExpr, lastParam.ParameterType));
        }

        void TrimArguments(List<Argument> arguments, ParameterInfo[] parameters, out List<Expr> sideEffects)
        {
            if (arguments.Count <= parameters.Length)
            {
                sideEffects = new List<Expr>();
                return;
            }

            sideEffects = arguments
                .Skip(parameters.Length)
                .Select(arg => arg.Expression)
                .ToList();
            arguments.RemoveRange(parameters.Length, arguments.Count - parameters.Length);
        }

        void ConvertArgumentToParamType(List<Argument> arguments, ParameterInfo[] parameters, out Expr failExpr)
        {
            failExpr = null;

            for (int i = 0; i < arguments.Count; i++)
            {
                var arg = arguments[i];
                var param = parameters[i];

                if (param.ParameterType == typeof(bool) && arg.Type != typeof(bool))
                {
                    arg.Expression = ExprHelpers.ConvertToBoolean(context, arg.Expression);
                }
                else if (param.ParameterType == typeof(double) && arg.Type == typeof(string))
                {
                    arg.Expression = ExprHelpers.ConvertToNumberAndCheck(
                        context, arg.Expression,
                        ExceptionMessage.INVOKE_BAD_ARGUMENT_GOT, i + 1, "number", "string");
                }
                else if (param.ParameterType == typeof(string) && arg.Type != typeof(string))
                {
                    arg.Expression = Expr.Call(arg.Expression, Methods.ObjectToString, arg.Expression);
                }
                else
                {
                    if (arg.Type == param.ParameterType || arg.Type.IsSubclassOf(param.ParameterType))
                    {
                        arg.Expression = Expr.Convert(arg.Expression, param.ParameterType);
                    }
                    else
                    {
                        Func<Expr, Expr> typeNameExpr =
                            obj => Expr.Invoke(
                                Expr.Constant((Func<object, string>)RuntimeHelpers.GetTypeName),
                                Expr.Convert(obj, typeof(object)));

                        // Ugly reflection hack
                        failExpr = Expr.Throw(
                            Expr.New(
                                Methods.NewRuntimeException,
                                Expr.Constant(ExceptionMessage.INVOKE_BAD_ARGUMENT_GOT),
                                Expr.NewArrayInit(
                                    typeof(object),
                                    Expr.Constant(i + 1, typeof(object)),
                                    typeNameExpr(Expr.Constant(Activator.CreateInstance(param.ParameterType))),
                                    typeNameExpr(arg.Expression))));
                        break;
                    }
                }
            }
        }

        void CheckNumberOfArguments(List<Argument> arguments, ParameterInfo[] parameters, out Expr failExpr)
        {
            failExpr = null;
            Debug.Assert(arguments.Count <= parameters.Length);

            if (arguments.Count < parameters.Length)
            {
                failExpr = Expr.Throw(
                    Expr.New(
                        Methods.NewRuntimeException,
                        Expr.Constant(ExceptionMessage.INVOKE_BAD_ARGUMENT),
                        Expr.Constant(new object[] {arguments.Count + 1, "value"})));
            }
        }

        class Argument
        {
            public Expr Expression { get; set; }
            public Type Type { get; private set; }

            public Argument(Expr expression, Type type)
            {
                Expression = expression;
                Type = type;
            }
        }
    }
}