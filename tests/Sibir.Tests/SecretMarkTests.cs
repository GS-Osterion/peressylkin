using System;
using System.IO;
using System.Linq;
using Sibir.Core;

namespace Sibir.Tests
{
    public class SecretMarkTests : IDisposable
    {
        private readonly TempDir _t = new TempDir();
        public void Dispose() => _t.Dispose();

        [Fact] public void SetHasClear_ContentUntouched()
        {
            var d = _t.Sub("tgt"); Directory.CreateDirectory(d); File.WriteAllText(Path.Combine(d, "a.txt"), "x");
            var before = Directory.GetFileSystemEntries(d).OrderBy(x => x).ToArray();
            Assert.False(SecretMark.Has(d));
            SecretMark.Set(d);
            Assert.True(SecretMark.Has(d));
            Assert.Equal(string.Join("|", before), string.Join("|", Directory.GetFileSystemEntries(d).OrderBy(x => x)));
            SecretMark.Clear(d);
            Assert.False(SecretMark.Has(d));
            SecretMark.Clear(d);   // absent: no-op
        }

        [Fact] public void Has_MissingDir_False() => Assert.False(SecretMark.Has(_t.Sub("nope")));

        [Fact] public void SupportsStreams_TempIsNtfs() => Assert.True(SecretMark.SupportsStreams(_t.Path));

        [Fact] public void RootOf_PrefersKnownRoot() =>
            Assert.Equal(@"E:\Data\Сибирь", SecretMark.RootOf(@"E:\Data\Сибирь\C\X", new[] { @"E:\Data", @"E:\Data\Сибирь" }));

        [Fact] public void RootOf_Plaque()
        {
            var root = _t.Sub("S"); var tgt = Path.Combine(root, @"C\Games\X");
            Directory.CreateDirectory(tgt); Plaque.EnsureSiberia(root);
            Assert.Equal(root, SecretMark.RootOf(tgt, null));
        }
    }
}
