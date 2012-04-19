using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Irony.Parsing;

using Dk.Dasm.Codegen;

namespace Dk.Dasm
{
    /// <summary>
    /// This class provides a high-level interface to the assembler.
    /// </summary>
    public class Assembler
    {
        /// <summary>
        /// Performs assembly based on the given options object.
        /// </summary>
        public void Assemble(AssemblerOptions options)
        {
            if (options.SourceFilePaths.Count == 0)
                throw new ArgumentOutOfRangeException("No source files specified.");
            if (options.SourceFilePaths.Count > 1)
                throw new ArgumentOutOfRangeException("Can only assemble one source file right now.");

            /*
             * Parse the source file.
             */

            var sourceFilePath = options.SourceFilePaths[0];
            ParseTreeNode rootNode = null;
            {
                var src = ReadFile(sourceFilePath);

                var g = new DasmGrammar(options.LanguageOptions);
                var ld = new LanguageData(g);
                var p = new Parser(ld);
                var pt = p.Parse(src, sourceFilePath);

                // TODO: Move these out somewhere.
                if (pt.ParserMessages.Count > 0)
                {
                    foreach (var msg in pt.ParserMessages)
                        Console.Error.WriteLine("{0}{1}: {2}",
                            sourceFilePath, msg.Location, msg.Message);
                    throw new CodegenException("Compilation failed.");
                }

                rootNode = pt.Root;
            }

            /*
             * Do codegen.
             */

            DasmCodegen.CgResult cgr;
            {
                var cg = new DasmCodegen();
                cgr = cg.CgProgram(rootNode);
            }

            /*
             * Write the output file.
             */

            using (var target = new FileStream(options.TargetPath, FileMode.Create))
            {
                switch (options.TargetFormat)
                {
                    case ImageFormat.Binary:
                        foreach (var word in cgr.Image)
                        {
                            target.WriteByte((byte)(word & 0xFF));
                            target.WriteByte((byte)(word >> 8));
                        }
                        break;

                    case ImageFormat.Hexadecimal:
                        {
                            const int HexWidth = 8;
                            using (var tw = new StreamWriter(target))
                            {
                                for (var i = 0; i < cgr.Image.Length; ++i)
                                {
                                    tw.Write(string.Format("{0:x4}", cgr.Image[i]));
                                    tw.Write(i % HexWidth == (HexWidth - 1) ? tw.NewLine : " ");
                                }
                                tw.WriteLine();
                            }
                        }
                        break;

                    case ImageFormat.HexWithAddress:
                        {
                            const int HexWidth = 8;
                            using (var tw = new StreamWriter(target))
                            {
                                for (var i = 0; i < cgr.Image.Length; ++i)
                                {
                                    if (i % HexWidth == 0)
                                        tw.Write(string.Format("{0:x4} ", i));

                                    tw.Write(string.Format("{0:x4}", cgr.Image[i]));
                                    tw.Write(i % HexWidth == (HexWidth - 1) ? tw.NewLine : " ");
                                }
                                tw.WriteLine();
                            }
                        }
                        break;

                    case ImageFormat.AssemblyHex:
                        {
                            const int HexWidth = 8;
                            using (var tw = new StreamWriter(target))
                            {
                                for (var i = 0; i < cgr.Image.Length; ++i)
                                {
                                    if (i % HexWidth == 0)
                                        tw.Write("dat ");

                                    tw.Write(string.Format("0x{0:x4}", cgr.Image[i]));
                                    tw.Write(
                                        (i % HexWidth == (HexWidth - 1))
                                            || (i + 1 == cgr.Image.Length)
                                        ? tw.NewLine
                                        : ", ");
                                }
                                tw.WriteLine();
                            }
                        }
                        break;

                    default:
                        throw new InvalidOperationException();
                }
            }

            /*
             * Generate symbol map.
             */

            if (options.DumpSymbols)
            {
                using (var f = new FileStream(
                    Path.ChangeExtension(options.TargetPath, "sym"),
                    FileMode.Create))
                {
                    {
                        using (var tw = new StreamWriter(f))
                        {
                            foreach (var label in cgr.Labels)
                                tw.WriteLine("{0:X4} {1} {2}",
                                    label.Value,
                                    label.Type.ToShorthand(),
                                    label.Name);
                        }
                    }
                }
            }
        }

        private string ReadFile(string path)
        {
            return File.ReadAllText(path);
        }
    }

    /// <summary>
    /// Format for output image.
    /// </summary>
    public enum ImageFormat
    {
        /// <summary>
        /// Raw little-endian binary.
        /// </summary>
        Binary,
        /// <summary>
        /// Whitespace-delimited hexadecimal words.
        /// </summary>
        Hexadecimal,
        /// <summary>
        /// The same as Hexadecimal, except with each line starting with
        /// the address that line starts at in hexadecimal.
        /// </summary>
        HexWithAddress,
        /// <summary>
        /// Effectively Hexadecimal format written to be consumable by
        /// a Dasm assembler.  Useful for emulators that don't accept
        /// pre-compiled images.
        /// </summary>
        AssemblyHex,
    }

    /// <summary>
    /// Options for the assembler.
    /// </summary>
    public class AssemblerOptions
    {
        public AssemblerOptions()
        {
            SourceFilePaths = new List<string>();
            LanguageOptions = DasmGrammar.Options.Extended;
        }

        public string TargetPath;
        public ImageFormat TargetFormat;
        public List<string> SourceFilePaths;
        public DasmGrammar.Options LanguageOptions;
        public bool DumpSymbols;

        public const string FormatNames = "b, binary, h, hex, H, hexwithaddr, A, assemblyhex";

        public void TargetFormatFrom(string value)
        {
            switch (value)
            {
                case "b": goto case "binary";
                case "binary":
                    TargetFormat = ImageFormat.Binary;
                    break;

                case "h": goto case "hex";
                case "hex":
                    TargetFormat = ImageFormat.Hexadecimal;
                    break;

                case "H": goto case "hexwithaddr";
                case "hexwithaddr":
                    TargetFormat = ImageFormat.HexWithAddress;
                    break;

                case "A": goto case "assemblyhex";
                case "assemblyhex":
                    TargetFormat = ImageFormat.AssemblyHex;
                    break;

                default:
                    throw new ArgumentException("Unknown target format '" + value + "'");
            }
        }
    }
}
