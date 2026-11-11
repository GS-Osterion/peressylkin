using System;
using System.IO;

namespace Sibir.Tests
{
    /// <summary>Unique scratch folder under %TEMP%\sibir-tests; junctions inside are removed as links.</summary>
    public sealed class TempDir : IDisposable
    {
        public string Path { get; }

        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sibir-tests", Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(Path);
        }

        public string Sub(string rel) => System.IO.Path.Combine(Path, rel);

        public void Dispose()
        {
            try { DeleteTree(@"\\?\" + Path); } catch { /* best effort */ }
        }

        public static void DeleteTree(string dir)
        {
            var info = new DirectoryInfo(dir);
            if (!info.Exists) return;
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0) { info.Attributes = FileAttributes.Directory; Directory.Delete(dir, false); return; }
            foreach (var f in info.GetFiles("*", SearchOption.TopDirectoryOnly)) { f.Attributes = FileAttributes.Normal; f.Delete(); }
            foreach (var d in info.GetDirectories()) DeleteTree(d.FullName);
            info.Attributes = FileAttributes.Directory;
            Directory.Delete(dir, false);
        }
    }
}
