using System;
using System.IO;
using System.Linq;
using System.Threading;
using Sibir.Core;
using Sibir.Native;

namespace Sibir.Tests
{
    internal sealed class Hook : IFaultHook
    {
        public Step At; public Action<string> Do;
        public Hook(Step at, Action<string> d) { At = at; Do = d; }
        public void Before(Step step, string source) { if (step == At) Do(source); }
    }

    public class MoverTests : IDisposable
    {
        private readonly TempDir _t = new TempDir();
        private readonly Archive _archive;
        private readonly string _src, _tgt;

        public MoverTests()
        {
            _archive = new Archive(_t.Sub("archive.jsonl"));
            _src = _t.Sub(@"home\Avorion");
            _tgt = _t.Sub(@"siberia\C\Avorion");
            TreeMaker.Make(_src, smallFiles: 200);
        }

        public void Dispose() => _t.Dispose();

        private Mover M(IFaultHook hook = null, bool crash = false) =>
            new Mover(_archive, new MoverOptions { StampIconPath = @"C:\x\stamp.ico" }, hook) { SimulateCrash = crash };

        private MoveResult Exile(Mover m) => m.Exile(_src, _tgt, null, CancellationToken.None);

        private string Status() => _archive.Current().Last().Status;

        [Fact] public void Mover_Exile_Roundtrip()
        {
            var before = Manifest.Build(_src);
            var r = Exile(M());
            Assert.True(r.Ok, r.Reason + " " + r.Message);
            Assert.True(Junction.IsReparsePoint(_src));
            Assert.Equal(LongPath.Normalize(_tgt), Junction.GetTarget(_src));
            Assert.Equal("корабль", File.ReadAllText(Path.Combine(_src, @"Сохранения\Сохранение 1.sav")));
            Assert.False(Directory.Exists(_src + ".~этап"));
            Assert.Equal("exiled", Status());
            var ini = Path.Combine(_tgt, "desktop.ini");
            Assert.Equal(FileAttributes.Hidden | FileAttributes.System, File.GetAttributes(ini) & (FileAttributes.Hidden | FileAttributes.System));
            Assert.Contains("stamp.ico", File.ReadAllText(ini));

            File.WriteAllText(Path.Combine(_src, "new.sav"), "written through link");
            Assert.True(File.Exists(Path.Combine(_tgt, "new.sav")));
            File.Delete(Path.Combine(_src, "new.sav"));

            var back = M().Rehab(_src, null, CancellationToken.None);
            Assert.True(back.Ok, back.Reason + " " + back.Message);
            Assert.False(Junction.IsReparsePoint(_src));
            Assert.Null(Manifest.Build(_src).FirstDifference(before, true));
            Assert.False(Directory.Exists(_tgt));
            Assert.Equal("rehabilitated", Status());
        }

        [Fact] public void Mover_Locked_NothingTouched()
        {
            var before = Manifest.Build(_src);
            MoveResult r;
            using (new FileStream(Path.Combine(_src, @"Сохранения\Сохранение 1.sav"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                r = Exile(M());
            Assert.False(r.Ok);
            Assert.Equal("locked", r.Reason);
            Assert.NotEmpty(r.Processes);
            Assert.Null(Manifest.Build(_src).FirstDifference(before, true));
            Assert.False(Directory.Exists(_tgt));
        }

        [Fact] public void Mover_CopyThrowsDiskFull_Amnesty()
        {
            var before = Manifest.Build(_src);
            var r = Exile(M(new Hook(Step.Copy, _ => throw new IOException("disk full"))));
            Assert.Equal("copy_failed", r.Reason);
            Assert.Null(Manifest.Build(_src).FirstDifference(before, true));
            Assert.False(Directory.Exists(_tgt + ".~пересылка"));
            Assert.Equal("amnestied", Status());
        }

        [Fact] public void Mover_FileChangedBetweenCopyAndVerify_Amnesty()
        {
            var r = Exile(M(new Hook(Step.Verify, s => File.AppendAllText(Path.Combine(s, @"Сохранения\Сохранение 1.sav"), "!"))));
            Assert.Equal("verify_failed", r.Reason);
            Assert.False(Junction.IsReparsePoint(_src));
            Assert.Equal("корабль!", File.ReadAllText(Path.Combine(_src, @"Сохранения\Сохранение 1.sav")));
            Assert.False(Directory.Exists(_tgt + ".~пересылка"));
            Assert.False(Directory.Exists(_tgt));
        }

        [Fact] public void Mover_CrashAtSwapLink_RecoverRestores()
        {
            var before = Manifest.Build(_src);
            Assert.Throws<InvalidOperationException>(() => Exile(M(new Hook(Step.SwapLink, _ => throw new InvalidOperationException("power cut")), crash: true)));
            Assert.False(Directory.Exists(_src));
            Assert.Equal(1, _archive.Unfinished().Count);
            var rec = M().Recover();
            Assert.Equal(1, rec.Count);
            Assert.False(Junction.IsReparsePoint(_src));
            Assert.Null(Manifest.Build(_src).FirstDifference(before, true));
            Assert.False(Directory.Exists(_tgt));
            Assert.Equal("amnestied", Status());
        }

        [Fact] public void Mover_CrashAtCleanup_RecoverCompletes()
        {
            Assert.Throws<InvalidOperationException>(() => Exile(M(new Hook(Step.Cleanup, _ => throw new InvalidOperationException("power cut")), crash: true)));
            Assert.True(Directory.Exists(_src + ".~этап"));
            M().Recover();
            Assert.True(Junction.IsReparsePoint(_src));
            Assert.False(Directory.Exists(_src + ".~этап"));
            Assert.Equal("exiled", Status());
        }

        [Fact] public void Mover_RehabWhenSourceReplacedByRealFolder_Refused()
        {
            Assert.True(Exile(M()).Ok);
            Junction.Delete(_src);
            Directory.CreateDirectory(_src);
            File.WriteAllText(Path.Combine(_src, "fresh.sav"), "new game");
            var r = M().Rehab(_src, null, CancellationToken.None);
            Assert.Equal("target_exists", r.Reason);
            Assert.True(File.Exists(Path.Combine(_src, "fresh.sav")));
            Assert.True(Directory.Exists(_tgt));
        }

        [Fact] public void Mover_RehabNotExiled()
        {
            var r = M().Rehab(_src, null, CancellationToken.None);
            Assert.Equal("not_exiled", r.Reason);
        }

        [Fact] public void Mover_StampExistingDesktopIni_Untouched()
        {
            File.WriteAllText(Path.Combine(_src, "desktop.ini"), "[.ShellClassInfo]\r\nIconResource=game.ico,0\r\n");
            Assert.True(Exile(M()).Ok);
            Assert.Contains("game.ico", File.ReadAllText(Path.Combine(_tgt, "desktop.ini")));
            Assert.False(_archive.Current().Last().StampCreated);
            Assert.True(M().Rehab(_src, null, CancellationToken.None).Ok);
            Assert.Contains("game.ico", File.ReadAllText(Path.Combine(_src, "desktop.ini")));
        }
    }
}
