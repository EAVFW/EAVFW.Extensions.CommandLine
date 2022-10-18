using System;
using System.CommandLine.Parsing;

namespace System.CommandLine
{
    public static class Extensions
    {
        public static T GetValue<T>(this Argument<T> argument, ParseResult parser)
            => parser.GetValueForArgument(argument);
        public static T GetValue<T>(this Option<T> argument, ParseResult parser)
           => parser.GetValueForOption(argument);
    }
}