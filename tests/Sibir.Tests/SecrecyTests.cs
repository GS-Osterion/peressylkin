using System;
using System.IO;
using System.Linq;
using System.Threading;
using Sibir.Core;
using Sibir.Native;

namespace Sibir.Tests
{
    public class SecrecyTests : IDisposable
    {
        private readonly TempDir _t = new TempDir();
        private readonly Archive _pub;
        private readonly SecretArchive _secret;
        private readonly string _src, _tgt, _root, _secretPath;
        private readonly Secrecy _sec;

        public SecrecyTests()
        {
            _pub = new Archive(_t.Sub(@"arch\archive.jsonl"));
            _secretPath = _t.Sub(@"arch\.secret.jsonl");
            _secret = new SecretArchive(_secretPath);
            _src = _t.Sub(@"home\Игра"); _root = _t.Sub("siberia"); _tgt = Path.Combine(_root, @"C\Игра");
            TreeMaker.Make(_src, smallFiles: 20);
            _sec = new Secrecy(_pub, _secret, new[] { _root }, @"C:\x\stamp.ico");
        }

        public void Dispose() => _t.Dispose();

        private Mover M() => new Mover(_pub, new MoverOptions { StampIconPath = @"C:\x\stamp.ico" });
        private ArchiveRecord Exiled() { Assert.True(M().Exile(_src, _tgt, null, CancellationToken.None).Ok); return _pub.Current().Last(); }
        private ArchiveRecord Rehabilitated() { Exiled(); Assert.True(M().Rehab(_src, null, CancellationToken.None).Ok); return _pub.Current().Last(); }
        private static void NeedNormal() { if (SecretArchive.Elevated) Assert.Skip("run without administrator rights"); }

        [Fact] public void Classify_Exiled_GoneFromPublic_MarksSet_StampOff()
        {
            NeedNormal();
            var r = _sec.Classify(Exiled());
            Assert.True(r.Ok, r.Message);
            Assert.Empty(_pub.Current());
            Assert.True(SecretMark.Has(_tgt), "target mark");
            Assert.True(SecretMark.Has(_root), "root mark");
            Assert.False(File.Exists(Path.Combine(_tgt, Stamp.FileName)), "stamp off");
            Assert.True(Junction.IsReparsePoint(_src), "link still works");
            Assert.True(Secrecy.IsClassifiedLink(_src, _pub));
            Assert.True(new FileInfo(_secretPath).Length > 0, "written to the secret archive");
        }

        [Fact] public void Classify_Rehabilitated_NoMark()
        {
            NeedNormal();
            Assert.True(_sec.Classify(Rehabilitated()).Ok);
            Assert.Empty(_pub.Current());
            Assert.False(SecretMark.Has(_root));
        }

        [Fact] public void Classify_Compromised_WritesNothing()
        {
            NeedNormal();
            var rec = Exiled();
            File.WriteAllText(_secretPath, "");
            var r = _sec.Classify(rec);
            Assert.Equal("safe_broken", r.Reason);
            Assert.Equal(1, _pub.Current().Count);
            Assert.Equal(0L, new FileInfo(_secretPath).Length);
            Assert.True(File.Exists(Path.Combine(_tgt, Stamp.FileName)), "stamp untouched");
        }

        [Fact] public void Classify_InterruptedThenRepeated_Idempotent()
        {
            NeedNormal();
            var rec = Exiled();
            _secret.Write(rec);                       // crash after the secret line, before the public erase
            Assert.True(_sec.Classify(_pub.Current().Last()).Ok);
            Assert.Empty(_pub.Current());
        }

        [Fact] public void Classify_InTransit_NotAllowed()
        {
            var rec = Exiled().With("in_transit");
            Assert.Equal("not_allowed", _sec.Classify(rec).Reason);
        }

        [Fact] public void Burn_Exiled_NotAllowed()
        {
            var rec = Exiled();
            Assert.Equal("not_allowed", Secrecy.Burn(rec, _pub).Reason);
            Assert.Equal(1, _pub.Current().Count);
        }

        [Fact] public void Burn_Rehabilitated_NoTrace()
        {
            var rec = Rehabilitated();
            Assert.True(Secrecy.Burn(rec, _pub).Ok);
            Assert.Empty(_pub.Current());
            Assert.False(File.ReadAllText(_pub.FilePath).Contains(rec.Id), "no line left");
            Assert.Equal(1, Directory.GetFiles(Path.GetDirectoryName(_pub.FilePath)).Length, "no backup files: ");
        }

        [Fact] public void NormalMover_ClassifiedLink_NotExiled()
        {
            NeedNormal();
            _sec.Classify(Exiled());
            Assert.Equal("not_exiled", M().Rehab(_src, null, CancellationToken.None).Reason);
            Assert.True(Junction.IsReparsePoint(_src), "untouched");
        }

        [Fact] public void Rehab_SecretCase_ProgressStaysSecret()
        {
            if (!SecretArchive.Elevated) Assert.Skip("run as administrator");
            Plaque.EnsureSiberia(_root);
            _sec.Classify(Exiled());
            var r = new Mover(_pub, new MoverOptions(), null, _secret).Rehab(_src, null, CancellationToken.None);
            Assert.True(r.Ok, r.Message);
            Assert.Empty(_pub.Current());
            Assert.Equal("rehabilitated", _secret.Current().Single().Status);
            Assert.False(SecretMark.Has(_root), "root mark cleared");
        }

        [Fact] public void Recover_SecretRehabilitating()
        {
            if (!SecretArchive.Elevated) Assert.Skip("run as administrator");
            _sec.Classify(Exiled());
            var crash = new Mover(_pub, new MoverOptions(), new Hook(Step.SwapLink, _ => throw new IOException("power cut")), _secret) { SimulateCrash = true };
            try { crash.Rehab(_src, null, CancellationToken.None); } catch (IOException) { }
            Assert.Equal("rehabilitating", _secret.Current().Single().Status);
            new Mover(_pub, new MoverOptions(), null, _secret).Recover();
            Assert.Equal("exiled", _secret.Current().Single().Status);
            Assert.Empty(_pub.Current());
        }

        [Fact] public void Declassify_RestoresStampAndPublicRecord()
        {
            if (!SecretArchive.Elevated) Assert.Skip("run as administrator");
            _sec.Classify(Exiled());
            var rec = _secret.Current().Single();
            Assert.True(rec.StampRemoved);
            Assert.True(_sec.Declassify(rec).Ok);
            Assert.Empty(_secret.Current());
            Assert.True(_pub.Current().Single().StampCreated, "stamp back");
            Assert.False(SecretMark.Has(_tgt)); Assert.False(SecretMark.Has(_root));
        }
    }
}
