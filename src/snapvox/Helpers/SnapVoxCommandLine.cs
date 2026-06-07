using snapvox.native;
using snapvox.native.foundation;
using snapvox.native.graphics;
using snapvox.native.ui;
using System;
using System.Collections.Generic;
using System.Linq;
using snapvox.foundation.Interop;
using Kernel32Api = snapvox.foundation.Interop.Kernel32Api;

namespace snapvox.helpers
{
    public class CommandLineOptions
    {
        public bool Exit { get; set; }

        public bool Reload { get; set; }

        public bool NoRun { get; set; }

        public string Language { get; set; }

        public string IniDirectory { get; set; }

        public string[] Files { get; set; } = [];

        public bool Restore { get; set; }

        public bool AutoRun { get; set; }
    }

    internal static class snapvoxCommandLine
    {
        public static CommandLineOptions Parse(string[] args)
        {
            bool needsConsole = args.Any(a => a is "--help" or "-h" or "-?");
            bool allocatedNewConsole = false;
            if (needsConsole)
            {
                bool attached = Kernel32Api.AttachConsole();
                if (!attached)
                {
                    Kernel32Api.AllocConsole();
                    allocatedNewConsole = true;
                }

                PrintHelp();
            }

            var options = new CommandLineOptions();
            var files = new List<string>();

            for (int index = 0; index < args.Length; index++)
            {
                string arg = args[index];
                string lower = arg.ToLowerInvariant();

                if (TryReadValueOption(arg, "--language", out string language))
                {
                    options.Language = language;
                    continue;
                }

                if (TryReadValueOption(arg, "--ini-directory", out string iniDirectory))
                {
                    options.IniDirectory = iniDirectory;
                    continue;
                }

                switch (lower)
                {
                    case "--exit":
                        options.Exit = true;
                        continue;
                    case "--reload":
                        options.Reload = true;
                        continue;
                    case "--no-run":
                        options.NoRun = true;
                        continue;
                    case "--restore":
                        options.Restore = true;
                        continue;
                    case "--autorun":
                        options.AutoRun = true;
                        continue;
                    case "--language":
                        if (index + 1 < args.Length)
                        {
                            options.Language = args[++index];
                        }
                        continue;
                    case "--ini-directory":
                        if (index + 1 < args.Length)
                        {
                            options.IniDirectory = args[++index];
                        }
                        continue;
                    case "--help":
                    case "-h":
                    case "-?":
                        continue;
                }

                if (!arg.StartsWith("-", StringComparison.Ordinal))
                {
                    files.Add(arg);
                }
            }

            options.Files = files.ToArray();

            if (allocatedNewConsole)
            {
                Console.ReadKey();
            }

            return options;
        }

        private static bool TryReadValueOption(string arg, string optionName, out string value)
        {
            value = null;
            string prefix = optionName + "=";
            if (!arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            value = arg.Substring(prefix.Length);
            return true;
        }

        private static void PrintHelp()
        {
            Console.WriteLine("snapvox");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  snapvox [options] [files]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --exit                 Send an exit command to running instances.");
            Console.WriteLine("  --reload               Send a reload-configuration command to running instances.");
            Console.WriteLine("  --no-run               Exit immediately without starting or showing the app.");
            Console.WriteLine("  --language <code>      Set the UI language.");
            Console.WriteLine("  --ini-directory <dir>  Set the configuration directory.");
            Console.WriteLine("  --help, -h, -?         Show help.");
        }
    }
}
