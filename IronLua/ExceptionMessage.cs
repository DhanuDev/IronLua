﻿namespace IronLua
{
    static class ExceptionMessage
    {
        // Syntax
        public const string UNEXPECTED_EOF = "Unexpected end of file";
        public const string UNEXPECTED_EOS = "Unexpected end of string";
        public const string UNEXPECTED_CHAR = "Unexpected '{0}'";
        public const string UNKNOWN_PUNCTUATION = "Unknown punctuation '{0}'";
        public const string INVALID_LONG_STRING_DELIMTER = "Invalid long string delimter '{0}'";
        public const string UNEXPECTED_SYMBOL = "Unexpected symbol '{0}'";
        public const string EXPECTED_SYMBOL = "Unexpected symbol '{0}', expected '{1}'";
        public const string MALFORMED_NUMBER = "Malformed number '{0}'";
        public const string AMBIGUOUS_SYNTAX_FUNCTION_CALL = "Ambiguous syntax (function call or new statement)";

        // Runtime
        public const string FOR_VALUE_NOT_NUMBER = "For loop {0} must be a number";    

        // Library
        public const string STRING_FORMAT_BAD_ARGUMENT = "Bad argument #{0} to 'format' (number expected, got string)";
        public const string STRING_FORMAT_INVALID_OPTION = "Invalid option '{0}', to 'format'";
    }
}
