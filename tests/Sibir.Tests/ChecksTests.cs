using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sibir.Core;
using Sibir.Native;

namespace Sibir.Tests
{
    /// <summary>Layout: t\c = system volume C, t\d = volume D, t\t = Siberia volume T, t\g = regime G.</summary>
    public class ChecksTests : IDisposable
    {
        private readonly TempDir _t = new TempDir();
        private readonly FakeEnv _env;
        private readonly Archive _archive;
        private readonly string _root;

        public ChecksTests()
        {
            _env = new FakeEnv().Drive(@"C:\", "volC", 100).Drive(@"T:\", "volT", 100).Drive(@"D:\", "volD", 100).Drive(@"G:\", "volG", 100).Drive(@"N:\", "volN", 100, network: true);
            _env.Map(_t.Sub("c"), "volC").Map(_t.Sub("t"), "volT").Map(_t.Sub("d"), "volD").Map(_t.Sub("g"), "volG").Map(_t.Sub("n"), "volN");
            _archive = new Archive(_t.Sub("archive.jsonl"));
            _root = _t.Sub(@"t\Сибирь");
        }

        public void Dispose() => _t.Dispose();

        private string Src(string rel, int bytes = 10)
        {
            var p = _t.Sub(rel);
            Directory.CreateDirectory(p);
            File.WriteAllBytes(Path.Combine(p, "f.bin"), new byte[bytes]);
            return p;
        }

        private CheckResult One(string src, string root = null, CheckOptions o = null, Func<string, LauncherHit> detect = null) =>
            Checks.Run(new[] { src }, root ?? _root, o ?? new CheckOptions(), _env, _archive, detect ?? (_ => null))[0];

        private static void Is(CheckResult r, Verdict v, string reason) { Assert.Equal(reason, r.Reason, "reason "); Assert.Equal(v, r.Verdict, "verdict "); }

        [Fact] public void Ok_Basic()
        {
            var r = One(Src(@"c\Avorion"));
            Is(r, Verdict.Ok, "ok");
            Assert.Equal(10L, r.Bytes);
            Assert.Equal(Destination.MapTarget(_t.Sub(@"c\Avorion"), _root, false), r.Target);
        }

        [Fact] public void NotFound() => Is(One(_t.Sub(@"c\nope")), Verdict.Refused, "not_found");

        [Fact] public void AlreadyExiled_JunctionAndChildOfJunction()
        {
            var real = Src(@"t\real"); var link = _t.Sub(@"c\link");
            Junction.Create(link, real);
            Directory.CreateDirectory(Path.Combine(real, "sub"));
            Is(One(link), Verdict.Refused, "already_exiled");
            Is(One(Path.Combine(link, "sub")), Verdict.Refused, "already_exiled");
        }

        [Fact] public void Protected_WindowsAndProfile()
        {
            Is(One(Environment.GetFolderPath(Environment.SpecialFolder.Windows)), Verdict.Refused, "protected");
            Is(One(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)), Verdict.Refused, "protected");
        }

        [Fact] public void InSiberia_TargetOfExiledRecord_AndInsideRoot()
        {
            var tgt = Src(@"t\Сибирь\C\x");
            _archive.Write(new ArchiveRecord { Id = "1", Source = @"C:\x", Target = tgt, Status = "exiled" });
            Is(One(tgt, root: _t.Sub(@"d\Сибирь")), Verdict.Refused, "in_siberia");
            Is(One(Src(@"t\Сибирь\C\x\inner"), root: _t.Sub(@"d\Сибирь")), Verdict.Refused, "in_siberia");
            Is(One(Src(@"t\Сибирь\other")), Verdict.Refused, "in_siberia");
        }

        [Fact] public void CaseOpen_ArchiveSaysExiledButRealFolder()
        {
            var src = Src(@"c\Zomboid");
            _archive.Write(new ArchiveRecord { Id = "1", Source = src, Target = _t.Sub(@"t\x"), Status = "exiled" });
            Is(One(src), Verdict.Refused, "case_open");
        }

        [Fact] public void TargetIsSource() { var s = Src(@"d\A"); Is(One(s, root: Path.Combine(s, "Сибирь")), Verdict.Refused, "target_is_source"); }

        [Fact] public void Capital_RefusedUnlessDisabled()
        {
            var s = Src(@"d\A");
            var r = One(s, root: _t.Sub(@"c\Сибирь"));
            Is(r, Verdict.Refused, "capital");
            Assert.Equal("C — столица. Ссыльным — от 101-го километра", r.Message);
            Is(One(s, root: _t.Sub(@"c\Сибирь"), o: new CheckOptions { Settings = new Settings { ProtectCapital = false } }), Verdict.Ok, "ok");
        }

        [Fact] public void RegimeCity()
        {
            var r = One(Src(@"c\A"), root: _t.Sub(@"g\Сибирь"), o: new CheckOptions { Settings = new Settings { RegimeDrives = new List<string> { "G:" } } });
            Is(r, Verdict.Refused, "regime_city");
            Assert.Equal("G: — режимный город", r.Message);
        }

        [Fact] public void SameDrive() => Is(One(Src(@"t\A")), Verdict.Refused, "same_drive");

        [Fact] public void Checks_SameVolumeViaDifferentSpelling_Refused()
        {
            var s = Src(@"c\x");
            var o = new CheckOptions { Settings = new Settings { ProtectCapital = false } };
            Is(One(s.ToLowerInvariant() + @"\", root: _t.Sub(@"C\..\C\Сибирь").ToUpperInvariant(), o: o), Verdict.Refused, "same_drive");
            _env.Map(@"S:\", "volC");   // subst S: -> C:
            Is(One(s, root: @"S:\Сибирь", o: o), Verdict.Refused, "same_drive");
        }

        [Fact] public void NetworkTarget() => Is(One(Src(@"c\A"), root: _t.Sub(@"n\Сибирь")), Verdict.Refused, "network_target");

        [Fact] public void NoSpace()
        {
            _env.DriveList.First(d => d.VolumeId == "volT").FreeBytes = 5;
            Is(One(Src(@"c\A", 10)), Verdict.Refused, "no_space");
        }

        [Fact] public void NoSpace_Cumulative()
        {
            _env.DriveList.First(d => d.VolumeId == "volT").FreeBytes = 15;
            var r = Checks.Run(new[] { Src(@"c\A", 10), Src(@"c\B", 10) }, _root, new CheckOptions(), _env, _archive, _ => null);
            Is(r[0], Verdict.Ok, "ok");
            Is(r[1], Verdict.Refused, "no_space");
        }

        [Fact] public void RemovableTarget_Warn()
        {
            _env.DriveList.First(d => d.VolumeId == "volT").IsRemovable = true;
            Is(One(Src(@"c\A")), Verdict.Warn, "removable_target");
        }

        [Fact] public void Nested_ChildRefused()
        {
            var a = Src(@"c\A"); var b = Src(@"c\A\B");
            var r = Checks.Run(new[] { b, a }, _root, new CheckOptions(), _env, _archive, _ => null);
            Is(r[0], Verdict.Refused, "nested");
            Is(r[1], Verdict.Ok, "ok");
        }

        [Fact] public void TargetExists_NonEmpty()
        {
            var s = Src(@"c\A");
            var tgt = Destination.MapTarget(s, _root, false);
            Directory.CreateDirectory(tgt); File.WriteAllText(Path.Combine(tgt, "x"), "x");
            Is(One(s), Verdict.Refused, "target_exists");
        }

        [Fact] public void Launchers()
        {
            var s = Src(@"c\Game");
            LauncherHit Hit(LauncherKind k) => new LauncherHit { Launcher = "Steam", Kind = k, Hint = "h" };
            Is(One(s, detect: _ => Hit(LauncherKind.Managed)), Verdict.Refused, "launcher");
            Is(One(s, o: new CheckOptions { IgnoreLauncher = true }, detect: _ => Hit(LauncherKind.Managed)), Verdict.Ok, "ok");
            Is(One(s, o: new CheckOptions { IgnoreLauncher = true }, detect: _ => Hit(LauncherKind.StoreApp)), Verdict.Refused, "store_app");
            var reg = One(s, detect: _ => Hit(LauncherKind.Registered));
            Is(reg, Verdict.Warn, "registered_app");
            Assert.NotNull(reg.Launcher);
        }
    }
}
