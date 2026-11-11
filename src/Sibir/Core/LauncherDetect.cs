using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Sibir.Native;

namespace Sibir.Core
{
    internal interface ILauncherEnv
    {
        IEnumerable<string> SteamLibraries();
        IEnumerable<string> EpicInstallDirs();
        IEnumerable<string> GogInstallDirs();
        IEnumerable<(string Launcher, string Dir)> OtherLauncherDirs();
        IEnumerable<string> UninstallLocations();
    }

    internal static class LauncherDetect
    {
        private const string SelfMoveHint = "Лаунчер умеет переносить игру сам или подхватить её из новой папки";

        public static LauncherHit Detect(string path, ILauncherEnv env)
        {
            var p = LongPath.Normalize(path);
            if (p.IndexOf(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase) >= 0 || p.EndsWith(@"\WindowsApps", StringComparison.OrdinalIgnoreCase)
                || LongPath.SameOrUnder(p, Path.GetPathRoot(p) + "XboxGames"))
                return new LauncherHit { Launcher = "Microsoft Store", Kind = LauncherKind.StoreApp, Hint = Strings.StoreApp };

            foreach (var lib in Safe(env.SteamLibraries))
            {
                var common = Path.Combine(lib, "steamapps", "common");
                if (LongPath.SameOrUnder(p, common) && !LongPath.Same(p, common))
                    return new LauncherHit { Launcher = "Steam", Kind = LauncherKind.Managed, Hint = "Steam → Свойства → Установленные файлы → Переместить" };
            }
            foreach (var d in Safe(env.EpicInstallDirs))
                if (Under(p, d)) return Managed("Epic Games");
            foreach (var d in Safe(env.GogInstallDirs))
                if (Under(p, d)) return Managed("GOG Galaxy");
            foreach (var (l, d) in Safe(env.OtherLauncherDirs))
                if (Under(p, d)) return Managed(l);
            foreach (var d in Safe(env.UninstallLocations))
                if (Under(p, d)) return new LauncherHit { Launcher = "установщик программы", Kind = LauncherKind.Registered, Hint = Strings.RegisteredApp };
            return null;
        }

        private static LauncherHit Managed(string name) => new LauncherHit { Launcher = name, Kind = LauncherKind.Managed, Hint = SelfMoveHint };

        private static bool Under(string p, string dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return false;
            try { var n = LongPath.Normalize(dir.Trim('"')); return n.Length > 3 && LongPath.SameOrUnder(p, n); }
            catch { return false; }
        }

        private static List<T> Safe<T>(Func<IEnumerable<T>> f)
        {
            try { return f().ToList(); } catch { return new List<T>(); }
        }
    }

    /// <summary>Reads launcher configs and registry. Read-only; any failure means "not installed".</summary>
    internal sealed class RealLauncherEnv : ILauncherEnv
    {
        public static IEnumerable<string> ParseVdfPaths(string vdf) =>
            Regex.Matches(vdf, "\"path\"\\s+\"(.+?)\"").Cast<Match>().Select(m => m.Groups[1].Value.Replace(@"\\", @"\"));

        public IEnumerable<string> SteamLibraries()
        {
            var steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
            if (steam == null) yield break;
            steam = steam.Replace('/', '\\');
            yield return steam;
            var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) yield break;
            foreach (var p in ParseVdfPaths(File.ReadAllText(vdf))) yield return p;
        }

        public IEnumerable<string> EpicInstallDirs()
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Epic\EpicGamesLauncher\Data\Manifests");
            if (!Directory.Exists(dir)) yield break;
            foreach (var f in Directory.GetFiles(dir, "*.item"))
            {
                var m = Regex.Match(File.ReadAllText(f), "\"InstallLocation\"\\s*:\\s*\"(.+?)\"");
                if (m.Success) yield return m.Groups[1].Value.Replace(@"\\", @"\");
            }
        }

        public IEnumerable<string> GogInstallDirs() => SubkeyValues(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\GOG.com\Games", "path");

        public IEnumerable<(string Launcher, string Dir)> OtherLauncherDirs()
        {
            foreach (var d in SubkeyValues(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Electronic Arts", "Install Dir")) yield return ("EA app", d);
            foreach (var d in SubkeyValues(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs", "InstallDir")) yield return ("Ubisoft Connect", d);
            foreach (var (pub, loc) in Uninstall())
                if (string.Equals(pub, "Blizzard Entertainment", StringComparison.OrdinalIgnoreCase)) yield return ("Battle.net", loc);
        }

        public IEnumerable<string> UninstallLocations() => Uninstall().Select(u => u.Location);

        private static IEnumerable<(string Publisher, string Location)> Uninstall()
        {
            var keys = new[] { (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                               (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
                               (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall") };
            foreach (var (hive, path) in keys)
            {
                using (var k = hive.OpenSubKey(path))
                {
                    if (k == null) continue;
                    foreach (var name in k.GetSubKeyNames())
                        using (var s = k.OpenSubKey(name))
                        {
                            var loc = s?.GetValue("InstallLocation") as string;
                            if (!string.IsNullOrWhiteSpace(loc)) yield return (s.GetValue("Publisher") as string, loc);
                        }
                }
            }
        }

        private static IEnumerable<string> SubkeyValues(RegistryKey hive, string path, string value)
        {
            using (var k = hive.OpenSubKey(path))
            {
                if (k == null) yield break;
                foreach (var name in k.GetSubKeyNames())
                    using (var s = k.OpenSubKey(name))
                        if (s?.GetValue(value) is string v && v.Length > 0) yield return v;
            }
        }
    }
}
