using System;
using System.IO;
using Sibir.Core;

namespace Sibir.Tests
{
    public class PlaqueTests : IDisposable
    {
        private readonly TempDir _t = new TempDir();
        public void Dispose() => _t.Dispose();

        [Fact] public void Ensure_CreatesOnce_KeepsUserEdits()
        {
            var root = _t.Sub("Сибирь");
            Plaque.EnsureSiberia(root);
            var file = Path.Combine(root, Plaque.SiberiaFile);
            Assert.True(File.Exists(file), "plaque created");
            Assert.Contains("Перессылкин", File.ReadAllText(file));
            File.WriteAllText(file, "edited");
            Plaque.EnsureSiberia(root);
            Assert.Equal("edited", File.ReadAllText(file));
        }

        [Fact] public void Ensure_RecreatesAfterDelete()
        {
            var dir = _t.Sub("Sibir");
            Plaque.EnsureArchive(dir);
            var file = Path.Combine(dir, Plaque.ArchiveFile);
            File.Delete(file);
            Plaque.EnsureArchive(dir);
            Assert.True(File.Exists(file), "plaque recreated");
        }

        [Fact] public void Ensure_UnwritableDir_DoesNotThrow()
        {
            var blocker = _t.Sub("file");
            File.WriteAllText(blocker, "not a dir");
            Plaque.EnsureSiberia(Path.Combine(blocker, "Сибирь"));
            Plaque.EnsureArchive(Path.Combine(blocker, "Sibir"));
        }

        [Fact] public void IsMarked_OnlyDirectChild()
        {
            var top = _t.Sub("top");
            Plaque.EnsureArchive(Path.Combine(top, "inner"));
            Assert.False(Plaque.IsMarked(top), "subfolder plaque does not mark parent");
            Assert.True(Plaque.IsMarked(Path.Combine(top, "inner")), "direct plaque marks");
        }
    }
}
