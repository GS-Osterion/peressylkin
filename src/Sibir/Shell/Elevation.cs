using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Security.Principal;

namespace Sibir.Shell
{
    internal static class Elevation
    {
        public static bool IsAdmin() => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

        /// <summary>Restart this exe with "runas" ("Требуется санкция прокурора"). False if the user declined UAC.</summary>
        public static bool Relaunch(string[] args)
        {
            var psi = new ProcessStartInfo(Assembly.GetEntryAssembly().Location, string.Join(" ", args.Select(a => "\"" + a.TrimEnd('\\') + "\"")))
            {
                Verb = "runas", UseShellExecute = true,
            };
            try { Process.Start(psi); return true; }
            catch (Win32Exception e) when (e.NativeErrorCode == 1223) { return false; }
        }
    }
}
