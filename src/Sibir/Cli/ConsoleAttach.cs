using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Sibir.Native;

namespace Sibir.Cli
{
    /// <summary>GUI-subsystem exe in CLI mode: use redirected handles as-is, otherwise attach to the parent console.</summary>
    internal static class ConsoleAttach
    {
        [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int n);
        [DllImport("kernel32.dll")] private static extern int GetFileType(IntPtr h);

        private static bool Redirected(int std)
        {
            var h = GetStdHandle(std);
            return h != IntPtr.Zero && h != new IntPtr(-1) && GetFileType(h) != 0 /* FILE_TYPE_UNKNOWN */;
        }

        public static void Attach()
        {
            bool outRedirected = Redirected(-11), errRedirected = Redirected(-12);
            if (!outRedirected || !errRedirected) NativeMethods.AttachConsole(-1);   // ATTACH_PARENT_PROCESS
            var utf8 = new UTF8Encoding(false);
            // Redirected pipes get UTF-8; a real console keeps its own code page (no side effects on the user's console).
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), outRedirected ? utf8 : Console.OutputEncoding) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError(), errRedirected ? utf8 : Console.OutputEncoding) { AutoFlush = true });
            if (!outRedirected) Console.WriteLine();
        }
    }
}
