using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Irony.Parsing;

namespace Dk.Dasm
{
    /// <summary>
    /// Dasm Grammar definition.
    /// </summary>
    [Language("DASM Ex", "0.1", "DCPU-16 extended assembly language")]
    public class DasmGrammar : Grammar
    {
        public DasmGrammar()
            : this(Options.Extended)
        {
        }

        public DasmGrammar(Options options)
            : base(caseSensitive: false)
        {
            #region Terminals
            var numberLit = CreateDasmNumber("number", options);
            var charLit = CreateDasmCharacter("character", options);
            var stringLit = CreateDasmString("string", options);
            var ident = CreateDasmIdentifier("identifier", options);

            var comment = CreateDasmComment("comment", options);
            base.NonGrammarTerminals.Add(comment);

            var comma = ToTerm(",");
            var colon = ToTerm(":");
            var atsym = ToTerm("@");
            var plus = ToTerm("+");
            var minus = ToTerm("-");
            var lbracket = ToTerm("[");
            var rbracket = ToTerm("]");

            MarkPunctuation(comma, colon, atsym, lbracket, rbracket);
            #endregion

            #region Non-Terminals
            var Program = new NonTerminal("Program");
            var Line = new NonTerminal("Line");
            var Label = new NonTerminal("Label");
            var LinePostLabel = new NonTerminal("LinePostLabel");
            var AddressFix = new NonTerminal("AddressFix");
            var BasicInstruction = new NonTerminal("BasicInstruction");
            var ExtInstruction = new NonTerminal("ExtInstruction");
            var Data = new NonTerminal("Data");
            var BasicArgument = new NonTerminal("BasicArgument");
            var DataArguments = new NonTerminal("DataArguments");
            var DataValue = new NonTerminal("DataValue");
            var Reg = new NonTerminal("Register");
            var GeneralReg = new NonTerminal("GeneralRegister");
            var SpecialReg = new NonTerminal("SpecialRegister");
            var RegLookup = new NonTerminal("RegisterLookup");
            var RegOffLookup = new NonTerminal("RegisterOffsetLookup");
            var StackOp = new NonTerminal("StackOp");
            var LiteralWord = new NonTerminal("LiteralWord");
            var LiteralLookup = new NonTerminal("LiteralLookup");
            #endregion

            #region Production Rules
            this.Root = Program;

            Program.Rule = MakeStarRule(Program, Line);

            Line.Rule = Named("OptLabel", Label | Empty)
                + Named("OptInstruction", LinePostLabel | Empty)
                + NewLine;
            Line.ErrorRule = SyntaxError + NewLine;

            Label.Rule = colon + ident;

            MarkTransient(LinePostLabel);
            LinePostLabel.Rule = BasicInstruction
                    | ExtInstruction
                    | AddressFix
                    | Data;

            AddressFix.Rule = atsym + LiteralWord;

            BasicInstruction.Rule = Named("Opcode",
                    ToTerm("set") | "add" | "sub" | "mul" | "div" | "mod" | "shl" | "shr"
                    | "and" | "bor" | "xor" | "ife" | "ifn" | "ifg" | "ifb"
                ) + BasicArgument + comma + BasicArgument;

            ExtInstruction.Rule = Named("Opcode",
                    ToTerm("jsr")
                ) + BasicArgument;

            Data.Rule = ToTerm("dat") + DataArguments;

            MarkTransient(BasicArgument);
            BasicArgument.Rule = Reg | RegLookup | RegOffLookup
                | StackOp | LiteralWord | LiteralLookup;
            //BasicArgument.ErrorRule = SyntaxError + comma | SyntaxError + NewLine;

            DataArguments.Rule = MakePlusRule(DataArguments, comma, DataValue);

            MarkTransient(DataValue);
            DataValue.Rule = stringLit | numberLit | charLit | ident;

            MarkTransient(Reg);
            Reg.Rule = GeneralReg | SpecialReg;

            GeneralReg.Rule = ToTerm("a") | "b" | "c" | "x" | "y" | "z" | "i" | "j";

            SpecialReg.Rule = ToTerm("pc") | "sp" | "o";

            RegLookup.Rule = "[" + GeneralReg + "]";

            RegOffLookup.Rule = "[" + (
                    LiteralWord + plus + GeneralReg
                    | GeneralReg + (plus | minus) + LiteralWord
                ) + "]";

            StackOp.Rule = ToTerm("push") | "pop" | "peek";

            LiteralWord.Rule = charLit | numberLit | ident;

            LiteralLookup.Rule = "[" + LiteralWord + "]";
            #endregion

            #region Language Flags
            this.LanguageFlags = Irony.Parsing.LanguageFlags.NewLineBeforeEOF;
            #endregion
        }

        #region Non-Terminal constructors
        public static NonTerminal Named(string name, BnfExpression expr)
        {
            var nt = new NonTerminal(name);
            nt.Rule = expr;
            return nt;
        }
        #endregion

        #region Terminal constructors
        public static NumberLiteral CreateDasmNumber(string name, Options options)
        {
            var lit = new NumberLiteral(name,
                (options.SignedNumbers ? NumberOptions.AllowSign : 0)
                | (options.UnderscoreInNumbers ? NumberOptions.AllowUnderscore : 0)
                | NumberOptions.IntOnly | NumberOptions.NoDotAfterInt);

            lit.DefaultIntTypes = new TypeCode[] { TypeCode.UInt16, TypeCode.Int16 };
            if (options.BinaryLiterals)
                lit.AddPrefix("0b", NumberOptions.Binary);
            lit.AddPrefix("0x", NumberOptions.Hex);

            return lit;
        }

        public static StringLiteral CreateDasmCharacter(string name, Options options)
        {
            var lit = new StringLiteral(name);

            lit.AddStartEnd("'", StringOptions.AllowsXEscapes | StringOptions.IsChar);

            return lit;
        }

        public static StringLiteral CreateDasmString(string name, Options options)
        {
            var lit = new StringLiteral(name);

            lit.AddStartEnd("\"", StringOptions.AllowsXEscapes);

            return lit;
        }

        public static IdentifierTerminal CreateDasmIdentifier(string name, Options options)
        {
            var lit = new IdentifierTerminal(name);

            if (options.LocalLabels)
            {
                lit.AllFirstChars += ".";
                lit.AllChars += ".";
            }

            if (options.ExtendedLabelNames)
            {
                lit.AllFirstChars += "$";
                lit.AllChars += "$";
            }

            return lit;
        }

        // Not used.  Yet.
        /*
        public static RegexBasedTerminal CreateDasmComplexIdentifier(string name, Options options)
        {
            var lit = new RegexBasedTerminal(name,
                @"[.$_a-zA-Z]([.$_a-zA-Z0-9]|[#]([+-][0-9]+)?)*");

            return lit;
        }
        //*/

        public static CommentTerminal CreateDasmComment(string name, Options options)
        {
            var lit = new CommentTerminal(name, ";", "\r", "\n");
            return lit;
        }
        #endregion

        /// <summary>
        /// This class aggregates various Dasm language options.
        /// </summary>
        public class Options
        {
            /// <summary>
            /// Allow negative numbers?  Then will be stored as 2's
            /// complement; i.e. -1 is stored as 0xffff.
            /// </summary>
            public bool SignedNumbers = false;
            /// <summary>
            /// Allow underscores in numbers?  This is useful for
            /// visually dividing up long literals; e.g. 32_768.
            /// </summary>
            public bool UnderscoreInNumbers = false;
            /// <summary>
            /// Allow binary number literals?  E.g. 0b101101.
            /// </summary>
            public bool BinaryLiterals = false;
            /// <summary>
            /// Allow local labels?  Prefixing a label with a period
            /// causes the assembler to automatically prepend the
            /// full name of the last non-local label.
            /// </summary>
            public bool LocalLabels = false;
            /// <summary>
            /// Allows extra characters in label names.  Currently,
            /// this boils down to '$'.
            /// </summary>
            public bool ExtendedLabelNames = false;

            /// <summary>
            /// Options that conform to standard assembler syntax.
            /// </summary>
            public static Options Standard { get; private set; }
            /// <summary>
            /// Extended syntax a.k.a. everything turned on.
            /// </summary>
            public static Options Extended { get; private set; }

            static Options()
            {
                Standard = new Options
                {
                };

                Extended = new Options
                {
                    SignedNumbers = true,
                    UnderscoreInNumbers = true,
                    BinaryLiterals = true,
                    LocalLabels = true,
                    ExtendedLabelNames = true,
                };
            }
        }
    }
}
