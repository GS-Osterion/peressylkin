using System;
using System.IO;
using Microsoft.Win32;

namespace Sibir.Shell
{
    /// <summary>Explorer context menu for folders, per-user (HKCU\Software\Classes), no admin rights.</summary>
    internal static class ShellIntegration
    {
        private static readonly (string Key, string Verb, string Text)[] Verbs =
        {
            ("SibirExile", "--menu-exile", Strings.MenuExile),
            ("SibirRehab", "--menu-rehab", Strings.MenuRehab),
            ("SibirCase", "--menu-case", Strings.MenuCase),
        };

        public static string InstallDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sibir");
        public static string InstalledExe => Path.Combine(InstallDir, "Sibir.exe");

        public static RegistryKey UserClasses() => Registry.CurrentUser.CreateSubKey(@"Software\Classes");

        public static void Install(RegistryKey classesRoot, string exePath)
        {
            foreach (var (key, verb, text) in Verbs)
            {
                using (var k = classesRoot.CreateSubKey(@"Directory\shell\" + key))
                {
                    k.SetValue("MUIVerb", text);
                    k.SetValue("Icon", "\"" + exePath + "\",0");
                    k.SetValue("MultiSelectModel", "Player");
                    using (var c = k.CreateSubKey("command"))
                        c.SetValue(null, $"\"{exePath}\" {verb} \"%1\"");
                }
            }
        }

        public static void Uninstall(RegistryKey classesRoot)
        {
            foreach (var (key, _, _) in Verbs)
                classesRoot.DeleteSubKeyTree(@"Directory\shell\" + key, false);
        }

        public static bool IsInstalled(RegistryKey classesRoot)
        {
            using (var k = classesRoot.OpenSubKey(@"Directory\shell\SibirExile")) return k != null;
        }

        /// <summary>Copies the running exe and its icons into InstallDir. Returns the installed exe path.</summary>
        public static string InstallFiles(string fromExe)
        {
            Directory.CreateDirectory(InstallDir);
            var dir = Path.GetDirectoryName(fromExe);
            if (!string.Equals(Path.GetFullPath(fromExe), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase))
                File.Copy(fromExe, InstalledExe, true);
            foreach (var ico in new[] { "stamp.ico", "sibir.ico" })
            {
                var src = Path.Combine(dir, ico);
                if (File.Exists(src) && !string.Equals(dir, InstallDir, StringComparison.OrdinalIgnoreCase))
                    File.Copy(src, Path.Combine(InstallDir, ico), true);
            }
            return InstalledExe;
        }
    }
}
