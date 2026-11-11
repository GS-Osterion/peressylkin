using System.Collections.Generic;
using System.Linq;
using Sibir.Core;

namespace Sibir.Tests
{
    internal sealed class FakeLaunchers : ILauncherEnv
    {
        public List<string> Steam = new List<string>(), Epic = new List<string>(), Gog = new List<string>(), Uninstall = new List<string>();
        public List<(string, string)> Other = new List<(string, string)>();
        public IEnumerable<string> SteamLibraries() => Steam;
        public IEnumerable<string> EpicInstallDirs() => Epic;
        public IEnumerable<string> GogInstallDirs() => Gog;
        public IEnumerable<(string Launcher, string Dir)> OtherLauncherDirs() => Other;
        public IEnumerable<string> UninstallLocations() => Uninstall;
    }

    public class LauncherDetectTests
    {
        [Fact] public void Steam_GameFolder_Managed()
        {
            var env = new FakeLaunchers { Steam = { @"K:\SteamLibrary" } };
            var h = LauncherDetect.Detect(@"K:\SteamLibrary\steamapps\common\Avorion", env);
            Assert.Equal(LauncherKind.Managed, h.Kind);
            Assert.Equal("Steam", h.Launcher);
            Assert.Equal(LauncherKind.Managed, LauncherDetect.Detect(@"k:\steamlibrary\steamapps\common\Avorion\data", env).Kind);
        }

        [Fact] public void Steam_LibraryRoot_NotMatched() =>
            Assert.Null(LauncherDetect.Detect(@"K:\SteamLibrary", new FakeLaunchers { Steam = { @"K:\SteamLibrary" } }));

        [Fact] public void Epic_InstallDir_Managed() =>
            Assert.Equal("Epic Games", LauncherDetect.Detect(@"D:\Epic\Fortnite", new FakeLaunchers { Epic = { @"D:\Epic\Fortnite" } }).Launcher);

        [Fact] public void Store_WindowsApps_StoreApp()
        {
            Assert.Equal(LauncherKind.StoreApp, LauncherDetect.Detect(@"C:\Program Files\WindowsApps\Foo", new FakeLaunchers()).Kind);
            Assert.Equal(LauncherKind.StoreApp, LauncherDetect.Detect(@"E:\XboxGames\Halo", new FakeLaunchers()).Kind);
        }

        [Fact] public void Uninstall_Registered()
        {
            var env = new FakeLaunchers { Uninstall = { @"C:\Games\Zomboid", @"C:\", "" } };
            Assert.Equal(LauncherKind.Registered, LauncherDetect.Detect(@"C:\Games\Zomboid", env).Kind);
            Assert.Null(LauncherDetect.Detect(@"C:\Other", env));
        }

        [Fact] public void AppData_NoMatch() =>
            Assert.Null(LauncherDetect.Detect(@"C:\Users\rylov\AppData\Roaming\Avorion", new FakeLaunchers { Steam = { @"C:\Program Files (x86)\Steam" } }));

        [Fact] public void Vdf_ParsesPaths()
        {
            var vdf = "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"C:\\\\Program Files (x86)\\\\Steam\"\n\t}\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"K:\\\\SteamLibrary\"\n\t}\n}";
            var paths = RealLauncherEnv.ParseVdfPaths(vdf).ToList();
            Assert.Equal(2, paths.Count);
            Assert.Equal(@"C:\Program Files (x86)\Steam", paths[0]);
            Assert.Equal(@"K:\SteamLibrary", paths[1]);
        }

        [Fact(Skip = "manual: real machine Steam libraries")] public void Real_SeesSteamLibrary() =>
            Assert.Contains(@"K:\SteamLibrary", new RealLauncherEnv().SteamLibraries().ToList());
    }
}
