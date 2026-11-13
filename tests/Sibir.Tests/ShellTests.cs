using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using Microsoft.Win32;
using Sibir.Shell;

namespace Sibir.Tests
{
    public class ShellTests
    {
        private const string TestKey = @"Software\SibirTests\Classes";

        [Fact] public void Install_CreatesThreeVerbs_Uninstall_RemovesAll()
        {
            try
            {
                using (var root = Registry.CurrentUser.CreateSubKey(TestKey))
                {
                    ShellIntegration.Install(root, @"C:\Апп\Sibir.exe");
                    Assert.True(ShellIntegration.IsInstalled(root));
                    using (var k = root.OpenSubKey(@"Directory\shell\SibirExile"))
                    {
                        Assert.Equal(Strings.MenuExile, (string)k.GetValue("MUIVerb"));
                        Assert.Equal("Player", (string)k.GetValue("MultiSelectModel"));
                        Assert.Equal("\"C:\\Апп\\Sibir.exe\" --menu-exile \"%1\"", (string)k.OpenSubKey("command").GetValue(null));
                    }
                    Assert.NotNull(root.OpenSubKey(@"Directory\shell\SibirRehab\command"));
                    Assert.NotNull(root.OpenSubKey(@"Directory\shell\SibirCase\command"));
                    ShellIntegration.Uninstall(root);
                    Assert.False(ShellIntegration.IsInstalled(root));
                    Assert.Null(root.OpenSubKey(@"Directory\shell\SibirRehab"));
                }
            }
            finally { Registry.CurrentUser.DeleteSubKeyTree(@"Software\SibirTests", false); }
        }

        [Fact] public void InstallFiles_RemovesLegacyExe()
        {
            using (var t = new TempDir())
            {
                var from = t.Sub(@"dl\Peressylkin.exe"); System.IO.Directory.CreateDirectory(t.Sub("dl")); System.IO.File.WriteAllText(from, "new");
                var dir = t.Sub("inst"); System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "Sibir.exe"), "old");
                var exe = ShellIntegration.InstallFiles(from, dir);
                Assert.Equal(System.IO.Path.Combine(dir, "Peressylkin.exe"), exe);
                Assert.Equal("new", System.IO.File.ReadAllText(exe));
                Assert.False(System.IO.File.Exists(System.IO.Path.Combine(dir, "Sibir.exe")), "legacy Sibir.exe removed");
            }
        }

        [Fact] public void InstallFiles_LegacyLocked_StillSucceeds()
        {
            using (var t = new TempDir())
            {
                var from = t.Sub(@"dl\Peressylkin.exe"); System.IO.Directory.CreateDirectory(t.Sub("dl")); System.IO.File.WriteAllText(from, "new");
                var dir = t.Sub("inst"); System.IO.Directory.CreateDirectory(dir);
                var legacy = System.IO.Path.Combine(dir, "Sibir.exe");
                System.IO.File.WriteAllText(legacy, "old");
                using (System.IO.File.Open(legacy, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.None))
                    Assert.Equal(System.IO.Path.Combine(dir, "Peressylkin.exe"), ShellIntegration.InstallFiles(from, dir));
            }
        }

        [Fact] public void IsInstalledAt_OldExeRegistered_IsNotCurrent()
        {
            try
            {
                using (var root = Registry.CurrentUser.CreateSubKey(TestKey))
                {
                    ShellIntegration.Install(root, @"C:\Апп\Sibir.exe");
                    Assert.False(ShellIntegration.IsInstalledAt(root, @"C:\Апп\Peressylkin.exe"), "1.0.0 menu is not the current install");
                    Assert.True(ShellIntegration.IsInstalledAt(root, @"C:\Апп\Sibir.exe"), "same exe is current");
                }
            }
            finally { Registry.CurrentUser.DeleteSubKeyTree(@"Software\SibirTests", false); }
        }

        [Fact] public void ShortcutName_ByUiLanguage()
        {
            Assert.Equal("Перессылкин", ShellIntegration.ShortcutName(System.Globalization.CultureInfo.GetCultureInfo("ru-RU")));
            Assert.Equal("Peressylkin", ShellIntegration.ShortcutName(System.Globalization.CultureInfo.GetCultureInfo("en-US")));
            Assert.Equal("Peressylkin", ShellIntegration.ShortcutName(System.Globalization.CultureInfo.GetCultureInfo("de-DE")));
        }

        [Fact] public void Shortcut_CreateThenRemove()
        {
            using (var t = new TempDir())
            {
                var exe = t.Sub("Peressylkin.exe"); System.IO.File.WriteAllText(exe, "x");
                var dir = t.Sub("Programs");
                ShellIntegration.CreateShortcut(dir, exe, System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));
                Assert.True(System.IO.File.Exists(System.IO.Path.Combine(dir, "Перессылкин.lnk")), "ru shortcut");
                ShellIntegration.CreateShortcut(dir, exe, System.Globalization.CultureInfo.GetCultureInfo("en-US"));
                Assert.True(System.IO.File.Exists(System.IO.Path.Combine(dir, "Peressylkin.lnk")), "en shortcut");
                ShellIntegration.RemoveShortcuts(dir, exe);
                Assert.Empty(System.IO.Directory.GetFiles(dir, "*.lnk"));
            }
        }

        [Fact] public void RemoveShortcuts_KeepsForeignTarget()
        {
            using (var t = new TempDir())
            {
                var ours = t.Sub("Peressylkin.exe"); System.IO.File.WriteAllText(ours, "x");
                var other = t.Sub("other.exe"); System.IO.File.WriteAllText(other, "x");
                var dir = t.Sub("Desktop");
                ShellIntegration.CreateShortcut(dir, other, System.Globalization.CultureInfo.GetCultureInfo("en-US"));
                ShellIntegration.RemoveShortcuts(dir, ours);
                Assert.True(System.IO.File.Exists(System.IO.Path.Combine(dir, "Peressylkin.lnk")), "foreign shortcut kept");
            }
        }

        [Fact] public void RemoveShortcuts_MissingDir_DoesNotThrow()
        {
            using (var t = new TempDir()) ShellIntegration.RemoveShortcuts(t.Sub("nope"), t.Sub("Peressylkin.exe"));
        }

        [Fact] public void InstalledExe_IsPeressylkin() =>
            Assert.Equal("Peressylkin.exe", System.IO.Path.GetFileName(ShellIntegration.InstalledExe));

        [Fact] public void Collector_ThreeCallers_OneGetsAll()
        {
            var verb = "test" + Guid.NewGuid().ToString("N").Substring(0, 6);
            var results = new ConcurrentBag<string[]>();
            var threads = Enumerable.Range(0, 3).Select(i => new Thread(() =>
            {
                Thread.Sleep(i * 50);
                results.Add(Collector.Gather(verb, @"C:\p" + i, TimeSpan.FromMilliseconds(400)));
            })).ToList();
            threads.ForEach(t => t.Start());
            threads.ForEach(t => t.Join());
            var owners = results.Where(r => r != null).ToList();
            Assert.Equal(1, owners.Count);
            Assert.Equal(3, owners[0].Length);
            Assert.Equal(2, results.Count(r => r == null));
        }
    }
}
