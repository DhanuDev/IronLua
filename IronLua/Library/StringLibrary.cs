﻿using System;
using System.Linq;
using System.Text;
using IronLua.Runtime;

namespace IronLua.Library
{
    class StringLibrary : Library
    {
        public StringLibrary(Context context) : base(context)
        {
        }

        public static double[] Byte(string str, int? i = null, int? j = null)
        {
            char[] chars;
            if (!i.HasValue)
                chars = str.ToCharArray();
            else if (!j.HasValue)
                chars = str.Substring(i.Value).ToCharArray();
            else
                chars = str.Substring(i.Value, j.Value).ToCharArray();

            return chars.Select(c => (double)c).ToArray();
        }

        public static string Char(params double[] varargs)
        {
            var sb = new StringBuilder(varargs.Length);
            foreach (var arg in varargs)
                sb.Append((char) arg);
            return sb.ToString();
        }

        public static string Dump(object function)
        {
            throw new NotImplementedException();
        }

        public static object[] Find(string str, string pattern, int? init = 1, bool? plain = false)
        {
            if (plain.HasValue && plain.Value && init.HasValue)
            {
                var index = str.Substring(init.Value).IndexOf(pattern);
                return index != -1 ? new object[] {index, index+pattern.Length} : null;
            }
            throw new NotImplementedException();
        }

        public static string Format(string format, params object[] varargs)
        {
            return StringFormatter.Format(format, varargs);
        }

        public override void Setup(LuaTable table)
        {
            throw new NotImplementedException();
        }
    }
}
