﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using IronLua.Compiler;
using IronLua.Compiler.Ast;
using IronLua.Runtime;
using IronLua.Runtime.Binder;
using IronLua.Util;
using Expr = System.Linq.Expressions.Expression;
using ParamExpr = System.Linq.Expressions.ParameterExpression;
using ExprType = System.Linq.Expressions.ExpressionType;
using Expression = IronLua.Compiler.Ast.Expression;
using IronLua.Library;

namespace IronLua.Compiler
{
    class Generator : IStatementVisitor<Expr>, ILastStatementVisitor<Expr>, IExpressionVisitor<Expr>
    {
        static Dictionary<BinaryOp, ExprType> binaryExprTypes =
            new Dictionary<BinaryOp, ExprType>
                {
                    {BinaryOp.Or,           ExprType.OrElse},
                    {BinaryOp.And,          ExprType.AndAlso},
                    {BinaryOp.Equal,        ExprType.Equal},
                    {BinaryOp.NotEqual,     ExprType.NotEqual},
                    {BinaryOp.Less,         ExprType.LessThan},
                    {BinaryOp.Greater,      ExprType.GreaterThan},
                    {BinaryOp.LessEqual,    ExprType.LessThanOrEqual},
                    {BinaryOp.GreaterEqual, ExprType.GreaterThanOrEqual},
                    {BinaryOp.Add,          ExprType.Add},
                    {BinaryOp.Subtract,     ExprType.Subtract},
                    {BinaryOp.Multiply,     ExprType.Multiply},
                    {BinaryOp.Divide,       ExprType.Divide},
                    {BinaryOp.Mod,          ExprType.Modulo},
                    {BinaryOp.Power,        ExprType.Power} 
                };

        static Dictionary<UnaryOp, ExprType> unaryExprTypes =
            new Dictionary<UnaryOp, ExprType>
                {
                    {UnaryOp.Negate, ExprType.Negate},
                    {UnaryOp.Not,    ExprType.Not}
                };

        Scope scope;
        Context context;

        public Generator(Context context)
        {
            this.context = context;
        }

        public Expression<Func<dynamic>> Compile(Block block)
        {
            scope = Scope.CreateRoot();

            return Expr.Lambda<Func<dynamic>>(Visit(block));
        }

        Expr Visit(Block block)
        {
            if (!scope.IsRoot)
                scope = Scope.CreateChild(scope);

            var linqStatements = block.Statements.Select(s => s.Visit(this));

            if (block.LastStatement != null)
                linqStatements = linqStatements.Add(block.LastStatement.Visit(this));

            return Expr.Block(linqStatements);
        }

        Expr IStatementVisitor<Expr>.Visit(Statement.Assign statement)
        {
            throw new NotImplementedException();
        }

        Expr IStatementVisitor<Expr>.Visit(Statement.Do statement)
        {
            throw new NotImplementedException();
        }

        Expr IStatementVisitor<Expr>.Visit(Statement.For statement)
        {
            throw new NotImplementedException();
        }

        Expr IStatementVisitor<Expr>.Visit(Statement.ForIn statement)
        {
            throw new NotImplementedException();
        }

        Expr IStatementVisitor<Expr>.Visit(Statement.Function statement)
        {
            throw new NotImplementedException();
        }

        Expr IStatementVisitor<Expr>.Visit(Statement.FunctionCall statement)
        {
            throw new NotImplementedException();
        }

        Expr IStatementVisitor<Expr>.Visit(Statement.If statement)
        {
            throw new NotImplementedException();
        }

        Expr IStatementVisitor<Expr>.Visit(Statement.LocalAssign statement)
        {
            var locals = statement.Identifiers.Select(scope.AddLocal).ToList();

            // Try to wrap all values except the last with varargs select
            var values = new List<Expr>(statement.Values.Count);
            for (int i = 0; i < values.Count - 1; i++)
                values.Add(TryWrapWithVarargsSelect(statement.Values[i]));
            values.Add(statement.Values.Last().Visit(this));

            if (statement.Values.Last().IsVarargsOrFuncCall())
                return VarargsExpandAssignment(values, locals);

            // Assign values to temporaries
            var tempVariables = values.Select(expr => Expr.Variable(expr.Type)).ToList();
            var tempAssigns = tempVariables.Zip(values, Expr.Assign);

            // Shrink or pad temporary's list with nil to match local's list length
            // and cast temporaries to locals type
            var tempVariablesResized = tempVariables
                .Resize(statement.Identifiers.Count, new Expression.Nil().Visit(this))
                .Zip(locals, (tempVar, local) => Expr.Convert(tempVar, local.Type));

            // Assign temporaries to locals
            var realAssigns = locals.Zip(tempVariablesResized, Expr.Assign);
            return Expr.Block(tempVariables.Concat(locals), tempAssigns.Concat(realAssigns));
        }

        Expr TryWrapWithVarargsSelect(Expression expr)
        {
            // If expr is a varargs or function call expression we need return the first element in
            // the Varargs list if the value is of type Varargs or do nothing
            if (!expr.IsVarargsOrFuncCall())
                return expr.Visit(this);

            var variable = Expr.Variable(typeof(object));

            return
                Expr.Block(
                    new[] {variable},
                    Expr.Assign(variable, expr.Visit(this)),
                    Expr.IfThenElse(
                        Expr.TypeIs(variable, typeof(Varargs)),
                        Expr.Call(variable, typeof(Varargs).GetMethod("First")),
                        variable));
        }

        Expr VarargsExpandAssignment(List<Expr> values, List<ParameterExpression> locals)
        {
            return Expr.Invoke(
                Expr.Constant((Action<IRuntimeVariables, object[]>)LuaOps.VarargsAssign),
                Expr.RuntimeVariables(locals),
                Expr.NewArrayInit(typeof(object[]), values));
        }

        Expr IStatementVisitor<Expr>.Visit(Statement.LocalFunction statement)
        {
            throw new NotImplementedException();
        }

        Expr IStatementVisitor<Expr>.Visit(Statement.Repeat statement)
        {
            throw new NotImplementedException();
        }

        Expr IStatementVisitor<Expr>.Visit(Statement.While statement)
        {
            throw new NotImplementedException();
        }

        Expr ILastStatementVisitor<Expr>.Visit(LastStatement.Break lastStatement)
        {
            throw new NotImplementedException();
        }

        Expr ILastStatementVisitor<Expr>.Visit(LastStatement.Return lastStatement)
        {
            throw new NotImplementedException();
        }

        Expr IExpressionVisitor<Expr>.Visit(Expression.BinaryOp expression)
        {
            var left = expression.Left.Visit(this);
            var right = expression.Right.Visit(this);
            ExprType operation;
            if (binaryExprTypes.TryGetValue(expression.Operation, out operation))
                return Expr.Dynamic(context.BinderCache.GetBinaryOperationBinder(operation),
                                    typeof(object), left, right);

            // BinaryOp have to be Concat at this point which can't be represented as a binary operation in the DLR
            return
                Expr.Call(
                    Expr.Field(
                        Expr.Constant(context),
                        typeof(Context).GetField("StringLibrary", BindingFlags.NonPublic | BindingFlags.Instance)),
                    typeof(LuaString).GetMethod("Concat", BindingFlags.NonPublic | BindingFlags.Instance),
                    Expr.Convert(left, typeof(object)), Expr.Convert(right, typeof(object)));
        }

        Expr IExpressionVisitor<Expr>.Visit(Expression.Boolean expression)
        {
            return Expr.Constant(expression.Literal);
        }

        Expr IExpressionVisitor<Expr>.Visit(Expression.Function expression)
        {
            throw new NotImplementedException();
        }

        Expr IExpressionVisitor<Expr>.Visit(Expression.Nil expression)
        {
            return Expr.Default(typeof(object));
        }

        Expr IExpressionVisitor<Expr>.Visit(Expression.Number expression)
        {
            return Expr.Constant(expression.Literal);
        }

        Expr IExpressionVisitor<Expr>.Visit(Expression.Prefix expression)
        {
            throw new NotImplementedException();
        }

        Expr IExpressionVisitor<Expr>.Visit(Expression.String expression)
        {
            return Expr.Constant(expression.Literal);
        }

        Expr IExpressionVisitor<Expr>.Visit(Expression.Table expression)
        {
            throw new NotImplementedException();
        }

        Expr IExpressionVisitor<Expr>.Visit(Expression.UnaryOp expression)
        {
            var operand = expression.Operand.Visit(this);
            ExprType operation;
            if (unaryExprTypes.TryGetValue(expression.Operation, out operation))
                return Expr.Dynamic(context.BinderCache.GetUnaryOperationBinder(operation),
                                    typeof(object), operand);

            // UnaryOp have to be Length at this point which can't be represented as a unary operation in the DLR
            return Expr.Invoke(
                Expr.Constant((Func<Context, object, object>)LuaOps.Length),
                Expr.Constant(context),
                Expr.Convert(operand, typeof(object)));
        }

        Expr IExpressionVisitor<Expr>.Visit(Expression.Varargs expression)
        {
            throw new NotImplementedException();
        }
    }
}
