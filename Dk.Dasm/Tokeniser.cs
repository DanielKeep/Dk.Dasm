﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Dk.Dasm
{
    public enum TokenType
    {
        Invalid,
        
        Eol,
        Eos,

        Number,
        Character,
        String,
        Identifier,
        Comment,
        HashInclude,
        HashMacro,
        IncludePath,

        Comma,
        Colon,
        At,
        Plus,
        Minus,
        LBracket,
        RBracket,
        Tilde,
    }

    public struct Token
    {
        public TokenType Type;
        public NewSourceLocation Location;
        public string Text;
        public object Value;
    }

    public class TokeniserOptions
    {
        public bool SignedNumbers = false;
        public bool UnderscoreInNumbers = false;
        public bool BinaryLiterals = false;
        public bool FormatPrefix = false;
        public bool LocalLabels = false;
    }

    public class Tokeniser
    {
        public Tokeniser(TokeniserOptions options)
        {
            var us = options.UnderscoreInNumbers ? "_" : "";

            string formatPrefix = "(?<format>)";

            if (options.FormatPrefix)
                formatPrefix = @"(?<format>[RrYyGgAaBbPpZzWw]([RrYyGgAaBbPpZzWw]([!])?)?)?";

            {
            	var pattern = "^[ \t]*";
            	WhitespaceRegex = new Regex(pattern);
            }
            {
            	var pattern = "^;.*";
            	CommentRegex = new Regex(pattern);
            }
            {
            	var pattern = @"^\r\n|\r|\n";
            	EolRegex = new Regex(pattern);
            }
            {
            	var pattern = "$";
            	EosRegex = new Regex(pattern);
            }
            {
                var pattern = "^";

                if (options.SignedNumbers)
                    pattern += "(?<sign>[-+]?)(";
                else
                    pattern += "(?<sign>)(";

                pattern += "(?<base>)(?<value>[0-9][0-9" + us + "]+)";
                pattern += "|0(?<base>x)(?<value>[0-9a-f" + us + "]+)";
                if (options.BinaryLiterals)
                    pattern += "|0(?<base>b)(?<value>[01" + us + "]+)";

                pattern += ")";

                NumberRegex = new Regex(pattern, RegexOptions.IgnoreCase);
            }
            {
                var pattern = "^";

                pattern += formatPrefix;
                pattern += @"\s*'(?<value>[^'\\]|\\'|\\[^']+)'";

                CharacterRegex = new Regex(pattern);
            }
            {
                var pattern = "^";

                pattern += formatPrefix;
                pattern += @"""(?<value>([^""\\]+|\\""|\\[^""]+)*)""";

                StringRegex = new Regex(pattern);
            }
            {
                var pattern = "^";

                pattern += "(?<value>";

                if (options.LocalLabels)
                    pattern += @"[.a-z0-9_]";
                else
                    pattern += @"[a-z_]";

                pattern += @"[a-z0-9_]*";
                pattern += ")";

                IdentifierRegex = new Regex(pattern, RegexOptions.IgnoreCase);
            }
            {
            	var pattern = "^#\s*include\s";
            	HashIncludeRegex = new Regex(pattern, RegexOptions.IgnoreCase);
            }
            {
            	var pattern = "^#\s*macro\s";
            	HashMacroRegex = new Regex(pattern, RegexOptions.IgnoreCase);
            }
            {
            	var pattern = "^(?<value>.+)|\"(?<value>[^\"]+)\"";
            	IncludePathRegex = new Regex(pattern);
            }
        }

        private TokeniserOptions options;

        private Regex WhitespaceRegex;
        private Regex CommentRegex;
        private Regex EolRegex;
        private Regex EosRegex;
        private Regex NumberRegex;
        private Regex CharacterRegex;
        private Regex StringRegex;
        private Regex IdentifierRegex;
        private Regex HashIncludeRegex;
        private Regex HashMacroRegex;
        private Regex IncludePathRegex;
        
        private delegate Token? Eater(ref Source src);
        private Eater forceNextEater = null;
        
        public bool TokeniseLine(ref Source src,
                                 ref List<Token> tokens,
                                 ref List<Token> comments)
        {
        	while (true)
        	{
        		var tok = NextToken(ref src);
        		if (tok.Type == TokenType.Comment)
        			comments.Add(tok);
        		
        		else if (tok.Type == TokenType.Eol)
        			return true;
        		
        		else if (tok.Type == TokenType.Eos)
        			return false;
        		
        		else
        			tokens.Add(tok);
        	}
        }
        
        public Token NextToken(ref Source src)
        {
        	DropWhitespace(src);
        	Token? tok = null;
        	
        	if (forceNextEater != null)
        	{
        		tok = forceNextEater(ref src);
        		forceNextEater = null;
        		if (!tok.HasValue)
        			throw new TokeniseException(src.Location,
        				"expected include path");
        		return tok.Value;
        	}
        	
        	if ((tok = EatEol(ref src)).HasValue) return tok.Value;
        	if ((tok = EatEos(ref src)).HasValue) return tok.Value;
        	if ((tok = EatHashInclude(ref src)).HasValue) return tok.Value;
        	if ((tok = EatHashMacro(ref src)).HasValue) return tok.Value;
        	if ((tok = EatSymbolicToken(ref src)).HasValue) return tok.Value;
        	if ((tok = EatCharacter(ref src)).HasValue) return tok.Value;
        	if ((tok = EatIdentifier(ref src)).HasValue) return tok.Value;
        	if ((tok = EatNumber(ref src)).HasValue) return tok.Value;
        	if ((tok = EatString(ref src)).HasValue) return tok.Value;
        	
        	throw new TokeniseException(src.Location, "unexpected character");
        }
        
        public void DropWhitespace(ref Source src)
        {
        	var m = WhitespaceRegex.Match(src.Text);
        	src.Advance(m.Length);
        }
        
        public Token? EatEol(ref Source src)
        {
        	var m = EolRegex.Match(src.Text);
        	if (!m.Success)
        		return null;
        	
        	var loc = src.Location + m.Length;
        	
        	return new Token()
        	{
        		Type = TokenType.Eol,
        		Location = loc,
        		Text = src.Advance(m.Length),
        		Value = null
        	};
        }
        
        public Token? EatEos(ref Source src)
        {
        	var m = EosRegex.Match(src.Text);
        	if (!m.Success)
        		return null;
        	
        	var loc = src.Location + m.Length;
        	
        	return new Token()
        	{
        		Type = TokenType.Eos,
        		Location = loc,
        		Text = src.Advance(m.Length),
        		Value = null
        	};
        }
        
        public Token? EatHashInclude(ref Source src)
        {
        	var m = HashIncludeRegex.Match(src.Text);
        	if (!m.Success)
        		return null;
        	
        	var loc = src.Location + m.Length;
        	
        	forceNextEater = EatIncludePath;
        	
        	return new Token()
        	{
        		Type = TokenType.HashInclude,
        		Location = loc,
        		Text = src.Advance(m.Length),
        		Value = null
        	};
        }
        
        public Token? EatHashMacro(ref Source src)
        {
        	var m = HashMacroRegex.Match(src.Text);
        	if (!m.Success)
        		return null;
        	
        	var loc = src.Location + m.Length;
        	
        	return new Token()
        	{
        		Type = TokenType.HashMacro,
        		Location = loc,
        		Text = src.Advance(m.Length),
        		ValueType = null
        	};
        }
        
        public Token? EatIncludePath(ref Source src)
        {
        	var m = IncludePathRegex.Match(src.Text);
        	if (!m.Success)
        		return null;
        	
        	var loc = src.Location + m.Length;
        	
        	return new Token()
        	{
        		Type = TokenType.IncludePath,
        		Location = loc,
        		Text = src.Advance(m.Length),
        		Value = m.Groups["value"].Value
        	};
        }

        public Token? EatNumber(ref Source src)
        {
            var m = NumberRegex.Match(src.Text);
            if (!m.Success)
                return null;

            var gSign = m.Groups["sign"];
            var gBase = m.Groups["base"];
            var gValue = m.Groups["value"];

            var neg = (gSign.Value == "-" ? true : false);
            var @base = baseFromPrefix(gBase.Value);
            UInt64 value;

            var loc = src.Location + m.Length;

            try
            {
                value = Convert.ToUInt64(gValue.Value, @base);
            }
            catch (OverflowException ex)
            {
                throw new TokeniseException(src.Location, "number too large", ex);
            }

            Object oValue = null;
            if (neg)
            {
                if (value > (UInt64)(-(Int32)Int16.MinValue))
                    throw new TokeniseException(src.Location, "number too large");
                var v = -(Int64)value;
                oValue = (UInt16)v;
            }
            else
            {
                if (value > UInt16.MaxValue)
                    throw new TokeniseException(src.Location, "number too large");
                oValue = (UInt16)value;
            }

            return new Token()
            {
                Type = TokenType.Number,
                Location = loc,
                Text = src.Advance(m.Length),
                Value = oValue,
            };
        }

        public Token? EatCharacter(ref Source src)
        {
            var m = CharacterRegex.Match(src.Text);
            if (!m.Success)
                return null;

            var gFormat = m.Groups["format"];
            var gValue = m.Groups["value"];

            var loc = src.Location + m.Length;

            var format = formatFromPrefix(gFormat.Value);
            ushort value = 0;

            if (gValue.Length == 1)
                value = gValue.Value[0];
            else
            {
                int len;
                try
                {
                    // If we got null, then we've got an invalid character literal
                    // anyway...
                    value = parseCharacter(gValue.Value, out len).Value;
                }
                catch (Exception ex)
                {
                    throw new TokeniseException(loc, "invalid character literal", ex);
                }

                if (len != gValue.Length)
                    throw new TokeniseException(loc, "invalid character literal");
            }

            // Add in format
            value = (ushort)(format | value);

            return new Token()
            {
                Type = TokenType.Character,
                Location = loc,
                Text = src.Advance(loc.Span),
                Value = value
            };
        }

        public Token? EatString(ref Source src)
        {
            var m = StringRegex.Match(src.Text);
            if (!m.Success)
                return null;

            var gFormat = m.Groups["format"];
            var gValue = m.Groups["value"];

            var loc = src.Location + m.Length;

            var format = formatFromPrefix(gFormat.Value);
            string value = "";
            
            var input = gValue.Value;
            
            while (input.Length > 0)
            {
            	int len;
            	var ch = parseCharacter(input, out len);
            	
            	input = input.Substring(len);
            	
            	if (!ch.HasValue)
            		continue;
            	
            	value += (char)(format | (ushort)ch.Value);
            }
            
            return new Token()
            {
                Type = TokenType.String,
                Location = loc,
                Text = src.Advance(loc.Span),
                Value = value
            };
        }
        
        public Token? EatIdentifier(ref Source src)
        {
            var m = IdentifierRegex.Match(src.Text);
            if (!m.Success)
                return null;

            var gValue = m.Groups["value"];

            var loc = src.Location + m.Length;

            return new Token()
            {
                Type = TokenType.Identifier,
                Location = loc,
                Text = src.Advance(loc.Span),
                Value = gValue.Value
            };
        }

        public Token? EatSymbolicToken(ref Source src)
        {
            var c0 = src.Get(0);
            var tt = TokenType.Invalid;

            switch (c0)
            {
                case ',': tt = TokenType.Comma; break;
                case ':': tt = TokenType.Colon; break;
                case '@': tt = TokenType.At; break;
                case '+': tt = TokenType.Plus; break;
                case '-': tt = TokenType.Minus; break;
                case '[': tt = TokenType.LBracket; break;
                case ']': tt = TokenType.RBracket; break;
                case '~': tt = TokenType.Tilde; break;
                default: break;
            }

            if (tt == TokenType.Invalid)
                return null;

            return new Token() { Type = tt, Location = src.Location, Text = src.Advance(1) };
        }

        private int baseFromPrefix(string prefix)
        {
            switch (prefix)
            {
                case "": goto case "d";
                case "D": goto case "D";
                case "d": return 10;

                case "B": goto case "b";
                case "b": return 2;

                case "H": goto case "h";
                case "h": return 16;

                default:
                    throw new ArgumentOutOfRangeException("prefix");
            }
        }

        private char? parseCharacter(string text, out int length)
        {
            length = 0;
            if (text.SafeNth(0) != '\\')
            	return text.SafeNth(0);

            // Common case
            length = 2;
            var c1 = text.SafeNth(1);
            switch (c1)
            {
                case '\r': goto case '\n';
                case '\n': return null;

                case '\\': return '\\';
                case '\'': return '\'';
                case '\"': return '\"';
                case 'a': return '\a';
                case 'b': return '\b';
                case 'e': return '\x1b';
                case 'f': return '\f';
                case 'n': return '\n';
                case 'r': return '\r';
                case 't': return '\t';
                case 'v': return '\v';

                case 'x':
                    {
                        length = 4;
                        var x = text.SafeSlice(2, 4);
                        var v = Convert.ToByte(x, 16);
                        return (char)v;
                    }

                default:
                    throw new ArgumentOutOfRangeException("text");
            }
        }

        private ushort formatFromPrefix(string prefix)
        {
            var fg = prefix.SafeNth(0);
            var bg = prefix.SafeNth(1);
            var bl = prefix.SafeNth(2);

            ushort format = 0;

            if (fg != Source.EOS) format |= (ushort)(colourCode(fg) << 12);
            if (bg != Source.EOS) format |= (ushort)(colourCode(bg) << 8);
            if (bl == '!') format |= (ushort)(0x1 << 7);

            return format;
        }

        private int colourCode(char code)
        {
            switch (code)
            {
                case 'R': return 0x8 | 0x4 | 0x0 | 0x0;
                case 'Y': return 0x8 | 0x4 | 0x2 | 0x0;
                case 'G': return 0x8 | 0x0 | 0x2 | 0x0;
                case 'A': return 0x8 | 0x0 | 0x2 | 0x1;
                case 'B': return 0x8 | 0x0 | 0x0 | 0x1;
                case 'P': return 0x8 | 0x4 | 0x0 | 0x1;
                case 'Z': return 0x8 | 0x0 | 0x0 | 0x0;
                case 'W': return 0x8 | 0x4 | 0x2 | 0x1;
                case 'r': return 0x0 | 0x4 | 0x0 | 0x0;
                case 'y': return 0x0 | 0x4 | 0x2 | 0x0;
                case 'g': return 0x0 | 0x0 | 0x2 | 0x0;
                case 'a': return 0x0 | 0x0 | 0x2 | 0x1;
                case 'b': return 0x0 | 0x0 | 0x0 | 0x1;
                case 'p': return 0x0 | 0x4 | 0x0 | 0x1;
                case 'z': return 0x0 | 0x0 | 0x0 | 0x0;
                case 'w': return 0x0 | 0x4 | 0x2 | 0x1;

                default:
                    throw new ArgumentOutOfRangeException("code");
            }
        }
    }

    public class TokeniseException : Exception
    {
        public TokeniseException(NewSourceLocation location, string what) { }
        public TokeniseException(NewSourceLocation location, string what, Exception inner) { }
    }
}
