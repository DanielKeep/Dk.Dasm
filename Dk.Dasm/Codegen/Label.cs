using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Irony.Parsing;

namespace Dk.Dasm.Codegen
{
    /// <summary>
    /// Represents what the label is "attached" to.  This is only of use
    /// when generating the symbol list to give readers a clue as to
    /// what the label was used for.
    /// </summary>
    public enum LabelType
    {
        /// <summary>
        /// We don't know what this label is for.
        /// </summary>
        Unknown,
        /// <summary>
        /// The label points to code.
        /// </summary>
        Code,
        /// <summary>
        /// The label points to data.
        /// </summary>
        Data,
    }

    /// <summary>
    /// A Label is used to name a position in memory.  This can be either
    /// for referencing a subroutine or jump target, a variable or a
    /// block of static data.
    /// 
    /// Labels also track an index which is used to refer to them inside
    /// Code structures.
    /// 
    /// Finally, Labels can be forwarded to another Label.  This is used
    /// in cases where a label is fixed to another label which has not
    /// yet been defined.  For example:
    /// 
    /// <example>:A @ B
    /// :B dat 0</example>
    /// </summary>
    public class Label
    {
        /// <summary>
        /// Create an unfixed label.
        /// </summary>
        public Label(ushort index, string name)
        {
            this.Index = index;
            this.Name = name;
        }

        /// <summary>
        /// Create a fixed label with the given value.
        /// </summary>
        public Label(ushort index, string name, ushort value)
            : this(index, name)
        {
            this.Index = index;
            this.Fixed = true;
            this.Value = value;
        }

        /// <summary>
        /// Fix the label to the given value.
        /// </summary>
        public void Fix(ushort value, SourceSpan span)
        {
            this.Value = value;
            this.Span = span;
            this.Fixed = true;
        }

        /// <summary>
        /// Fix the label to the given label.
        /// </summary>
        public void Fix(Label fixTo, SourceSpan span)
        {
            this.ForwardsTo = fixTo;
            this.Span = span;
        }

        /// <summary>
        /// Name of the label.
        /// </summary>
        public string Name;

        /// <summary>
        /// Indicates whether or not the label's value has been determined
        /// yet.  This will be false for labels which have been used before
        /// they have been defined.
        /// </summary>
        public bool _Fixed;
        public bool Fixed
        {
            get
            {
                return (ForwardsTo != null ? ForwardsTo.Fixed : _Fixed);
            }
            set
            {
                if (ForwardsTo != null)
                    throw new InvalidOperationException();
                _Fixed = value;
            }
        }

        /// <summary>
        /// The value of this label.
        /// </summary>
        private ushort _Value;
        public ushort Value
        {
            get
            {
                return (ForwardsTo != null ? ForwardsTo.Value : _Value);
            }
            set
            {
                if (ForwardsTo != null)
                    throw new InvalidOperationException();
                _Value = value;
            }
        }

        /// <summary>
        /// Location of the label within the source.
        /// </summary>
        public SourceSpan Span;

        /// <summary>
        /// The index of this label within the list of labels.
        /// </summary>
        public ushort Index;

        /// <summary>
        /// The label which this label is fixed to.
        /// </summary>
        public Label ForwardsTo;

        /// <summary>
        /// Shorthand for determining if the label is forwarded.
        /// </summary>
        public bool IsForwarded
        {
            get
            {
                return ForwardsTo != null;
            }
        }

        /// <summary>
        /// Best-guess as to the type of the label.
        /// </summary>
        public LabelType _Type;
        public LabelType Type
        {
            get
            {
                return (ForwardsTo != null ? ForwardsTo.Type : _Type);
            }
            set
            {
                if (ForwardsTo != null)
                    throw new InvalidOperationException();
                _Type = value;
            }
        }
    }

    public static class LabelTypeExtensions
    {
        public static string ToShorthand(this LabelType type)
        {
            switch (type)
            {
                case LabelType.Unknown: return "?";
                case LabelType.Code: return "c";
                case LabelType.Data: return "d";
                default:
                    throw new InvalidOperationException();
            }
        }
    }
}
