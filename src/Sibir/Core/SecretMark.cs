using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Sibir.Native;

namespace Sibir.Core
{
    /// <summary>
    /// Invisible mark (empty NTFS stream) on a classified exile target and on its Siberia root,
    /// so a normal process — which cannot read the secret archive — still refuses to exile them.
    /// </summary>
    internal static class SecretMark
    {
        public const string Stream = "Peressylkin.secret";

        private static string StreamPath(string dir) => LongPath.Ex(LongPath.Normalize(dir)) + ":" + Stream;

        public static bool SupportsStreams(string dir)
        {
            var root = new StringBuilder(1024);
            if (!NativeMethods.GetVolumePathName(LongPath.Normalize(dir), root, root.Capacity)) return false;
            return NativeMethods.GetVolumeInformation(root.ToString(), null, 0, out _, out _, out var flags, null, 0)
                && (flags & NativeMethods.FILE_NAMED_STREAMS) != 0;
        }

        public static void Set(string dir)
        {
            using (var h = NativeMethods.CreateFile(StreamPath(dir), NativeMethods.GENERIC_WRITE, NativeMethods.FILE_SHARE_ALL, IntPtr.Zero, NativeMethods.OPEN_ALWAYS, NativeMethods.FILE_ATTRIBUTE_NORMAL, IntPtr.Zero))
                if (h.IsInvalid) throw new IOException(new Win32Exception(Marshal.GetLastWin32Error()).Message + ": " + dir);
        }

        public static bool Has(string dir)
        {
            try { return NativeMethods.GetFileAttributes(StreamPath(dir)) != NativeMethods.INVALID_FILE_ATTRIBUTES; }
            catch (ArgumentException) { return false; }
        }

        public static void Clear(string dir)
        {
            try { NativeMethods.DeleteFile(StreamPath(dir)); } catch (ArgumentException) { }
        }

        /// <summary>The Siberia root holding <paramref name="target"/>: longest known root, else the nearest ancestor with a Siberia plaque, else the parent.</summary>
        public static string RootOf(string target, IEnumerable<string> knownRoots)
        {
            var t = LongPath.Normalize(target);
            var known = (knownRoots ?? Enumerable.Empty<string>()).Where(r => !string.IsNullOrEmpty(r) && !LongPath.Same(r, t) && LongPath.SameOrUnder(t, r))
                .OrderByDescending(r => LongPath.Normalize(r).Length).FirstOrDefault();
            if (known != null) return LongPath.Normalize(known);
            for (var p = Path.GetDirectoryName(t); !string.IsNullOrEmpty(p); p = Path.GetDirectoryName(p))
                if (File.Exists(Path.Combine(p, Plaque.SiberiaFile))) return p;
            return Path.GetDirectoryName(t);
        }
    }
}
