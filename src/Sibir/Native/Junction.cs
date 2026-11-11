using System;
using System.ComponentModel;
using System.IO;
using System.Text;
using static Sibir.Native.NativeMethods;

namespace Sibir.Native
{
    /// <summary>NTFS junctions (mount-point reparse points) and file symlinks.</summary>
    internal static class Junction
    {
        public static void Create(string link, string target)
        {
            target = LongPath.Normalize(target);
            Directory.CreateDirectory(LongPath.Ex(link));
            var sub = Encoding.Unicode.GetBytes(@"\??\" + target);
            var print = Encoding.Unicode.GetBytes(target);
            int pathBytes = sub.Length + 2 + print.Length + 2;
            var buf = new byte[8 + 8 + pathBytes];
            BitConverter.GetBytes(IO_REPARSE_TAG_MOUNT_POINT).CopyTo(buf, 0);
            BitConverter.GetBytes((ushort)(8 + pathBytes)).CopyTo(buf, 4);
            BitConverter.GetBytes((ushort)0).CopyTo(buf, 8);                       // SubstituteNameOffset
            BitConverter.GetBytes((ushort)sub.Length).CopyTo(buf, 10);
            BitConverter.GetBytes((ushort)(sub.Length + 2)).CopyTo(buf, 12);        // PrintNameOffset
            BitConverter.GetBytes((ushort)print.Length).CopyTo(buf, 14);
            sub.CopyTo(buf, 16);
            print.CopyTo(buf, 16 + sub.Length + 2);
            using (var h = Open(link, GENERIC_WRITE))
            {
                if (!DeviceIoControl(h, FSCTL_SET_REPARSE_POINT, buf, buf.Length, null, 0, out _, IntPtr.Zero))
                {
                    var err = new Win32Exception();
                    try { Directory.Delete(LongPath.Ex(link)); } catch { }
                    throw new IOException("Junction failed: " + err.Message, err);
                }
            }
        }

        public static bool IsReparsePoint(string path)
        {
            try { return (File.GetAttributes(LongPath.Ex(path)) & FileAttributes.ReparsePoint) != 0; }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
        }

        /// <summary>Target of a junction or symlink, normalized; null if not a reparse point we understand.</summary>
        public static string GetTarget(string link)
        {
            if (!IsReparsePoint(link)) return null;
            var buf = new byte[16 * 1024 + 64];
            using (var h = Open(link, 0))
            {
                if (!DeviceIoControl(h, FSCTL_GET_REPARSE_POINT, null, 0, buf, buf.Length, out _, IntPtr.Zero))
                    throw new IOException("Read reparse failed", new Win32Exception());
            }
            uint tag = BitConverter.ToUInt32(buf, 0);
            int baseOff = tag == IO_REPARSE_TAG_MOUNT_POINT ? 16 : tag == IO_REPARSE_TAG_SYMLINK ? 20 : -1;
            if (baseOff < 0) return null;
            int off = BitConverter.ToUInt16(buf, 8), len = BitConverter.ToUInt16(buf, 10);
            var s = Encoding.Unicode.GetString(buf, baseOff + off, len);
            if (s.StartsWith(@"\??\")) s = s.Substring(4);
            if (tag == IO_REPARSE_TAG_SYMLINK && (BitConverter.ToUInt32(buf, 16) & 1) != 0)   // relative symlink
                return s;
            return LongPath.Normalize(s);
        }

        /// <summary>Removes the link only; the target is untouched.</summary>
        public static void Delete(string link)
        {
            var ex = LongPath.Ex(link);
            var attr = File.GetAttributes(ex);
            if ((attr & FileAttributes.ReparsePoint) == 0) throw new IOException("Not a link: " + link);
            File.SetAttributes(ex, attr & ~(FileAttributes.ReadOnly | FileAttributes.System | FileAttributes.Hidden));
            if ((attr & FileAttributes.Directory) != 0) Directory.Delete(ex, false); else File.Delete(ex);
        }

        public static void CreateFileSymlink(string link, string target)
        {
            // 0x2 = SYMBOLIC_LINK_FLAG_ALLOW_UNPRIVILEGED_CREATE (works in Developer Mode)
            if (!CreateSymbolicLink(LongPath.Ex(link), target, 0x2))
            {
                var err = new Win32Exception();
                if (err.NativeErrorCode == 1314) throw new UnauthorizedAccessException("needs_admin", err);
                throw new IOException("Symlink failed: " + err.Message, err);
            }
        }

        public static void CreateDirSymlink(string link, string target)
        {
            if (!CreateSymbolicLink(LongPath.Ex(link), target, 0x1 | 0x2))
            {
                var err = new Win32Exception();
                if (err.NativeErrorCode == 1314) throw new UnauthorizedAccessException("needs_admin", err);
                throw new IOException("Symlink failed: " + err.Message, err);
            }
        }

        private static Microsoft.Win32.SafeHandles.SafeFileHandle Open(string path, uint access)
        {
            var h = CreateFile(LongPath.Ex(path), access, FILE_SHARE_ALL, IntPtr.Zero, OPEN_EXISTING,
                FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero);
            if (h.IsInvalid) throw new IOException("Open failed: " + path, new Win32Exception());
            return h;
        }
    }
}
