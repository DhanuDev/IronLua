using System;
using LinqExpression = System.Linq.Expressions.Expression;

namespace IronLua.Compiler.Ast
{
    abstract class Field : Node
    {
        public class MemberExpr : Field
        {
            public Expression Member { get; private set; }
            public Expression Value { get; private set; }

            public MemberExpr(Expression member, Expression value)
            {
                Member = member;
                Value = value;
            }

            public override LinqExpression Compile(Scope scope)
            {
                throw new NotImplementedException();
            }
        }

        public class MemberId : Field
        {
            public string Member { get; private set; }
            public Expression Value { get; private set; }

            public MemberId(string member, Expression value)
            {
                Member = member;
                Value = value;
            }

            public override LinqExpression Compile(Scope scope)
            {
                throw new NotImplementedException();
            }
        }

        public class Normal : Field
        {
            public Expression Value { get; private set; }

            public Normal(Expression value)
            {
                Value = value;
            }

            public override LinqExpression Compile(Scope scope)
            {
                throw new NotImplementedException();
            }
        }
    }
}