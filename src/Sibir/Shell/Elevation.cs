using System;
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

        /// <summary>Restart this exe with "runas" ("Требуется санкция прокурора"). False if the user declined UAC.
        /// Passes "--as &lt;SID&gt;" so the elevated process can tell it is still the same account.</summary>
        public static bool Relaunch(string[] args)
        {
            var all = args.Concat(new[] { "--as", WindowsIdentity.GetCurrent().User.Value });
            var psi = new ProcessStartInfo(Assembly.GetEntryAssembly().Location, string.Join(" ", all.Select(a => "\"" + a.TrimEnd('\\') + "\"")))
            {
                Verb = "runas", UseShellExecute = true,
            };
            try { Process.Start(psi); return true; }
            catch (Win32Exception e) when (e.NativeErrorCode == 1223) { return false; }
        }

        public static string AsSid(string[] args)
        {
            var i = Array.IndexOf(args, "--as");
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        /// <summary>Elevated as another account (UAC with someone else's password): its %LOCALAPPDATA% is not the user's.</summary>
        public static bool WrongAccount(string[] args)
        {
            var sid = AsSid(args);
            return sid != null && IsAdmin() && !string.Equals(sid, WindowsIdentity.GetCurrent().User.Value, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Arguments without "--as &lt;SID&gt;".</summary>
        public static string[] Strip(string[] args)
        {
            var i = Array.IndexOf(args, "--as");
            return i < 0 ? args : args.Take(i).Concat(args.Skip(i + 2)).ToArray();
        }
    }
}
