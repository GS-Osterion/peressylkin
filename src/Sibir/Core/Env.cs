using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Sibir.Native;

namespace Sibir.Core
{
    internal sealed class DriveFacts
    {
        public string Root { get; set; }        // "T:\"
        public string VolumeId { get; set; }
        public bool IsFixed { get; set; }
        public bool IsRemovable { get; set; }
        public bool IsNetwork { get; set; }
        public long FreeBytes { get; set; }
        public string Letter => Root.Substring(0, 2).ToUpperInvariant();   // "T:"
    }

    internal interface IDriveEnv
    {
        IReadOnlyList<DriveFacts> Drives();
        string VolumeIdOf(string path);
        bool IsNetworkPath(string path);
        long SizeOf(string folder);
        string SystemDrive { get; }   // "C:\"
    }

    internal enum LauncherKind { Managed, StoreApp, Registered }

    internal sealed class LauncherHit
    {
        public string Launcher { get; set; }
        public LauncherKind Kind { get; set; }
        public string Hint { get; set; }
    }

    internal static class Fs
    {
        /// <summary>Total bytes of files under folder; does not descend into reparse points.</summary>
        public static long SizeOf(string folder)
        {
            long sum = 0;
            var stack = new Stack<DirectoryInfo>();
            stack.Push(new DirectoryInfo(LongPath.Ex(folder)));
            while (stack.Count > 0)
            {
                var d = stack.Pop();
                FileSystemInfo[] items;
                try { items = d.GetFileSystemInfos(); } catch (UnauthorizedAccessException) { continue; }
                foreach (var i in items)
                {
                    if ((i.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if (i is DirectoryInfo sub) stack.Push(sub); else sum += ((FileInfo)i).Length;
                }
            }
            return sum;
        }

        /// <summary>Deletes a tree; reparse points inside (and at the top) are removed as links, never followed.</summary>
        public static void DeleteTree(string dir)
        {
            var ex = LongPath.Ex(dir);
            var info = new DirectoryInfo(ex);
            if (!info.Exists) return;
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0) { Junction.Delete(dir); return; }
            foreach (var i in info.GetFileSystemInfos())
            {
                if (i is DirectoryInfo d) DeleteTree(d.FullName);
                else { i.Attributes = FileAttributes.Normal; i.Delete(); }
            }
            info.Attributes = FileAttributes.Directory;
            Directory.Delete(ex, false);
        }

        public static bool DirExists(string dir) => Directory.Exists(LongPath.Ex(dir));

        public static bool IsEmptyDir(string dir) =>
            !Directory.Exists(LongPath.Ex(dir)) || !Directory.EnumerateFileSystemEntries(LongPath.Ex(dir)).Any();
    }

    internal sealed class RealDriveEnv : IDriveEnv
    {
        public string SystemDrive => (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:") + @"\";

        public IReadOnlyList<DriveFacts> Drives()
        {
            var list = new List<DriveFacts>();
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (!d.IsReady) continue;
                    list.Add(new DriveFacts
                    {
                        Root = d.RootDirectory.FullName,
                        VolumeId = VolumeIdOf(d.RootDirectory.FullName),
                        IsFixed = d.DriveType == DriveType.Fixed,
                        IsRemovable = d.DriveType == DriveType.Removable || d.DriveType == DriveType.CDRom,
                        IsNetwork = d.DriveType == DriveType.Network,
                        FreeBytes = d.AvailableFreeSpace,
                    });
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return list;
        }

        public string VolumeIdOf(string path)
        {
            var p = LongPath.Normalize(path);
            for (int guard = 0; guard < 8 && p.Length >= 2 && p[1] == ':'; guard++)
            {
                var sb = new StringBuilder(1024);
                if (NativeMethods.QueryDosDevice(p.Substring(0, 2), sb, sb.Capacity) == 0) break;
                var dev = sb.ToString();
                if (!dev.StartsWith(@"\??\")) break;            // real volume, not subst
                p = LongPath.Normalize(dev.Substring(4).TrimEnd('\\') + p.Substring(2));
            }
            var mount = new StringBuilder(1024);
            if (!NativeMethods.GetVolumePathName(p, mount, mount.Capacity)) return Path.GetPathRoot(p).ToUpperInvariant();
            var vol = new StringBuilder(1024);
            return NativeMethods.GetVolumeNameForVolumeMountPoint(mount.ToString(), vol, vol.Capacity)
                ? vol.ToString().ToUpperInvariant()
                : mount.ToString().ToUpperInvariant();
        }

        public bool IsNetworkPath(string path)
        {
            var n = LongPath.Normalize(path);
            if (n.StartsWith(@"\\")) return true;
            try { return new DriveInfo(n.Substring(0, 1)).DriveType == DriveType.Network; } catch { return false; }
        }

        public long SizeOf(string folder) => Fs.SizeOf(folder);
    }
}
