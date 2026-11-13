using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Sibir.Core;
using Sibir.Native;

namespace Sibir.Tests
{
    /// <summary>Real subst drives (DefineDosDevice, per user, no admin), removed after each test.</summary>
    public class SubstTests : IDisposable
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool DefineDosDevice(int flags, string device, string target);
        private const int DDD_REMOVE_DEFINITION = 2;

        private readonly TempDir _t = new TempDir();
        private string _drive;

        public void Dispose()
        {
            if (_drive != null) DefineDosDevice(DDD_REMOVE_DEFINITION, _drive, null);
            _t.Dispose();
        }

        private string Subst(string target)
        {
            var used = DriveInfo.GetDrives().Select(d => d.Name.Substring(0, 2).ToUpperInvariant()).ToList();
            _drive = "ZYXWVUTSRQPONMLKJ".Select(c => c + ":").First(d => !used.Contains(d));
            Assert.True(DefineDosDevice(0, _drive, target), "subst created");
            return _drive;
        }

        [Fact] public void ResolveSubst_MapsToRealPath()
        {
            var real = _t.Sub(@"real\dir"); Directory.CreateDirectory(real);
            var s = Subst(_t.Sub("real"));
            Assert.Equal(LongPath.Normalize(real), LongPath.ResolveSubst(s + @"\dir"));
            Assert.Equal(LongPath.Normalize(_t.Sub("real")), LongPath.ResolveSubst(s + @"\"));
        }

        [Fact] public void ResolveSubst_OrdinaryPathUnchanged() =>
            Assert.Equal(LongPath.Normalize(_t.Sub("x")), LongPath.ResolveSubst(_t.Sub("x")));

        [Fact] public void Checks_SubstAliasOfArchiveAncestor_Refused()
        {
            var env = new FakeEnv().Drive(@"C:\", "volC", 100).Drive(@"T:\", "volT", 100);
            env.Map(_t.Sub("c"), "volC").Map(_t.Sub("t"), "volT");
            var appData = _t.Sub(@"c\AppData");
            Directory.CreateDirectory(Path.Combine(appData, @"Local\Sibir"));
            var s = Subst(appData);
            var r = Checks.Run(new[] { s + @"\Local" }, _t.Sub(@"t\Сибирь"), new CheckOptions { ArchiveDir = Path.Combine(appData, @"Local\Sibir") },
                env, new Archive(_t.Sub(@"arch\archive.jsonl")), _ => null)[0];
            Assert.Equal("peressylkin_property", r.Reason);
            Assert.Equal(LongPath.Normalize(Path.Combine(appData, "Local")), r.Path);
        }
    }
}
