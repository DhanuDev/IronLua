﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using IronLua_CSharp.Compiler.Ast;

namespace IronLua_CSharp.Compiler
{
    class Parser
    {
        const int UNARY_OP_PRIORITY = 8;
        const NumberStyles HEX_NUMBER_STYLE = NumberStyles.AllowHexSpecifier;
        const NumberStyles DECIMAL_NUMBER_STYLE = NumberStyles.AllowDecimalPoint |
                                                  NumberStyles.AllowExponent |
                                                  NumberStyles.AllowTrailingSign;

        static readonly CultureInfo cultureInfo = CultureInfo.InvariantCulture ;

        static readonly Dictionary<Symbol, UnaryOp> unaryOps =
            new Dictionary<Symbol, UnaryOp>
                {
                    {Symbol.Minus, UnaryOp.Negative},
                    {Symbol.Not,   UnaryOp.Not},
                    {Symbol.Hash,  UnaryOp.Length}
                };

        static readonly Dictionary<Symbol, BinaryOp> binaryOps =
            new Dictionary<Symbol, BinaryOp>
                {
                    {Symbol.Or,           BinaryOp.Or},
                    {Symbol.And,          BinaryOp.And},
                    {Symbol.Less,         BinaryOp.Less},
                    {Symbol.Greater,      BinaryOp.Greater},
                    {Symbol.LessEqual,    BinaryOp.LessEqual},
                    {Symbol.GreaterEqual, BinaryOp.GreaterEqual},
                    {Symbol.DotDot,       BinaryOp.Concat},
                    {Symbol.Plus,         BinaryOp.Add},
                    {Symbol.Minus,        BinaryOp.Subtract},
                    {Symbol.Star,         BinaryOp.Multiply},
                    {Symbol.Slash,        BinaryOp.Divide},
                    {Symbol.Percent,      BinaryOp.Mod},
                    {Symbol.Carrot,       BinaryOp.Raise}
                };

        static readonly Dictionary<BinaryOp, Tuple<int, int>> binaryOpPriorities =
            new Dictionary<BinaryOp, Tuple<int, int>>
                {
                    {BinaryOp.Or,           new Tuple<int, int>(1, 1)},
                    {BinaryOp.And,          new Tuple<int, int>(2, 2)},
                    {BinaryOp.Less,         new Tuple<int, int>(3, 3)},
                    {BinaryOp.Greater,      new Tuple<int, int>(3, 3)},
                    {BinaryOp.LessEqual,    new Tuple<int, int>(3, 3)},
                    {BinaryOp.GreaterEqual, new Tuple<int, int>(3, 3)},
                    {BinaryOp.Concat,       new Tuple<int, int>(5, 4)}, // Left associative
                    {BinaryOp.Add,          new Tuple<int, int>(6, 6)},
                    {BinaryOp.Subtract,     new Tuple<int, int>(6, 6)},
                    {BinaryOp.Multiply,     new Tuple<int, int>(7, 7)},
                    {BinaryOp.Divide,       new Tuple<int, int>(7, 7)},
                    {BinaryOp.Mod,          new Tuple<int, int>(7, 7)},
                    {BinaryOp.Raise,        new Tuple<int, int>(9, 8)}  // Left associative
                };

        Input input;
        Lexer lexer;

        public Parser(Input input)
        {
            this.input = input;
            lexer = new Lexer(input);
        }

        public Block Parse()
        {
            var block = Block();
            lexer.Expect(Symbol.Eof);
            return block;
        }

        string[] IdentifierList()
        {
            var identifiers = new List<string> {lexer.ExpectLexeme(Symbol.String)};

            while (lexer.TryConsume(Symbol.Comma))
                identifiers.Add(lexer.ExpectLexeme(Symbol.Identifier));

            return identifiers.ToArray();
        }

        Expression[] ExpressionList()
        {
            var expressions = new List<Expression> { Expression() };

            while (lexer.TryConsume(Symbol.Comma))
                expressions.Add(Expression());

            return expressions.ToArray();
        }

        Variable[] VariableList(Variable oldVariable = null)
        {
            var variables = new List<Variable> {oldVariable ?? Variable()};

            while (lexer.Current.Symbol == Symbol.Comma)
                variables.Add(Variable());

            return variables.ToArray();
        }

        Field[] Table()
        {
            lexer.Expect(Symbol.LeftBrace);
            var fields = new List<Field>();

            var loop = true;
            while (loop)
            {
                if (lexer.Current.Symbol == Symbol.RightBrace)
                    break;
                fields.Add(Field());

                switch (lexer.Current.Symbol)
                {
                    case Symbol.Comma:
                    case Symbol.SemiColon:
                        lexer.Consume();
                        break;
                    case Symbol.RightBrace:
                        loop = false;
                        break;
                    default:
                        throw new CompileException(input, ExceptionMessage.UNEXPECTED_SYMBOL, lexer.Current.Symbol);
                }
            }

            lexer.Expect(Symbol.RightBrace);
            return fields.ToArray();
        }

        double NumberLiteral()
        {
            var number = lexer.ExpectLexeme(Symbol.String);
            try
            {
                if (number.StartsWith("0x"))
                    return HexNumber(number.Substring(2));
                return DecimalNumber(number);
            }
            catch(FormatException)
            {
                throw new CompileException(input, ExceptionMessage.MALFORMED_NUMBER, number);
            }
            catch(OverflowException)
            {
                throw new CompileException(input, ExceptionMessage.MALFORMED_NUMBER, number);
            }
        }

        double DecimalNumber(string number)
        {
            return Double.Parse(number, DECIMAL_NUMBER_STYLE, cultureInfo);
        }

        double HexNumber(string number)
        {
            var exponentIndex = number.IndexOfAny(new[] { 'p', 'P' });
            if (exponentIndex == -1)
                return UInt64.Parse(number, HEX_NUMBER_STYLE, cultureInfo);

            var hexPart = number.Substring(0, exponentIndex);
            var exponentPart = number.Substring(exponentIndex + 1);
            var hexNumber = UInt64.Parse(hexPart, HEX_NUMBER_STYLE, cultureInfo);
            var exponentNumber = UInt64.Parse(exponentPart, HEX_NUMBER_STYLE, cultureInfo);

            return hexNumber * Math.Pow(exponentNumber, 2.0);
        }

        Variable Variable()
        {
            var variable = PrefixExpression().LiftVariable();
            if (variable == null)
                throw new CompileException(input, ExceptionMessage.UNEXPECTED_SYMBOL, lexer.Current.Symbol);
            return variable;
        }

        Field Field()
        {
            switch (lexer.Current.Symbol)
            {
                case Symbol.LeftBrack:
                    lexer.Consume();
                    var member = Expression();
                    lexer.Expect(Symbol.RightBrack);
                    lexer.Expect(Symbol.Equal);
                    var value = Expression();
                    return new Field.MemberExpr(member, value);

                default:
                    var expression = Expression();
                    if (lexer.Current.Symbol != Symbol.Equal)
                        return new Field.Normal(expression);

                    lexer.Consume();
                    var memberId = expression.LiftIdentifier();
                    if (memberId != null)
                        return new Field.MemberId(memberId, Expression());
                    throw new CompileException(input, ExceptionMessage.UNEXPECTED_SYMBOL, lexer.Current.Symbol);
            }
        }

        Elseif Elseif()
        {
            lexer.Expect(Symbol.Elseif);
            var test = Expression();
            lexer.Expect(Symbol.Then);
            var body = Block();
            return new Elseif(test, body);
        }

        Arguments Arguments()
        {
            switch (lexer.Current.Symbol)
            {
                case Symbol.LeftParen:
                    if (lexer.Current.Line != lexer.Last.Line)
                        throw new CompileException(input, ExceptionMessage.AMBIGUOUS_SYNTAX_FUNCTION_CALL);
                    lexer.Consume();
                    var arguments = ExpressionList();
                    lexer.Expect(Symbol.RightParen);
                    return new Arguments.Normal(arguments);

                case Symbol.LeftBrace:
                    return new Arguments.Table(Table());

                case Symbol.String:
                    return new Arguments.String(lexer.ExpectLexeme(Symbol.String));

                default:
                    throw new CompileException(input, ExceptionMessage.UNEXPECTED_SYMBOL, lexer.Current.Symbol);
            }
        }

        FunctionBody FunctionBody()
        {
            lexer.Expect(Symbol.LeftParen);
            if (lexer.TryConsume(Symbol.RightParen))
                return new FunctionBody(new string[] {}, false, Block());

            if (lexer.TryConsume(Symbol.DotDotDot))
                return new FunctionBody(new string[] {}, true, Block());

            var parameters = new List<string> {lexer.ExpectLexeme(Symbol.String)};
            var varargs = false;
            while (!varargs && lexer.TryConsume(Symbol.Comma))
            {
                if (lexer.Current.Symbol == Symbol.Identifier)
                    parameters.Add(lexer.ConsumeLexeme());
                else if (lexer.Current.Symbol == Symbol.DotDotDot)
                    varargs = true;
            }

            lexer.Expect(Symbol.RightParen);
            return new FunctionBody(parameters.ToArray(), varargs, Block());
        }

        FunctionName FunctionName()
        {
            var identifiers = new List<string> { lexer.ExpectLexeme(Symbol.Identifier) };

            while (lexer.TryConsume(Symbol.Comma))
                identifiers.Add(lexer.ExpectLexeme(Symbol.Identifier));

            var table = lexer.TryConsume(Symbol.Colon) ? lexer.ExpectLexeme(Symbol.Identifier) : null;

            return new FunctionName(identifiers.ToArray(), table);
        }

        PrefixExpression PrefixExpression()
        {
            PrefixExpression left;
            switch (lexer.Current.Symbol)
            {
                case Symbol.Identifier:
                    left = new PrefixExpression.Variable(new Variable.Identifier(lexer.ConsumeLexeme()));
                    break;
                case Symbol.LeftParen:
                    lexer.Consume();
                    left = new PrefixExpression.Expression(Expression());
                    lexer.ExpectLexeme(Symbol.RightParen);
                    break;
                default:
                    throw new CompileException(input, ExceptionMessage.UNEXPECTED_SYMBOL, lexer.Current.Symbol);
            }

            var loop = true;
            while (loop)
            {
                string identifier;
                switch (lexer.Current.Symbol)
                {
                    case Symbol.LeftBrack:
                        lexer.Consume();
                        left = new PrefixExpression.Variable(new Variable.MemberExpr(left, Expression()));
                        lexer.Expect(Symbol.RightBrack);
                        break;

                    case Symbol.Dot:
                        lexer.Consume();
                        identifier = lexer.ExpectLexeme(Symbol.Identifier);
                        left = new PrefixExpression.Variable(new Variable.MemberId(left, identifier));
                        break;

                    case Symbol.Colon:
                        lexer.Consume();
                        identifier = lexer.ExpectLexeme(Symbol.Identifier);
                        var arguments = Arguments();
                        left = new PrefixExpression.FunctionCall(new FunctionCall.Table(left, identifier, arguments));
                        break;

                    case Symbol.LeftParen:
                    case Symbol.LeftBrace:
                    case Symbol.String:
                        left = new PrefixExpression.FunctionCall(new FunctionCall.Normal(left, Arguments()));
                        break;

                    default:
                        loop = false;
                        break;
                }
            }

            return left;
        }

        Expression Expression()
        {
            var left = SimpleExpression();

            while (true)
            {
                left = BinaryExpression(left, 0);
                if (!binaryOps.ContainsKey(lexer.Current.Symbol))
                    break;
            }

            return left;
        }

        Expression SimpleExpression()
        {
            switch (lexer.Current.Symbol)
            {
                case Symbol.Nil:
                    lexer.Consume();
                    return new Expression.Nil();
                case Symbol.True:
                    lexer.Consume();
                    return new Expression.Boolean(true);
                case Symbol.False:
                    lexer.Consume();
                    return new Expression.Boolean(false);
                case Symbol.Number:
                    return new Expression.Number(NumberLiteral());
                case Symbol.String:
                    return new Expression.String(lexer.ConsumeLexeme());
                case Symbol.DotDotDot:
                    lexer.Consume();
                    return new Expression.Varargs();
                case Symbol.Function:
                    return new Expression.Function(FunctionBody());
                case Symbol.Identifier:
                case Symbol.LeftParen:
                    return new Expression.Prefix(PrefixExpression());
                case Symbol.LeftBrace:
                    return new Expression.Table(Table());
                
                default:
                    UnaryOp unaryOp;
                    if (!unaryOps.TryGetValue(lexer.Current.Symbol, out unaryOp))
                        throw new CompileException(input, ExceptionMessage.UNEXPECTED_SYMBOL, lexer.Current.Symbol);

                    lexer.Consume();
                    var expression = BinaryExpression(SimpleExpression(), UNARY_OP_PRIORITY);
                    return new Expression.UnaryOp(unaryOp, expression);
            }
        }

        Expression BinaryExpression(Expression left, int limit)
        {
            BinaryOp binaryOp;
            if (!binaryOps.TryGetValue(lexer.Current.Symbol, out binaryOp))
                return left;

            var priority = binaryOpPriorities[binaryOp];
            if (priority.Item1 < limit)
                return left;

            var right = BinaryExpression(SimpleExpression(), priority.Item2);
            return new Expression.BinaryOp(binaryOp, left, right);
        }

        Block Block()
        {
            var statements = new List<Statement>();

            while (true)
            {
                switch (lexer.Current.Symbol)
                {
                    case Symbol.Do:
                        statements.Add(Do());
                        break;
                    case Symbol.While:
                        statements.Add(While());
                        break;
                    case Symbol.Repeat:
                        statements.Add(Repeat());
                        break;
                    case Symbol.If:
                        statements.Add(If());
                        break;
                    case Symbol.For:
                        statements.Add(For());
                        break;
                    case Symbol.Function:
                        statements.Add(Function());
                        break;
                    case Symbol.Local:
                        statements.Add(Local());
                        break;
                    case Symbol.Identifier:
                    case Symbol.LeftParen:
                        statements.Add(AssignOrFunctionCall());
                        break;
                    case Symbol.Return:
                        return new Block(statements.ToArray(), Return());
                    case Symbol.Break:
                        return new Block(statements.ToArray(), new LastStatement.Break());
                    default:
                        throw new CompileException(input, ExceptionMessage.UNEXPECTED_SYMBOL, lexer.Current.Symbol);
                }

                lexer.TryConsume(Symbol.SemiColon);
            }
        }

        LastStatement Return()
        {
            lexer.Expect(Symbol.Return);
            return new LastStatement.Return(ExpressionList());
        }

        Statement AssignOrFunctionCall()
        {
            var prefixExpr = PrefixExpression();

            switch (lexer.Current.Symbol)
            {
                case Symbol.Comma:
                case Symbol.Equal:
                    return Assign(prefixExpr);
                case Symbol.Colon:
                case Symbol.LeftParen:
                case Symbol.LeftBrace:
                case Symbol.String:
                    return FunctionCall(prefixExpr);
                default:
                    throw new CompileException(input, ExceptionMessage.UNEXPECTED_SYMBOL, lexer.Current.Symbol);
            }
        }

        Statement FunctionCall(PrefixExpression prefixExpr)
        {
            var functionCall = prefixExpr.LiftFunctionCall();
            if (functionCall == null)
                throw new CompileException(input, ExceptionMessage.UNEXPECTED_SYMBOL, lexer.Current.Symbol);

            return new Statement.FunctionCall(functionCall);
        }

        Statement Assign(PrefixExpression prefixExpr)
        {
            var variable = prefixExpr.LiftVariable();
            if (variable == null)
                throw new CompileException(input, ExceptionMessage.UNEXPECTED_SYMBOL, lexer.Current.Symbol);

            var variables = lexer.TryConsume(Symbol.Comma) ? VariableList(variable) : new[] {variable};
            lexer.Expect(Symbol.Equal);
            var expressions = ExpressionList();

            return new Statement.Assign(variables, expressions);
        }

        Statement Local()
        {
            lexer.Expect(Symbol.Local);

            if (lexer.Current.Symbol == Symbol.Function)
                return LocalFunction();
            if (lexer.Current.Symbol == Symbol.Identifier)
                return LocalAssign();

            throw new CompileException(input, ExceptionMessage.UNEXPECTED_SYMBOL, lexer.Current.Symbol);
        }

        Statement LocalAssign()
        {
            var identifiers = IdentifierList();
            var values = lexer.TryConsume(Symbol.Equal) ? ExpressionList() : null;
            return new Statement.LocalAssign(identifiers, values);
        }

        Statement LocalFunction()
        {
            lexer.Expect(Symbol.Function);
            var identifier = lexer.ExpectLexeme(Symbol.Identifier);
            return new Statement.LocalFunction(identifier, FunctionBody());
        }

        Statement Function()
        {
            lexer.Expect(Symbol.Function);
            return new Statement.Function(FunctionName(), FunctionBody());
        }

        Statement For()
        {
            lexer.Expect(Symbol.For);
            if (lexer.Next.Symbol == Symbol.Comma || lexer.Next.Symbol == Symbol.In)
                return ForIn();
            return ForNormal();
        }

        Statement ForNormal()
        {
            var identifier = lexer.ExpectLexeme(Symbol.Identifier);
            lexer.Expect(Symbol.Equal);
            var var = Expression();
            lexer.Expect(Symbol.Comma);
            var limit = Expression();
            var step = lexer.TryConsume(Symbol.Comma) ? Expression() : null;

            lexer.Expect(Symbol.Do);
            var body = Block();
            lexer.Expect(Symbol.End);

            return new Statement.For(identifier, var, limit, step, body);
        }

        Statement ForIn()
        {
            var identifiers = IdentifierList();
            lexer.Expect(Symbol.In);
            var values = ExpressionList();

            lexer.Expect(Symbol.Do);
            var body = Block();
            lexer.Expect(Symbol.End);

            return new Statement.ForIn(identifiers, values, body);
        }

        Statement If()
        {
            lexer.Expect(Symbol.If);
            var test = Expression();
            lexer.Expect(Symbol.Then);
            var body = Block();

            var elseifs = new List<Elseif>();
            while (lexer.Current.Symbol == Symbol.Elseif)
                elseifs.Add(Elseif());

            var elseBody = lexer.TryConsume(Symbol.Else) ? Block() : null;

            return new Statement.If(test, body, elseifs.ToArray(), elseBody);
        }

        Statement Repeat()
        {
            lexer.Expect(Symbol.Repeat);
            var body = Block();
            lexer.Expect(Symbol.Until);
            var test = Expression();
            return new Statement.Repeat(body, test);
        }

        Statement While()
        {
            lexer.Expect(Symbol.While);
            var test = Expression();
            lexer.Expect(Symbol.Do);
            var body = Block();
            lexer.Expect(Symbol.End);
            return new Statement.While(test, body);
        }

        Statement Do()
        {
            lexer.Expect(Symbol.Do);
            var body = Block();
            lexer.Expect(Symbol.End);
            return new Statement.Do(body);
        }
    }
}
