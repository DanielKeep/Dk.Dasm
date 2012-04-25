using System;
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
            Difference,
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

            public Code(CodeType type, ushort value)
            {
                this.Type = type;
                this.Value = value;
                this.Flags = CodeFlags.IsLiteral;
            }

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
        /// Represents a difference literal.
        /// </summary>
        public struct Difference
        {
            public Code Base, Target;

            public Difference(Code @base, Code target)
            {
                this.Base = @base;
                this.Target = target;
            }

            public Difference(ushort @base, Code target)
                : this(new Code(@base), target)
            {
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
            /// <summary>
            /// The current length of the code array.
            /// </summary>
            public Func<ushort> GetCurrentAddress;
            /// <summary>
            /// Callback to emit a difference to the code array.  We need
            /// a separate callback since a difference won't actually
            /// fit in a Code.
            /// </summary>
            public Func<Difference, Code> EncodeDifference;
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
             * This array stores difference literals.  We need to do this
             * since difference literals can depend on labels but won't
             * fit in a code.
             */
            var diffLits = new List<Difference>();
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
            Func<Difference, Code> diffToCode = delegate(Difference diff)
            {
                var index = (ushort)diffLits.Count;
                diffLits.Add(diff);
                return new Code(CodeType.Difference, index);
            };
            /*
             * Whack 'em in a context.
             */
            var ctx = new CgContext
            {
                Lookup = lookup,
                Write = write,
                GetCurrentAddress = () => (ushort)code.Count,
                EncodeDifference = diffToCode,
            };
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

                            case CodeType.Difference:
                                throw new CodegenException("Labels cannot be fixed to difference literals");

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
            Func<Code, ushort> evalCode = null;
            evalCode = delegate(Code c)
            {
                switch (c.Type)
                {
                    case CodeType.Label:
                        return labels[c.Value].Value;

                    case CodeType.Difference:
                        {
                            var diff = diffLits[c.Value];
                            return (ushort)(evalCode(diff.Target) - evalCode(diff.Base));
                        }

                    default:
                        return c.Value;
                }
            };

            var image = new ushort[code.Count];
            {
                var i = 0;
                foreach (var c in code)
                {
                    image[i] = evalCode(c);
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
            instr.ArgA = CgArgument(argA, ref ctx, out tailA, shortForm: false);
            instr.ArgB = CgArgument(argB, ref ctx, out tailB, shortForm: true);

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
            instr.ArgB = CgArgument(arg, ref ctx, out tail, shortForm: false);

            ctx.Write(new Code(instr));
            if (tail.HasValue) ctx.Write(tail.Value);
        }

        public void CgData(ParseTreeNode node, ref CgContext ctx)
        {
            var args = node.ChildNodes[1];

            var i = 0;
            foreach (var arg in args.ChildNodes)
            {
                if (arg.Term.Name == "DataLength")
                    ctx.Write(new Code(CountDataWords(args, i + 1, ref ctx)));

                else
                    CgDataValue(arg, ref ctx);

                ++i;
            }
        }

        public ushort CountDataWords(ParseTreeNode datNode, int startAt, ref CgContext ctx)
        {
            var acc = (ushort)0;

            for (var i = startAt; i < datNode.ChildNodes.Count; ++i)
            {
                var val = datNode.ChildNodes[i];

                switch (val.Term.Name)
                {
                    case "string":
                        acc += (ushort)EvalString(val, ref ctx).Length;
                        break;

                    default:
                        acc++;
                        break;
                }
            }

            return acc;
        }

        public void CgDataValue(ParseTreeNode node, ref CgContext ctx)
        {
            // DataValue is transient
            // DataValue = [tilde |] string | number | character | identifier

            // Note that ~ is handled by the caller since, by this point, it's
            // too late to do anything.
            if (node.Term.Name == "string")
            {
                var s = EvalString(node, ref ctx);
                foreach (var ch in s)
                    ctx.Write(new Code(ch));
            }
            else
            {
                ctx.Write(EvalLiteralWord(node, ref ctx));
            }
        }

        public ushort CgArgument(ParseTreeNode node, ref CgContext ctx, out Code? tail, bool shortForm)
        {
            switch (node.Term.Name)
            {
                case "GeneralRegister": return CgGeneralReg(node, ref ctx, out tail);
                case "SpecialRegister": return CgSpecialReg(node, ref ctx, out tail);
                case "RegisterLookup": return CgRegLookup(node, ref ctx, out tail);
                case "RegisterOffsetLookup": return CgRegOffLookup(node, ref ctx, out tail);
                case "StackOp": return CgStackOp(node, ref ctx, out tail);
                case "LiteralWord": return CgLiteralWord(node, shortForm, ref ctx, out tail);
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
                case "sp": return 0x1b;
                case "pc": return 0x1c;
                case "ex": return 0x1d;
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
                // Note: op is actually wrapped in an unnamed production.
                if (op.ChildNodes[0].Token.Text == "-")
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

            //switch (node.ChildNodes[0].Token.Text.ToLower())
            switch (node.ChildNodes[0].Term.Name)
            {
                case "StackPop": return 0x18; // assume in arg b
                case "StackPush": return 0x18; // assume in arg a
                case "StackPeek": return 0x19;
                case "StackPick":
                    // Use length-1 since we could have either [sp,+,N] or [pick,N].
                    tail = EvalLiteralWord(node.ChildNodes[node.ChildNodes.Count - 1], ref ctx);
                    return 0x1a;
                default:
                    throw new UnexpectedGrammarException();
            }

        }

        public ushort CgLiteralWord(ParseTreeNode node, bool shortForm, ref CgContext ctx, out Code? tail)
        {
            tail = null;

            var valueCode = EvalLiteralWord(node, ref ctx);

            if (valueCode.Type == CodeType.Literal && !shortForm)
            {
                if (valueCode.Value <= 30 || valueCode.Value == 0xffff)
                    return (ushort)(32 + (ushort)(valueCode.Value-1));
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
            var head = node.ChildNodes[0];
            var tail = node.ChildNodes.Count > 1 ? node.ChildNodes[1] : null;
            
            var code = EvalLiteralWordAtom(head, ref ctx);

            if (tail != null)
            {
                var tailC = EvalLiteralWordAtom(tail, ref ctx);
                var diff = new Difference(code, tailC);
                code = ctx.EncodeDifference(diff);
            }

            return code;
        }

        public Code EvalLiteralWordAtom(ParseTreeNode node, ref CgContext ctx)
        {
            switch (node.Term.Name)
            {
                case "identifier":
                    return new Code(EvalIdentifier(node, ref ctx));

                case "character":
                    return new Code(EvalCharacter(node, ref ctx));

                case "number":
                    return new Code(EvalNumber(node, ref ctx));

                case "DifferenceLiteral":
                    {
                        var @base = ctx.GetCurrentAddress();
                        var target = EvalLiteralWordAtom(node.ChildNodes[0].ChildNodes[0], ref ctx);
                        return ctx.EncodeDifference(new Difference(@base, target));
                    }

                case "DifferenceTo":
                    return EvalLiteralWordAtom(node.ChildNodes[0], ref ctx);

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
