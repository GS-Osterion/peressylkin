using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Sibir.Core;
using Sibir.Native;

namespace Sibir.Tests
{
    internal static class TreeMaker
    {
        /// <summary>Mixed tree: many small files, optional big file, empty dirs, ro+hidden file, Cyrillic name, path over 260 chars.</summary>
        public static void Make(string root, int smallFiles = 2000, long bigBytes = 0)
        {
            var rnd = new Random(42);
            for (int i = 0; i < smallFiles; i++)
            {
                var dir = Path.Combine(root, "d" + (i % 50));
                Directory.CreateDirectory(dir);
                var data = new byte[1024 + rnd.Next(7 * 1024)]; rnd.NextBytes(data);
                File.WriteAllBytes(Path.Combine(dir, $"f{i}.dat"), data);
            }
            Directory.CreateDirectory(Path.Combine(root, @"empty\deeper"));
            var ro = Path.Combine(root, "ro.txt"); File.WriteAllText(ro, "ro");
            File.SetAttributes(ro, FileAttributes.ReadOnly | FileAttributes.Hidden);
            Directory.CreateDirectory(Path.Combine(root, "Сохранения"));
            File.WriteAllText(Path.Combine(root, @"Сохранения\Сохранение 1.sav"), "корабль");
            var deep = root;
            while (deep.Length < 300) deep = Path.Combine(deep, "очень_длинное_имя_папки_для_проверки");
            Directory.CreateDirectory(LongPath.Ex(deep));
            File.WriteAllText(LongPath.Ex(Path.Combine(deep, "deep.txt")), "deep");
            if (bigBytes > 0)
            {
                var buf = new byte[4 * 1024 * 1024]; rnd.NextBytes(buf);
                using (var fs = File.Create(Path.Combine(root, "big.bin")))
                    for (long w = 0; w < bigBytes; w += buf.Length) fs.Write(buf, 0, (int)Math.Min(buf.Length, bigBytes - w));
            }
        }
    }

    public class CopierTests
    {
        [Fact] public void Copier_MixedTree_ManifestsEqual()
        {
            using var t = new TempDir();
            var src = t.Sub("src"); var dst = t.Sub("dst");
            TreeMaker.Make(src, bigBytes: 300L * 1024 * 1024);
            var m = Manifest.Build(src);
            Copier.Copy(src, dst, m, null, CancellationToken.None);
            Assert.Null(Manifest.Build(dst).FirstDifference(m, compareTimes: true));
            Assert.Null(Copier.HashDiff(src, dst, m));
            Assert.Equal(FileAttributes.ReadOnly | FileAttributes.Hidden, File.GetAttributes(Path.Combine(dst, "ro.txt")) & (FileAttributes.ReadOnly | FileAttributes.Hidden));
        }

        [Fact] public void Manifest_DetectsDifference()
        {
            using var t = new TempDir();
            var src = t.Sub("src"); TreeMaker.Make(src, smallFiles: 20);
            var m = Manifest.Build(src);
            File.AppendAllText(Path.Combine(src, @"Сохранения\Сохранение 1.sav"), "!");
            Assert.NotNull(Manifest.Build(src).FirstDifference(m, false));
        }

        [Fact] public void Copier_NestedJunction_CopiedAsLink()
        {
            using var t = new TempDir();
            var outside = t.Sub("outside"); Directory.CreateDirectory(outside);
            File.WriteAllBytes(Path.Combine(outside, "huge.bin"), new byte[50 * 1024 * 1024]);
            var src = t.Sub("src"); Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "a.txt"), "a");
            Junction.Create(Path.Combine(src, "link"), outside);
            var m = Manifest.Build(src);
            Copier.Copy(src, t.Sub("dst"), m, null, CancellationToken.None);
            var dl = t.Sub(@"dst\link");
            Assert.True(Junction.IsReparsePoint(dl));
            Assert.Equal(LongPath.Normalize(outside), Junction.GetTarget(dl));
            Assert.True(Fs.SizeOf(t.Sub("dst")) < 1024 * 1024);
            Assert.Null(Manifest.Build(t.Sub("dst")).FirstDifference(m, true));
        }

        [Fact] public void Copier_Progress_Reports()
        {
            using var t = new TempDir();
            var src = t.Sub("src"); TreeMaker.Make(src, smallFiles: 30);
            var m = Manifest.Build(src);
            var reports = new List<CopyProgress>();
            Copier.Copy(src, t.Sub("dst"), m, new SyncProgress<CopyProgress>(reports.Add), CancellationToken.None);
            var last = reports.Last();
            Assert.Equal(last.BytesTotal, last.BytesDone);
            Assert.Equal(m.TotalBytes, last.BytesTotal);
        }

        [Fact] public void Copier_DestinationMissingDrive_Throws()
        {
            using var t = new TempDir();
            var src = t.Sub("src"); TreeMaker.Make(src, smallFiles: 3);
            var free = "QRSUVWXYZ".First(c => !Directory.Exists(c + @":\"));
            Assert.Throws<IOException>(() => Copier.Copy(src, free + @":\nowhere", Manifest.Build(src), null, CancellationToken.None));
        }
    }

    internal sealed class SyncProgress<T> : IProgress<T>
    {
        private readonly Action<T> _a; public SyncProgress(Action<T> a) { _a = a; }
        public void Report(T v) { lock (this) _a(v); }
    }

    public class LockFinderTests
    {
        [Fact] public void LockFinder_FindsHolder()
        {
            using var t = new TempDir();
            var dir = t.Sub("held"); Directory.CreateDirectory(dir);
            var f = Path.Combine(dir, "save.dat"); File.WriteAllText(f, "x");
            using (new FileStream(f, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.False(LockFinder.CanRename(dir));
                Assert.Contains(Process.GetCurrentProcess().ProcessName + ".exe", LockFinder.WhoHolds(dir).ToList());
            }
            Assert.True(LockFinder.CanRename(dir));
            Assert.True(Directory.Exists(dir));
        }
    }
}
