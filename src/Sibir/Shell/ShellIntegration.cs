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

        /// <summary>Menu is registered AND points to this exe (a 1.0.0 menu pointing to Sibir.exe is not current).</summary>
        public static bool IsInstalledAt(RegistryKey classesRoot, string exePath)
        {
            using (var k = classesRoot.OpenSubKey(@"Directory\shell\SibirExile\command"))
            {
                var cmd = k?.GetValue(null) as string;
                return cmd != null && cmd.StartsWith("\"" + exePath + "\"", StringComparison.OrdinalIgnoreCase);
            }
        }

        private static readonly string[] ShortcutNames = { "Перессылкин", "Peressylkin" };

        /// <summary>"Перессылкин" for a Russian Windows UI, "Peressylkin" otherwise.</summary>
        public static string ShortcutName(System.Globalization.CultureInfo ui) =>
            ui.TwoLetterISOLanguageName == "ru" ? ShortcutNames[0] : ShortcutNames[1];

        /// <summary>Creates &lt;dir&gt;\&lt;ShortcutName&gt;.lnk pointing to the exe. Returns false on failure.</summary>
        public static bool CreateShortcut(string dir, string exePath, System.Globalization.CultureInfo ui)
        {
            try
            {
                Directory.CreateDirectory(dir);
                var lnk = Call(WshShell(), "CreateShortcut", Path.Combine(dir, ShortcutName(ui) + ".lnk"));
                Set(lnk, "TargetPath", exePath);
                Set(lnk, "WorkingDirectory", Path.GetDirectoryName(exePath));
                Set(lnk, "IconLocation", exePath + ",0");
                Call(lnk, "Save");
                return true;
            }
            catch (Exception) { return false; }
        }

        /// <summary>Deletes our shortcuts (either name) in dir whose target is exePath; foreign ones stay.</summary>
        public static void RemoveShortcuts(string dir, string exePath)
        {
            foreach (var name in ShortcutNames)
            {
                var path = Path.Combine(dir, name + ".lnk");
                if (!File.Exists(path)) continue;
                try
                {
                    var target = (string)Get(Call(WshShell(), "CreateShortcut", path), "TargetPath");
                    if (string.Equals(Path.GetFullPath(target), Path.GetFullPath(exePath), StringComparison.OrdinalIgnoreCase))
                        File.Delete(path);
                }
                catch (Exception) { }
            }
        }

        private static string Programs => Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        private static string Desktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        /// <summary>Install-time shortcuts with the current UI language; failures never fail install.</summary>
        public static void CreateShortcuts(string exePath, bool startMenu, bool desktop)
        {
            var ui = System.Globalization.CultureInfo.CurrentUICulture;
            if (startMenu) CreateShortcut(Programs, exePath, ui);
            if (desktop) CreateShortcut(Desktop, exePath, ui);
        }

        /// <summary>Uninstall: our shortcuts in the Start menu and on the desktop.</summary>
        public static void RemoveAllShortcuts()
        {
            RemoveShortcuts(Programs, InstalledExe);
            RemoveShortcuts(Desktop, InstalledExe);
        }

        private static object WshShell() => Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        private static object Call(object o, string m, params object[] a) => o.GetType().InvokeMember(m, System.Reflection.BindingFlags.InvokeMethod, null, o, a);
        private static object Get(object o, string p) => o.GetType().InvokeMember(p, System.Reflection.BindingFlags.GetProperty, null, o, null);
        private static void Set(object o, string p, object v) => o.GetType().InvokeMember(p, System.Reflection.BindingFlags.SetProperty, null, o, new[] { v });

        /// <summary>Exe name of version 1.0.0; removed on install.</summary>
        public const string LegacyExe = "Sibir.exe";

        /// <summary>Copies the running exe and its icons into the install dir. Returns the installed exe path.</summary>
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
            return target;
        }

        /// <summary>Call after the menu points to the new exe: removes 1.0.0's Sibir.exe, best effort.</summary>
        public static void RemoveLegacyExe(string installDir = null)
        {
            try { File.Delete(Path.Combine(installDir ?? InstallDir, LegacyExe)); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
