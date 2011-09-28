﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using IronLua.Compiler;
using IronLua.Compiler.Parser;
using IronLua.Runtime;

namespace IronLua.Library
{
    class BaseLibrary : Library
    {
        public BaseLibrary(Context context) : base(context)
        {
        }

        [Internal]
        public Varargs Assert(bool v, object message = null, params object[] additional)
        {
            if (v)
            {
                var returnValues = new List<object>(2 + additional.Length) {true};
                if (message != null)
                    returnValues.Add(message);
                returnValues.AddRange(additional);
                return new Varargs(returnValues);
            }

            if (message == null)
                throw new LuaRuntimeException("Assertion failed");
            throw new LuaRuntimeException(message.ToString());
        }

        [Internal]
        public void CollectGarbage(string opt, string arg = null)
        {
            throw new LuaRuntimeException(ExceptionMessage.FUNCTION_NOT_IMPLEMENTED);
        }

        [Internal]
        public object DoFile(string filename = null)
        {
            var source = filename == null ? Console.In.ReadToEnd() : File.ReadAllText(filename);
            try
            {
                return CompileString(source)();
            }
            catch (LuaSyntaxException e)
            {
                throw new LuaRuntimeException(e.Message, e);
            }
        }

        [Internal]
        public void Error(string message, double level = 1.0)
        {
            // TODO: Use level when call stacks are implemented
            throw new LuaRuntimeException(message);
        }

        [Internal]
        public object GetFEnv(double f)
        {
            throw new LuaRuntimeException(ExceptionMessage.FUNCTION_NOT_IMPLEMENTED);
        }

        [Internal]
        public object GetMetatable(object obj)
        {
            var metatable = Context.GetMetatable(obj);
            if (metatable == null)
                return null;

            var metatable2 = metatable.GetValue(Constant.METATABLE_METAFIELD);
            return metatable2 ?? metatable;
        }

        [Internal]
        public Varargs IPairs(LuaTable t)
        {
            var length = t.Length();
            Func<double, object> func =
                index =>
                    {
                        index++;
                        return index > length ? null : new Varargs(index, t.GetValue(index));
                    };

            return new Varargs(func, t, 0.0);
        }

        [Internal]
        public Varargs Load(Delegate func, string chunkname = "=(load)")
        {
            var invoker = Context.GetDynamicCall0();
            var sb = new StringBuilder(1024);

            while (true)
            {
                var result = invoker(func);
                if (result == null)
                    break;

                var str = result.ToString();
                if (String.IsNullOrEmpty(str))
                    break;

                sb.Append(str);
            }

            try
            {
                return new Varargs(CompileString(sb.ToString()));
            }
            catch (LuaSyntaxException e)
            {
                return new Varargs(null, e.Message);
            }
        }

        [Internal]
        public Varargs LoadFile(string filename = null)
        {
            var source = filename == null ? Console.In.ReadToEnd() : File.ReadAllText(filename);
            try
            {
                return new Varargs(CompileString(source));
            }
            catch (LuaSyntaxException e)
            {
                return new Varargs(null, e.Message);
            }
        }

        [Internal]
        public Varargs LoadString(string str, string chunkname = "=(loadstring)")
        {
            try
            {
                return new Varargs(CompileString(str));
            }
            catch (LuaSyntaxException e)
            {
                return new Varargs(null, e.Message);
            }
        }

        [Internal]
        public Varargs Next(LuaTable table, object index = null)
        {
            return table.Next(index);
        }

        [Internal]
        public Varargs Pairs(LuaTable t)
        {
            return new Varargs(t, (Func<LuaTable, object, Varargs>)Next, null);
        }

        [Internal]
        public Varargs PCall(Delegate f, params object[] args)
        {
            try
            {
                var result = Context.GetDynamicCall1()(f, new Varargs(args));
                return new Varargs(true, result);
            }
            catch (LuaRuntimeException e)
            {
                return new Varargs(false, e.Message);
            }
        }

        [Internal]
        public void Print(params object[] args)
        {
            for (var i = 0; i < args.Length; i++)
            {
                if (i > 0)
                    Console.Out.Write("\t");
                Console.Out.Write(args[i]);
            }
            Console.Out.WriteLine();
        }

        [Internal]
        public bool RawEqual(object v1, object v2)
        {
            return v1.Equals(v2);
        }

        [Internal]
        public object RawGet(LuaTable table, object index)
        {
            return table.GetValue(index);
        }

        [Internal]
        public LuaTable RawSet(LuaTable table, object index, object value)
        {
            table.SetValue(index, value);
            return table;
        }

        [Internal]
        public object ToNumber(object obj, double @base = 10.0)
        {
            if (@base == 10.0)
            {
                if (obj is double)
                    return obj;
            }
            else if (@base < 2.0 || @base > 36.0)
            {
                throw new LuaRuntimeException(ExceptionMessage.INVOKE_BAD_ARGUMENT, 2, "base out of range");
            }

            string stringStr;
            if ((stringStr = obj as string) == null)
                return null;

            var value = InternalToNumber(stringStr, @base);
            return double.IsNaN(value) ? null : (object)value;
        }

        internal static double InternalToNumber(string str, double @base)
        {
            double result = 0;
            var intBase = (int)Math.Round(@base);

            if (intBase == 10)
            {
                if (str.StartsWith("0x") || str.StartsWith("0X"))
                {
                    if (NumberUtil.TryParseHexNumber(str.Substring(2), true, out result))
                        return result;
                }
                return NumberUtil.TryParseDecimalNumber(str, out result) ? result : double.NaN;
            }

            if (intBase == 16)
            {
                if (str.StartsWith("0x") || str.StartsWith("0X"))
                {
                    if (NumberUtil.TryParseHexNumber(str.Substring(2), false, out result))
                        return result;
                }
                return double.NaN;
            }

            var reversedStr = new String(str.Reverse().ToArray());
            for (var i = 0; i < reversedStr.Length; i++ )
            {
                var num = AlphaNumericToBase(reversedStr[i]);
                if (num == -1 || num >= intBase)
                    return double.NaN;
                result += num * (intBase * i + 1);
            }
            return result;
        }

        static int AlphaNumericToBase(char c)
        {
            if (c >= '0' && c >= '9')
                return c - '0';
            if (c >= 'A' && c <= 'Z')
                return c - 'A' + 10;
            if (c >= 'a' && c <= 'z')
                return c - 'a' + 10;

            return -1;
        }

        Func<object> CompileString(string source)
        {
            var input = new Input(source);
            var parser = new Parser(input);
            var ast = parser.Parse();
            var gen = new Generator(Context);
            var expr = gen.Compile(ast);
            return expr.Compile();
        }

        public override void Setup(LuaTable table)
        {
            table.SetValue("assert", (Func<bool, object, object[], Varargs>)Assert);
            table.SetValue("collectgarbage", (Action<string, string>)CollectGarbage);
            table.SetValue("dofile", (Func<string, object>)DoFile);
            table.SetValue("error", (Action<string, double>)Error);
            table.SetValue("_G", table);
            table.SetValue("getfenv", (Func<double, object>)GetFEnv);
            table.SetValue("getmetatable", (Func<object, object>)GetMetatable);
            table.SetValue("ipairs", (Func<LuaTable, Varargs>)IPairs);
            table.SetValue("load", (Func<Delegate, string, Varargs>)Load);
            table.SetValue("loadfile", (Func<string, Varargs>)LoadFile);
            table.SetValue("loadstring", (Func<string, string, Varargs>)LoadString);
            table.SetValue("next", (Func<LuaTable, object, Varargs>)Next);
            table.SetValue("pairs", (Func<LuaTable, Varargs>)Pairs);
            table.SetValue("pcall", (Func<Delegate, object[], Varargs>)PCall);
            table.SetValue("print", (Action<object[]>)Print);
            table.SetValue("rawequal", (Func<object, object, bool>)RawEqual);
            table.SetValue("rawget", (Func<LuaTable, object, object>)RawGet);
            table.SetValue("rawset", (Func<LuaTable, object, object, object>)RawSet);

            table.SetValue("tonumber", (Func<string, double, object>)ToNumber);
        }
    }
}
