using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Irony.Parsing;

namespace Dk.Dasm
{
    public static class ParseTreeNodeExtension
    {
        /// <summary>
        /// Helper method for dumping parse nodes.
        /// </summary>
        public static void DumpNode(this ParseTreeNode node, System.IO.TextWriter w, string ind)
        {
            w.WriteLine("{0}{1}{2}", ind, node.Term.Name,
                node.Token != null
                    ? " : " + node.Token.Value.GetType().ToString() + " = " + node.Token.Value.ToString()
                    : "");
            ind += "  ";
            foreach (var child in node.ChildNodes)
                DumpNode(child, w, ind);
        }
    }
}
