using System.Collections.Generic;
using System.IO;
using Sibir.Core;

namespace Sibir.Tests
{
    public class DestinationTests
    {
        private static FakeEnv Env() => new FakeEnv().Drive(@"C:\", "volC", 900).Drive(@"D:\", "volD", 50).Drive(@"E:\", "volE", 200).Drive(@"F:\", "volF", 900, removable: true);

        [Fact]
        public void DefaultRoot_CapitalExcluded()
        {
            var env = new FakeEnv().Drive(@"C:\", "volC", 900).Drive(@"D:\", "volD", 10).Drive(@"E:\", "volE", 50);
            Assert.Equal(@"E:\Сибирь", Destination.DefaultRoot(@"D:\x", new Settings { LastRoot = @"C:\Сибирь" }, env));
            Assert.Equal(@"C:\Сибирь", Destination.DefaultRoot(@"D:\x", new Settings { LastRoot = @"C:\Сибирь", ProtectCapital = false }, env));
        }

        [Fact]
        public void DefaultRoot_RegimeDriveExcluded()
        {
            var env = new FakeEnv().Drive(@"C:\", "volC", 100).Drive(@"G:\", "volG", 2000).Drive(@"T:\", "volT", 100);
            Assert.Equal(@"T:\Сибирь", Destination.DefaultRoot(@"C:\x", new Settings { RegimeDrives = new List<string> { "G:" } }, env));
        }

        [Fact]
        public void DefaultRoot_LastUsedValid() =>
            Assert.Equal(@"T:\Сибирь", Destination.DefaultRoot(@"C:\x", new Settings { LastRoot = @"T:\Сибирь" }, Env().Drive(@"T:\", "volT", 1)));

        [Fact]
        public void DefaultRoot_LastUsedOnSourceDrive_FallsBackToMostFree() =>
            Assert.Equal(@"E:\Сибирь", Destination.DefaultRoot(@"C:\x", new Settings { LastRoot = @"C:\x" }, Env()));

        [Fact]
        public void DefaultRoot_NoOtherDrive_Null() =>
            Assert.Null(Destination.DefaultRoot(@"C:\x", new Settings(), new FakeEnv().Drive(@"C:\", "volC", 100)));

        [Fact]
        public void MapTarget_Mirror() =>
            Assert.Equal(@"T:\Сибирь\C\Users\rylov\AppData\Roaming\Avorion", Destination.MapTarget(@"C:\Users\rylov\AppData\Roaming\Avorion", @"T:\Сибирь", false));

        [Fact]
        public void MapTarget_ByName() =>
            Assert.Equal(@"T:\Сибирь\Avorion", Destination.MapTarget(@"C:\Users\rylov\AppData\Roaming\Avorion\", @"T:\Сибирь\", true));

        [Fact]
        public void Settings_CorruptFile_Defaults()
        {
            using var t = new TempDir();
            var p = t.Sub("s.json");
            File.WriteAllText(p, "{ broken");
            var s = Settings.Load(p);
            Assert.True(s.ProtectCapital);
            Assert.Empty(s.RegimeDrives);
            new Settings { LastRoot = @"T:\Сибирь", RegimeDrives = new List<string> { "G:" } }.Save(p);
            Assert.Equal(@"T:\Сибирь", Settings.Load(p).LastRoot);
        }
    }
}
