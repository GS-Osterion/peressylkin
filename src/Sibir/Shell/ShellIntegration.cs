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
        public const string ExeName = "Peressylkin.exe";
        public static string InstalledExe => Path.Combine(InstallDir, ExeName);

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

        /// <summary>Exe name of version 1.0.0; removed on install.</summary>
        public const string LegacyExe = "Sibir.exe";

        /// <summary>Copies the running exe and its icons into the install dir, removes the legacy exe. Returns the installed exe path.</summary>
        public static string InstallFiles(string fromExe, string installDir = null)
        {
            installDir = installDir ?? InstallDir;
            var target = Path.Combine(installDir, ExeName);
            Directory.CreateDirectory(installDir);
            var dir = Path.GetDirectoryName(fromExe);
            if (!string.Equals(Path.GetFullPath(fromExe), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                File.Copy(fromExe, target, true);
            foreach (var ico in new[] { "stamp.ico", "sibir.ico" })
            {
                var src = Path.Combine(dir, ico);
                if (File.Exists(src) && !string.Equals(dir, installDir, StringComparison.OrdinalIgnoreCase))
                    File.Copy(src, Path.Combine(installDir, ico), true);
            }
            try { File.Delete(Path.Combine(installDir, LegacyExe)); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return target;
        }
    }
}
