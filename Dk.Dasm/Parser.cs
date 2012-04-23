using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Dk.Dasm
{
    public struct Source
    {
        public const char EOS = '\x04';

        public NewSourceLocation Location;
        public string Text;

        public char Advance()
        {
            throw new NotImplementedException();
        }
        public string Advance(int chars)
        {
            throw new NotImplementedException();
        }

        public char Get(int index)
        {
            if (index < Text.Length)
                return Text[index];
            else
                return EOS;
        }
    }

    public static class SourceExtensions
    {
        public static char SafeNth(this string s, int index)
        {
            if (index < s.Length)
                return s[index];
            else
                return '\x04';
        }

        public static string SafeSlice(this string s, int start, int end)
        {
            if (start < s.Length && end <= s.Length && start <= end)
                return s.Substring(start, end - start);
            else
                return "";
        }
    }

    public class LiteParser
    {
        public struct State
        {
            public Source Source;
        }

        public AstProgram ParseProgram(ref State state)
        {
            /*
             * - Extract comment, add to comment list.
             * - If line is empty, next.
             * - Parse statement.
             *   - If got a statement,
             *     - Attach comment list.
             *     - Clear comment list.
             *     - Add statement to program.
             *   - If caught an exception,
             *     - Add exception to error list.
             *     - Drop tokens until EOL or EOF found.
             */
            var comments = new List<AstComment>();
            var lines = new List<AstStatement>();
            var errors = new List<ParseException>();

            return null;
        }
    }

    public class ParseException : Exception
    {
        public ParseException(NewSourceLocation location, string what) { }
    }

    public abstract class AstNode { }
    public class AstProgram : AstNode { }
    public class AstComment : AstNode { }
    public abstract class AstStatement : AstNode { }
    public abstract class AstDirective : AstStatement { }
    public class AstHashInclude : AstDirective { }
    public class AstHashMacro : AstDirective { }
    public class AstLabel : AstStatement { }
    public class AstMacro : AstStatement { }
    public class AstInstruction : AstStatement { }
    public class AstOpcode : AstNode { }
    public abstract class AstArgument : AstNode { }
    public class AstRegArg : AstArgument { }
    public class AstRegLookupArg : AstArgument { }
    public class AstRegOffLookupArg : AstArgument { }
    public class AstStackArg : AstArgument { }
    public class AstLiteralArg : AstArgument { }
    public class AstLiteralLookupArg : AstArgument { }
    public class AstData : AstStatement { }
    public abstract class AstLiteral : AstNode { }
    public class AstStringLiteral : AstLiteral { }
    public class AstLabelLiteral : AstLiteral { }
    public class AstDiffLiteral : AstLiteral { }
    public abstract class AstWordLiteral : AstLiteral { }
    public class AstNumber : AstWordLiteral { }
    public class AstChar : AstWordLiteral { }

    public struct NewSourceLocation
    {
        public string Name;
        public int Line, Column, Span;

        public static NewSourceLocation operator +(NewSourceLocation loc, int Span)
        {
            return new NewSourceLocation()
            {
                Name = loc.Name,
                Line = loc.Line,
                Column = loc.Column,
                Span = loc.Span + Span
            };
        }
    }
}
