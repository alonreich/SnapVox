using snapvox.native;
using snapvox.native.foundation;
using System;
using System.Collections.Generic;
using System.Linq;
using snapvox.foundation.Interop;
using Kernel32Api = snapvox.foundation.Interop.Kernel32Api;

namespace snapvox.helpers
{
    public class CommandLineOptions
    {
        public string[] Files { get; set; } = [];
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
                if (arg.StartsWith("-", StringComparison.Ordinal) || arg.StartsWith("/", StringComparison.Ordinal))
                {
                    continue;
                }

                files.Add(arg);
            }

            options.Files = files.ToArray();

            if (allocatedNewConsole)
            {
                Console.ReadKey();
            }

            return options;
        }

        private static void PrintHelp()
        {
            Console.WriteLine("snapvox");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  snapvox [options] [files]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --help, -h, -?         Show help.");
        }
    }
}
