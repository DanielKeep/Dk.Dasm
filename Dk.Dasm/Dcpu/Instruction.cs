using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Dk.Dasm.Dcpu
{
    public struct Instruction
    {
        public Instruction(ushort value)
        {
            Value = value;
        }

        public ushort Value;

        /*
        public string ToString()
        {
            // Longest possible instruction; 26 chars, round up to 28
            // Xxx [0x0000+a], [0x0000+a]
            var sb = new StringBuilder(28);

            if (IsBasic)
            {
                sb.Append(BasicOpcode.ToString());
                sb.Append(" ");
                FormatArg(sb, ArgA).Append(", ");
                FormatArg(sb, ArgB);
            }
            else // if (IsExt)
            {
                sb.Append(ExtOpcode.ToString());
                sb.Append(" ");
                FormatArg(sb, ArgB);
            }

            return sb.ToString();
        }

        private const string GeneralRegisters = "ABCXYZIJ";

        public StringBuilder FormatArg(StringBuilder sb, ushort arg)
        {
            switch (arg)
            {
                case 0: case 1: case 2: case 3:
                case 4: case 5: case 6: case 7:
                    sb.Append(GeneralRegisters[arg]);
                    break;
                    
                case 8: case 9: case 10: case 11:
                case 12: case 13: case 14: case 15:
                    sb.Append("[");
                    sb.Append(GeneralRegisters[arg - 8]);
                    sb.Append("]");
                    break;

                case 16: case 17: case 18: case 19:
                case 20: case 21: case 22: case 23:
                    sb.AppendFormat("[{0}+pc++]", GeneralRegisters[arg - 16]);
                    break;

                case 24:
                    sb.Append("pop");
                    break;

                case 25:
                    sb.Append("peek");
                    break;

                case 26:
                    sb.Append("push");
                    break;

                case 27:
                    sb.Append("sp");
                    break;

                case 28:
                    sb.Append("pc");
                    break;

                case 29:
                    sb.Append("o");
                    break;

                case 30:
                    sb.Append("[pc++]");
                    break;

                case 31:
                    sb.Append("pc++");
                    break;

                default:
                    sb.AppendFormat("0x{0:X4}", arg - 32);
                    break;
            }

            return sb;
        }
        //*/

        public BasicOpcode BasicOpcode
        {
            get
            {
                return (BasicOpcode)(this.Value & 0x1F);
            }
            set
            {
                var old = this.Value & ~0x001F;
                this.Value = (ushort)(old | ((ushort)value));
            }
        }

        public ushort ArgA
        {
            get
            {
                return (ushort)((this.Value >> 10) & 0x3F);
            }
            set
            {
                var old = this.Value & ~0xFC00;
                this.Value = (ushort)(old | (((ushort)value & 0x3F) << 10));
            }
        }

        public ushort ArgB
        {
            get
            {
                return (ushort)((this.Value >> 5) & 0x1F);
            }
            set
            {
                if (value >= 0x20)
                    throw new ArgumentOutOfRangeException("value", "instruction argument B must be <= 0x1f");
                var old = this.Value & ~0x03E0;
                this.Value = (ushort)(old | (((ushort)value & 0x1F) << 5));
            }
        }

        public ExtOpcode ExtOpcode
        {
            get
            {
                return (ExtOpcode)ArgB;
            }
            set
            {
                this.ArgB = (ushort)value;
            }
        }

        public bool IsBasic
        {
            get
            {
                return BasicOpcode != Dcpu.BasicOpcode.Ext;
            }
        }

        public bool IsExt
        {
            get
            {
                return BasicOpcode == Dcpu.BasicOpcode.Ext;
            }
        }
    }

    public enum BasicOpcode
    {
        Ext, Set, Add, Sub, Mul, Mli, Div, Dvi,
        Mod, And, Bor, Xor, Shr, Asr, Shl, Mvi,
        Ifb, Ifc, Ife, Ifn, Ifg, Ifa, Ifl, Ifu,
        B18, B19, Adx, Sux, B1C, B1D, B1E, B1F,

        Max = B1F,
    }

    public enum ExtOpcode
    {
        X00, Jsr, X02, X03, X04, X05, X06, X07,
        Int, Iag, Ias, X0B, X0C, X0D, X0E, X0F,
        Hwn, Hwq, Hwi, X13, X14, X15, X16, X17,
        X18, X19, X1A, X1B, X1C, X1D, X1E, X1F,

        Max = X1F,
    }

    public static class OpcodeExtensions
    {
        static OpcodeExtensions()
        {
            BasicOpcodeMap = new Dictionary<string, BasicOpcode>();
            for (var i = 1; i < (int)BasicOpcode.Max; ++i)
                BasicOpcodeMap[((BasicOpcode)i).ToString().ToLower()] = (BasicOpcode)i;

            ExtOpcodeMap = new Dictionary<string, ExtOpcode>();
            for (var i = 0; i < (int)ExtOpcode.Max; ++i)
                ExtOpcodeMap[((ExtOpcode)i).ToString().ToLower()] = (ExtOpcode)i;
        }

        public static Dictionary<string, BasicOpcode> BasicOpcodeMap;
        public static Dictionary<string, ExtOpcode> ExtOpcodeMap;

        public static BasicOpcode ToBasicOpcode(this string name)
        {
            return BasicOpcodeMap[name.ToLower()];
        }

        public static ExtOpcode ToExtOpcode(this string name)
        {
            return ExtOpcodeMap[name.ToLower()];
        }
    }
}
