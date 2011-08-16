using System;
using LinqExpression = System.Linq.Expressions.Expression;

namespace IronLua.Compiler.Ast
{
    class FunctionName : Node
    {
        public string[] Identifiers { get; private set; }
        public string Table { get; private set; }

        public FunctionName(string[] identifiers, string table)
        {
            Identifiers = identifiers;
            Table = table;
        }

        public override LinqExpression Compile(Scope scope)
        {
            throw new NotImplementedException();
        }
    }
}