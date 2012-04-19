using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Irony.Parsing;

namespace Dk.Dasm
{
    /// <summary>
    /// Standard Dasm Grammar class.
    /// 
    /// This exists primarily so that the Irony Grammar Explorer
    /// can see it, letting me test it easily.
    /// </summary>
    [Language("DASM", "0.1", "DCPU-16 assembly language")]
    public class DasmStdGrammar : DasmGrammar
    {
        public DasmStdGrammar()
            : base(DasmGrammar.Options.Standard)
        {
        }
    }
}
