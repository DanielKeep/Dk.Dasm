using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Dk.Dasm.Codegen
{
    /// <summary>
    /// Used for general problems encountered during codegen.
    /// </summary>
    [Serializable]
    public class CodegenException : Exception
    {
        public CodegenException() { }
        public CodegenException(string message) : base(message) { }
        public CodegenException(string message, Exception inner) : base(message, inner) { }
        protected CodegenException(
          System.Runtime.Serialization.SerializationInfo info,
          System.Runtime.Serialization.StreamingContext context)
            : base(info, context) { }

        public CodegenException(string format, params object[] args)
            : base(String.Format(format, args)) { }
    }

    /// <summary>
    /// Exception thrown when we hit a grammar construct we didn't expect.
    /// </summary>
    [Serializable]
    public class UnexpectedGrammarException : Exception
    {
        public UnexpectedGrammarException() : this("Unexpected grammar element") { }
        public UnexpectedGrammarException(string message) : base(message) { }
        public UnexpectedGrammarException(string message, Exception inner) : base(message, inner) { }
        protected UnexpectedGrammarException(
          System.Runtime.Serialization.SerializationInfo info,
          System.Runtime.Serialization.StreamingContext context)
            : base(info, context) { }
    }
}
