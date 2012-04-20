﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Irony.Parsing;

using Dk.Dasm;
using Dk.Dasm.Dcpu;

namespace Dk.Dasm.Codegen
{
    /// <summary>
    /// Dasm Code generator.
    /// </summary>
    public class DasmCodegen
    {
        /// <summary>
        /// Composable flags for a code word.
        /// </summary>
        [Flags]
        public enum CodeFlags : byte
        {
            None = 0,
            Negate = 1,
            IsLiteral = 2,
        }

        /// <summary>
        /// Specifies the type of a code word.
        /// </summary>
        public enum CodeType : byte
        {
            Instruction,
            Literal,
            Label,
        }

        /// <summary>
        /// Code is a half-way house to the words in the final binary.
        /// Basically, the purpose of this structure is to allow us
        /// to store instructions, literal values and (importantly)
        /// uses of labels.
        /// 
        /// The reason we distinguish between instructions and literal
        /// values is to (someday) let us do optimisations like
        /// rewriting jumps to nearby addresses to short relative
        /// jumps.
        /// 
        /// The reason we need to special-case label uses is that we
        /// can get labels for which we don't yet know the address.
        /// This lets us defer working out the actual value to emit.
        /// </summary>
        public struct Code
        {
            public CodeType Type;
            public CodeFlags Flags;
            public ushort Value;

            public Code(Instruction instr)
            {
                this.Type = CodeType.Instruction;
                this.Value = instr.Value;
                this.Flags = CodeFlags.None;
            }

            public Code(byte value)
            {
                this.Type = CodeType.Literal;
                this.Value = value;
                this.Flags = CodeFlags.None;
            }

            public Code(ushort value)
            {
                this.Type = CodeType.Literal;
                this.Value = value;
                this.Flags = CodeFlags.None;
            }

            public Code(Label value)
            {
                this.Type = CodeType.Label;
                this.Value = value.Index;
                this.Flags = CodeFlags.None;
            }

            public void Negate()
            {
                this.Flags ^= CodeFlags.Negate;
            }

            public Dcpu.Instruction Instruction
            {
                get
                {
                    return new Dcpu.Instruction(Value);
                }
                set
                {
                    this.Value = value.Value;
                }
            }
        }

        /// <summary>
        /// This is the global label prepended to local labels
        /// *before* the first user-defined global label.
        /// </summary>
        public const string InitialLabel = "__GLOBAL__";

        public delegate Label LabelLookup(string name);
        public delegate void CodeWriter(Code code);

        /// <summary>
        /// Used to bundle together information passed down through the
        /// code generator methods.
        /// </summary>
        public struct CgContext
        {
            /// <summary>
            /// Look up the label object corresponding to the given name.
            /// </summary>
            public LabelLookup Lookup;
            /// <summary>
            /// Emit a code word to the output.
            /// </summary>
            public CodeWriter Write;
        }

        /// <summary>
        /// Aggregates the outputs of the code generator.
        /// </summary>
        public struct CgResult
        {
            /// <summary>
            /// The actual binary image.
            /// </summary>
            public ushort[] Image;
            /// <summary>
            /// A list of labels, sorted by increasing value.
            /// </summary>
            public List<Label> Labels;
        }

        /// <summary>
        /// The main entry point into the code generator, this assembles a
        /// complete, parsed source file.
        /// </summary>
        public CgResult CgProgram(ParseTreeNode node)
        {
            /*
             * If it weren't for labels, this would be a piece of cake.  All
             * we'd have to do is produce cpu code for each instruction and
             * join them all together.
             * 
             * But we *do* have to deal with labels, so there.  We could handle
             * this by keeping track of a list of fixups; locations that contain
             * a forward reference.  Then, once we know what that label resolves
             * to, we go back and do the fixups.
             * 
             * However, I'd like to be able to do optimisation on the generated
             * code; specifically, replacing jumps to nearby labels with
             * relative jumps using add/sub pc, X.  Doing this will cause the
             * address of all subsequent code (and labels) to change.
             * 
             * In that case, it's probably simpler to just leave all labels
             * unresolved until *after* optimisation.  But what happens when
             * we do shorten a given instruction?
             * 
             * When we shorten a given instruction, all subsequent fixups need
             * to have their target address adjusted.
             * 
             * An attractive alternative is to not keep a list of fixups;
             * instead, generated code is widened such that we can store
             * metadata for each word.  This would include storing label
             * references directly.  The advantage of this is that moving
             * code around is cheap.
             * 
             * So that's what we'll do.
             * 
             * For now, we'll just use a list to store the generated code.
             * Ideally, we'd have a linked list of pages since we won't be
             * doing a lot of cutting.
             */
            var code = new List<Code>();
            /*
             * We also need something to keep track of the defined labels.
             * Since we need to be able to pack this into code entries,
             * we'll stick them in a flat array.
             */
            var labels = new List<Label>();
            /*
             * A way of mapping a label name to a label object would also
             * be handy.
             */
            var labelMap = new Dictionary<string, Label>();
            /*
             * In order to implement local labels, we need to track the
             * last non-local label we saw.
             */
            var lastGlobalLabel = InitialLabel;
            /*
             * We'll also need a function we can pass into the codegen
             * functions to turn a label name into a label object.
             */
            LabelLookup lookup = delegate(string name)
            {
                if (name.StartsWith("."))
                    name = lastGlobalLabel + name;

                if (labelMap.ContainsKey(name))
                    return labelMap[name];

                else
                {
                    var index = (ushort)labels.Count;
                    var label = new Label(index, name);
                    labels.Add(label);
                    labelMap[name] = label;
                    return label;
                }
            };
            /*
             * And while we're on the subject: a function to write to the
             * code list.
             */
            CodeWriter write = delegate(Code c)
            {
                code.Add(c);
            };
            /*
             * Whack 'em in a context.
             */
            var ctx = new CgContext { Lookup = lookup, Write = write };
            /*
             * Ok, let's process those lines.
             */
            foreach (var line in node.ChildNodes)
            {
                var labelNode = line.ChildNodes[0].ChildNodes.FirstOrDefault();
                var instrNode = line.ChildNodes[1].ChildNodes.FirstOrDefault();
                var labelName = labelNode != null ? labelNode.ChildNodes[0].Token.Text : null;

                if (labelName != null)
                {
                    if (!labelName.StartsWith("."))
                        lastGlobalLabel = labelName;

                    var label = lookup(labelName);

                    if (label.Fixed)
                        throw new CodegenException("Label '{0}' already defined at {1}.",
                            labelName, label.Span.Location);

                    if (instrNode != null && instrNode.Term.Name == "AddressFix")
                    {
                        var fix = EvalLiteralWord(instrNode.ChildNodes[0], ref ctx);
                        switch (fix.Type)
                        {
                            case CodeType.Literal:
                                label.Fix(fix.Value, labelNode.Span);
                                break;

                            case CodeType.Label:
                                label.Fix(labels[fix.Value], labelNode.Span);
                                break;

                            default:
                                throw new UnexpectedGrammarException();
                        }

                        instrNode = null;
                    }
                    else
                    {
                        var addr = (ushort)code.Count;
                        label.Fix(addr, labelNode.Span);
                    }
                }

                if (instrNode != null)
                    CgOptInstruction(instrNode, ref ctx);
            }
            /*
             * Let's add a few extra labels for fun.  And usefulness.
             */
            ctx.Lookup("__CODE_START").Fix(0, node.Span);
            ctx.Lookup("__CODE_END").Fix((ushort)code.Count, node.Span);
            /*
             * We now have code generated for the whole program.  What we
             * we need to do now is go back and fill in the labels.
             * 
             * Before that, let's just make sure that all labels were
             * actually defined somewhere...
             */
            foreach (var label in labels)
                if (!label.Fixed)
                    throw new CodegenException("Label '{0}' never defined.", label.Name);
            /*
             * Right.  We now have enough information to generate the final, complete
             * binary.
             * 
             * First of all, we'll fill in guesses for what the labels are attached to.
             */
            foreach (var label in labels)
            {
                if (label.IsForwarded)
                    continue;

                var addr = label.Value;
                if (addr >= code.Count)
                    continue;

                switch (code[addr].Type)
                {
                    case CodeType.Instruction:
                        label.Type = LabelType.Code;
                        break;
                    case CodeType.Label:
                        if( (code[addr].Flags & CodeFlags.IsLiteral) == 0 )
                            goto default;
                        label.Type = LabelType.Data;
                        break;
                    case CodeType.Literal:
                        label.Type = LabelType.Data;
                        break;
                    default:
                        label.Type = LabelType.Unknown;
                        break;
                }
            }
            /*
             * Now we can produce the output image.
             */
            var image = new ushort[code.Count];
            {
                var i = 0;
                foreach (var c in code)
                {
                    switch (c.Type)
                    {
                        case CodeType.Label:
                            image[i] = labels[c.Value].Value;
                            break;

                        default:
                            image[i] = c.Value;
                            break;
                    }

                    ++i;
                }
            }
            /*
             * Done.  Collect the results.
             */
            labels.Sort((a, b) => a.Value - b.Value);
            return new CgResult
            {
                Image = image,
                Labels = labels,
            };
        }

        /*
         * The rest of the module generate specific grammar constructs.
         * The methods are named after the corresponding construct in the
         * grammar class.
         */

        public void CgOptInstruction(ParseTreeNode node, ref CgContext ctx)
        {
            // OptInstruction is transient
            var childNode = node;

            switch (childNode.Term.Name)
            {
                case "BasicInstruction":
                    CgBasicInstruction(childNode, ref ctx);
                    return;

                case "ExtInstruction":
                    CgExtInstruction(childNode, ref ctx);
                    return;

                case "Data":
                    CgData(childNode, ref ctx);
                    return;

                default:
                    throw new UnexpectedGrammarException();
            }
        }

        public void CgBasicInstruction(ParseTreeNode node, ref CgContext ctx)
        {
            var op = node.ChildNodes[0].ChildNodes[0];
            var argA = node.ChildNodes[1];
            var argB = node.ChildNodes[2];

            Instruction instr = new Instruction();
            Code? tailA, tailB;

            instr.BasicOpcode = op.Term.Name.ToBasicOpcode();
            instr.ArgA = CgBasicArgument(argA, ref ctx, out tailA);
            instr.ArgB = CgBasicArgument(argB, ref ctx, out tailB);

            ctx.Write(new Code(instr));
            if (tailA.HasValue) ctx.Write(tailA.Value);
            if (tailB.HasValue) ctx.Write(tailB.Value);
        }

        public void CgExtInstruction(ParseTreeNode node, ref CgContext ctx)
        {
            var op = node.ChildNodes[0];
            var arg = node.ChildNodes[1];

            Instruction instr = new Instruction();
            Code? tail;

            instr.BasicOpcode = BasicOpcode.Ext;
            instr.ExtOpcode = op.ChildNodes[0].Token.Text.ToExtOpcode();
            instr.ArgB = CgBasicArgument(arg, ref ctx, out tail);

            ctx.Write(new Code(instr));
            if (tail.HasValue) ctx.Write(tail.Value);
        }

        public void CgData(ParseTreeNode node, ref CgContext ctx)
        {
            var args = node.ChildNodes[1];

            foreach (var arg in args.ChildNodes)
                CgDataValue(arg, ref ctx);
        }

        public void CgDataValue(ParseTreeNode node, ref CgContext ctx)
        {
            // DataValue is transient
            // DataValue = string | number | character | identifier
            switch (node.Term.Name)
            {
                case "character":
                    {
                        var ch = EvalCharacter(node, ref ctx);
                        ctx.Write(new Code(ch));
                    }
                    break;

                case "identifier":
                    {
                        var l = EvalIdentifier(node, ref ctx);
                        var c = new Code(l);
                        c.Flags |= CodeFlags.IsLiteral;
                        ctx.Write(c);
                    }
                    break;

                case "number":
                    {
                        var v = EvalNumber(node, ref ctx);
                        ctx.Write(new Code(v));
                    }
                    break;

                case "string":
                    {
                        var s = EvalString(node, ref ctx);
                        foreach (var ch in s)
                        {
                            ctx.Write(new Code(ch));
                        }
                    }
                    break;

                default:
                    throw new UnexpectedGrammarException();
            }
        }

        public ushort CgBasicArgument(ParseTreeNode node, ref CgContext ctx, out Code? tail)
        {
            switch (node.Term.Name)
            {
                case "GeneralRegister": return CgGeneralReg(node, ref ctx, out tail);
                case "SpecialRegister": return CgSpecialReg(node, ref ctx, out tail);
                case "RegisterLookup": return CgRegLookup(node, ref ctx, out tail);
                case "RegisterOffsetLookup": return CgRegOffLookup(node, ref ctx, out tail);
                case "StackOp": return CgStackOp(node, ref ctx, out tail);
                case "LiteralWord": return CgLiteralWord(node, ref ctx, out tail);
                case "LiteralLookup": return CgLiteralLookup(node, ref ctx, out tail);

                default:
                    throw new UnexpectedGrammarException();
            }
        }

        public ushort CgGeneralReg(ParseTreeNode node, ref CgContext ctx, out Code? tail)
        {
            tail = null;
            return GeneralRegNumber(node.ChildNodes[0].Token.Text);
        }

        public ushort GeneralRegNumber(string name)
        {
            switch (name.ToLower())
            {
                case "a": return 0;
                case "b": return 1;
                case "c": return 2;
                case "x": return 3;
                case "y": return 4;
                case "z": return 5;
                case "i": return 6;
                case "j": return 7;
                default:
                    throw new UnexpectedGrammarException();
            }
        }

        public ushort CgSpecialReg(ParseTreeNode node, ref CgContext ctx, out Code? tail)
        {
            tail = null;

            switch (node.ChildNodes[0].Token.Text.ToLower())
            {
                case "sp": return 27;
                case "pc": return 28;
                case "o": return 29;
                default:
                    throw new UnexpectedGrammarException();
            }
        }

        public ushort CgRegLookup(ParseTreeNode node, ref CgContext ctx, out Code? tail)
        {
            tail = null;
            return (ushort)(8 + GeneralRegNumber(node.ChildNodes[0].ChildNodes[0].Token.Text));
        }

        public ushort CgRegOffLookup(ParseTreeNode node, ref CgContext ctx, out Code? tail)
        {
            var inner = node.ChildNodes[0];
            var lhs = inner.ChildNodes[0];
            var op = inner.ChildNodes[1];
            var rhs = inner.ChildNodes[2];

            ushort regNum = 0xffff;
            Code offsetCode;

            if (lhs.Term.Name == "GeneralRegister")
            {
                regNum = GeneralRegNumber(lhs.ChildNodes[0].Token.Text);
                offsetCode = EvalLiteralWord(rhs, ref ctx);
                if (op.Token.Text == "-")
                    offsetCode.Negate();
            }
            else
            {
                regNum = GeneralRegNumber(rhs.ChildNodes[0].Token.Text);
                offsetCode = EvalLiteralWord(lhs, ref ctx);
            }

            tail = offsetCode;
            return (ushort)(16 + regNum);
        }

        public ushort CgStackOp(ParseTreeNode node, ref CgContext ctx, out Code? tail)
        {
            tail = null;

            switch (node.ChildNodes[0].Token.Text.ToLower())
            {
                case "pop": return 24;
                case "peek": return 25;
                case "push": return 26;
                default:
                    throw new UnexpectedGrammarException();
            }

        }

        public ushort CgLiteralWord(ParseTreeNode node, ref CgContext ctx, out Code? tail)
        {
            tail = null;

            var valueCode = EvalLiteralWord(node, ref ctx);

            if (valueCode.Type == CodeType.Literal)
            {
                if (valueCode.Value < 32)
                    return (ushort)(32 + valueCode.Value);
            }

            tail = valueCode;
            return 31;
        }

        public ushort CgLiteralLookup(ParseTreeNode node, ref CgContext ctx, out Code? tail)
        {
            tail = EvalLiteralWord(node.ChildNodes[0], ref ctx);
            return 30;
        }

        public Code EvalLiteralWord(ParseTreeNode node, ref CgContext ctx)
        {
            var child = node.ChildNodes[0];

            switch (child.Term.Name)
            {
                case "character":
                    return new Code(EvalCharacter(child, ref ctx));

                case "identifier":
                    return new Code(EvalIdentifier(child, ref ctx));

                case "number":
                    return new Code(EvalNumber(child, ref ctx));

                default:
                    throw new UnexpectedGrammarException();
            }
        }

        public ushort EvalCharacter(ParseTreeNode node, ref CgContext ctx)
        {
            return (ushort)(Char)node.Token.Value;
        }

        public Label EvalIdentifier(ParseTreeNode node, ref CgContext ctx)
        {
            return ctx.Lookup((String)node.Token.Value);
        }

        public ushort EvalNumber(ParseTreeNode node, ref CgContext ctx)
        {
            var no = node.Token.Value;
            var nt = no.GetType();

            if (nt == typeof(Int16))
                return (ushort)(Int16)no;
            else
                return (UInt16)no;
        }

        public string EvalString(ParseTreeNode node, ref CgContext ctx)
        {
            return (String)node.Token.Value;
        }
    }
}