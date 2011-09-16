﻿using System;
using System.Linq;
using System.Text;
using IronLua.Runtime;

namespace IronLua.Library
{
    class LuaString : Library
    {
        public LuaString(Context context) : base(context)
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
            if (plain.HasValue && plain.Value == true && init.HasValue)
            {
                int index = str.Substring(init.Value).IndexOf(pattern);
                return index != -1 ? new object[] {index, index+pattern.Length} : null;
            }
            throw new NotImplementedException();
        }

        public static string Format(string format, params object[] varargs)
        {
            return StringFormatter.Format(format, varargs);
        }

        internal object Concat(object left, object right)
        {
            if ((left is string || left is double) && (right is double || right is string))
                return String.Concat(left, right);

            return LuaOps.GetConcatMetamethod(Context, left, right);
        }

        public override void Setup(LuaTable table)
        {
            throw new NotImplementedException();
        }
    }
}
