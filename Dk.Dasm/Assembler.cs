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
        public Assembler()
        {
            OnMessage = DefaultMessageHandler;
            OnOpenFile = DefaultOpenFileHandler;
        }

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

                // Emit error messages.
                if (pt.ParserMessages.Count > 0)
                {
                    foreach (var msg in pt.ParserMessages)
                        Error(sourceFilePath, msg.Location, msg.Message);
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

            using (var target = OpenFile(options.TargetPath, FileMode.Create))
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
                using (var f = OpenFile(
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

        public enum MessageType
        {
            Error,
        }

        public struct Message
        {
            public string Path;
            public int Line, Column;
            public MessageType Type;
            public string Text;
        }

        #region Callbacks
        public delegate void MessageDelegate(ref Message message);
        public MessageDelegate OnMessage;

        public delegate Stream OpenFileDelegate(string path, FileMode mode);
        public OpenFileDelegate OnOpenFile;
        #endregion

        #region Callback implementation & interface
        private void DefaultMessageHandler(ref Message msg)
        {
            Console.WriteLine("{0}({1}:{2}): {3}: {4}",
                msg.Path, msg.Line + 1, msg.Column + 1, msg.Type, msg.Text);
        }

        private Stream DefaultOpenFileHandler(string path, FileMode mode)
        {
            return new FileStream(path, mode);
        }

        private void Error(string path, SourceLocation location, string text)
        {
            if (OnMessage == null)
                return;

            var msg = new Message()
            {
                Path = path,
                Line = location.Line,
                Column = location.Column,
                Type = MessageType.Error,
                Text = text,
            };

            OnMessage(ref msg);
        }

        private Stream OpenFile(string path, FileMode mode)
        {
            return OnOpenFile(path, mode);
        }
        #endregion

        private string ReadFile(string path)
        {
            using (var f = OpenFile(path, FileMode.Open))
            {
                using (var tr = new StreamReader(f))
                {
                    return tr.ReadToEnd();
                }
            }
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
