using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Sibir.Native;

namespace Sibir.Core
{
    internal sealed class CopyProgress
    {
        public long BytesDone { get; set; }
        public long BytesTotal { get; set; }
        public int FilesDone { get; set; }
        public int FilesTotal { get; set; }
        public string Current { get; set; }
    }

    internal sealed class TreeEntry
    {
        public string RelPath { get; set; }
        public bool IsDir { get; set; }
        public bool IsLink { get; set; }
        public string LinkTarget { get; set; }
        public long Size { get; set; }
        public FileAttributes Attr { get; set; }
        public DateTime WriteUtc { get; set; }
    }

    /// <summary>Snapshot of a folder tree. Never descends into reparse points.</summary>
    internal sealed class Manifest
    {
        private const FileAttributes Compared = FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System;

        public List<TreeEntry> Entries { get; } = new List<TreeEntry>();
        public long TotalBytes => Entries.Where(e => !e.IsDir && !e.IsLink).Sum(e => e.Size);
        public int FileCount => Entries.Count(e => !e.IsDir && !e.IsLink);

        public static Manifest Build(string root, Func<string, bool> exclude = null)
        {
            var m = new Manifest();
            var rootEx = LongPath.Ex(root).TrimEnd('\\');
            var stack = new Stack<DirectoryInfo>();
            stack.Push(new DirectoryInfo(rootEx));
            while (stack.Count > 0)
            {
                foreach (var i in stack.Pop().GetFileSystemInfos())
                {
                    var rel = i.FullName.Substring(rootEx.Length + 1);
                    if (exclude != null && exclude(rel)) continue;
                    bool isDir = (i.Attributes & FileAttributes.Directory) != 0;
                    bool isLink = (i.Attributes & FileAttributes.ReparsePoint) != 0;
                    m.Entries.Add(new TreeEntry
                    {
                        RelPath = rel, IsDir = isDir, IsLink = isLink,
                        LinkTarget = isLink ? Junction.GetTarget(i.FullName) : null,
                        Size = isDir || isLink ? 0 : ((FileInfo)i).Length,
                        Attr = i.Attributes, WriteUtc = i.LastWriteTimeUtc,
                    });
                    if (isDir && !isLink) stack.Push((DirectoryInfo)i);
                }
            }
            m.Entries.Sort((a, b) => string.Compare(a.RelPath, b.RelPath, StringComparison.OrdinalIgnoreCase));
            return m;
        }

        /// <summary>Null when trees match; otherwise a description of the first mismatch.</summary>
        public string FirstDifference(Manifest other, bool compareTimes)
        {
            if (Entries.Count != other.Entries.Count) return $"entries {Entries.Count} vs {other.Entries.Count}";
            for (int i = 0; i < Entries.Count; i++)
            {
                TreeEntry a = Entries[i], b = other.Entries[i];
                if (!string.Equals(a.RelPath, b.RelPath, StringComparison.OrdinalIgnoreCase)) return $"name {a.RelPath} vs {b.RelPath}";
                if (a.IsDir != b.IsDir || a.IsLink != b.IsLink) return "kind " + a.RelPath;
                if (!string.Equals(a.LinkTarget, b.LinkTarget, StringComparison.OrdinalIgnoreCase)) return "link " + a.RelPath;
                if (a.Size != b.Size) return $"size {a.RelPath}: {a.Size} vs {b.Size}";
                if (!a.IsDir && !a.IsLink && (a.Attr & Compared) != (b.Attr & Compared)) return "attributes " + a.RelPath;
                if (compareTimes && !a.IsDir && !a.IsLink && a.WriteUtc != b.WriteUtc) return "time " + a.RelPath;
            }
            return null;
        }
    }

    internal static class Copier
    {
        private const long BigFile = 256L * 1024 * 1024;

        public static void Copy(string src, string dst, Manifest plan, IProgress<CopyProgress> progress, CancellationToken ct)
        {
            var srcEx = LongPath.Ex(src).TrimEnd('\\');
            var dstEx = LongPath.Ex(dst).TrimEnd('\\');
            Directory.CreateDirectory(dstEx);
            var files = plan.Entries.Where(e => !e.IsDir && !e.IsLink).ToList();
            long total = files.Sum(f => f.Size), done = 0;
            int filesDone = 0;
            void Report(string cur, long extra = 0) => progress?.Report(new CopyProgress
            {
                BytesDone = Interlocked.Read(ref done) + extra, BytesTotal = total,
                FilesDone = Volatile.Read(ref filesDone), FilesTotal = files.Count, Current = cur,
            });

            foreach (var d in plan.Entries.Where(e => e.IsDir && !e.IsLink))
                Directory.CreateDirectory(dstEx + "\\" + d.RelPath);

            foreach (var l in plan.Entries.Where(e => e.IsLink))
            {
                var at = dstEx + "\\" + l.RelPath;
                if (!l.IsDir) Junction.CreateFileSymlink(at, l.LinkTarget);
                else if (TagIsMountPoint(srcEx + "\\" + l.RelPath)) Junction.Create(at, l.LinkTarget);
                else Junction.CreateDirSymlink(at, l.LinkTarget);
            }

            void CopyOne(TreeEntry f, bool big)
            {
                ct.ThrowIfCancellationRequested();
                int cancel = 0;
                NativeMethods.CopyProgressRoutine routine = null;
                if (big)
                    routine = (t, transferred, a, b, c, r, s, d, data) =>
                    {
                        Report(f.RelPath, transferred);
                        return ct.IsCancellationRequested ? 1u : 0u;   // PROGRESS_CANCEL
                    };
                uint flags = NativeMethods.COPY_FILE_FAIL_IF_EXISTS | (big ? NativeMethods.COPY_FILE_NO_BUFFERING : 0);
                if (!NativeMethods.CopyFileEx(srcEx + "\\" + f.RelPath, dstEx + "\\" + f.RelPath, routine, IntPtr.Zero, ref cancel, flags))
                {
                    ct.ThrowIfCancellationRequested();
                    var err = new Win32Exception();
                    throw new IOException($"{f.RelPath}: {err.Message}", err);
                }
                GC.KeepAlive(routine);
                Interlocked.Add(ref done, f.Size);
                Interlocked.Increment(ref filesDone);
                Report(f.RelPath);
            }

            foreach (var f in files.Where(f => f.Size > BigFile)) CopyOne(f, true);
            try
            {
                Parallel.ForEach(files.Where(f => f.Size <= BigFile), new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, f => CopyOne(f, false));
            }
            catch (AggregateException ae) { throw ae.InnerExceptions.First(); }

            // Directory attributes and times, deepest first (creating children changes parent times).
            foreach (var d in plan.Entries.Where(e => e.IsDir && !e.IsLink).OrderByDescending(e => e.RelPath.Length))
            {
                var p = dstEx + "\\" + d.RelPath;
                Directory.SetLastWriteTimeUtc(p, d.WriteUtc);
                File.SetAttributes(p, d.Attr & ~FileAttributes.ReparsePoint);
            }
            Report(null);
        }

        private static bool TagIsMountPoint(string path)
        {
            // Junction.GetTarget handles both; distinguish via attribute trick: directory symlinks resolve relative targets.
            try { return new DirectoryInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint) && ReadTag(path) == NativeMethods.IO_REPARSE_TAG_MOUNT_POINT; }
            catch { return true; }
        }

        private static uint ReadTag(string path)
        {
            var buf = new byte[16 * 1024 + 64];
            using (var h = NativeMethods.CreateFile(LongPath.Ex(path), 0, NativeMethods.FILE_SHARE_ALL, IntPtr.Zero, NativeMethods.OPEN_EXISTING,
                NativeMethods.FILE_FLAG_BACKUP_SEMANTICS | NativeMethods.FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero))
            {
                if (h.IsInvalid || !NativeMethods.DeviceIoControl(h, NativeMethods.FSCTL_GET_REPARSE_POINT, null, 0, buf, buf.Length, out _, IntPtr.Zero)) return 0;
            }
            return BitConverter.ToUInt32(buf, 0);
        }

        /// <summary>SHA-256 comparison of every file in the manifest; null when all equal, else the first differing path.</summary>
        public static string HashDiff(string a, string b, Manifest m)
        {
            string aEx = LongPath.Ex(a).TrimEnd('\\'), bEx = LongPath.Ex(b).TrimEnd('\\');
            foreach (var f in m.Entries.Where(e => !e.IsDir && !e.IsLink))
                if (!Hash(aEx + "\\" + f.RelPath).SequenceEqual(Hash(bEx + "\\" + f.RelPath))) return f.RelPath;
            return null;
        }

        private static byte[] Hash(string path)
        {
            using (var sha = SHA256.Create())
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20))
                return sha.ComputeHash(fs);
        }
    }
}
