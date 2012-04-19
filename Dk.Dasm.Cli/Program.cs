using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Irony.Parsing;
using NDesk.Options;

using Dk.Dasm;

namespace Dk.Dasm.Cli
{
    /// <summary>
    /// Entry point for dkdasm.
    /// </summary>
    class Program
    {
        static void Main(string[] args)
        {
            /*
             * Set up options for the assembler.
             */
            var opt = new AssemblerOptions()
            {
                LanguageOptions = DasmGrammar.Options.Extended,
            };
            var showHelp = false;

            var os = new OptionSet()
            {
                {"t|target=", "target {PATH} for output",
                    a => opt.TargetPath = a},
                {"f|format=", "target {FORMAT}; one of: " + AssemblerOptions.FormatNames,
                    a => opt.TargetFormatFrom(a)},
                {"std", "stick to standard language",
                    a => opt.LanguageOptions = DasmGrammar.Options.Standard},
                {"s|symbols", "output symbol list to TARGET.lst",
                    a => opt.DumpSymbols = (a != null)},
                {"help", "show this message and exit",
                    a => showHelp = a != null},
            };

            var files = new List<string>();
            try
            {
                files = os.Parse(args);
            }
            catch (OptionException e)
            {
                Console.Error.WriteLine(e.Message);
            }

            if (files.Count == 0 || showHelp)
            {
                ShowHelp(os);
                return;
            }

            if (opt.TargetPath == null)
            {
                opt.TargetPath = System.IO.Path.ChangeExtension(files[0], "dcpu");
            }

            opt.SourceFilePaths.AddRange(files);

            /*
             * Do the assemble thing.
             */
            var asm = new Assembler();
            try
            {
                asm.Assemble(opt);
            }
            catch (Codegen.CodegenException e)
            {
                Console.Error.WriteLine(e.Message);
            }
        }

        static void ShowHelp(OptionSet os)
        {
            Console.WriteLine("Usage: dkdasm [OPTIONS] FILE");
            os.WriteOptionDescriptions(Console.Out);
        }
    }
}
