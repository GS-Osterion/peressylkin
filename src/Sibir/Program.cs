using System;

namespace Sibir
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (Cli.Cli.IsCliCommand(args))
            {
                Cli.ConsoleAttach.Attach();
                int code = Cli.Cli.Run(args, Console.Out, Console.Error, Cli.CliContext.Real());
                Console.Out.Flush(); Console.Error.Flush();
                return code;
            }
            return Gui.GuiApp.Run(args);
        }
    }
}
