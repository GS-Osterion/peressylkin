using System;
using System.IO;

namespace Sibir.Native
{
    internal static class LongPath
    {
        private static string StripPrefix(string p)
        {
            if (p.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return @"\\" + p.Substring(8);
            if (p.StartsWith(@"\\?\")) return p.Substring(4);
            return p;
        }

        /// <summary>Full path, no trailing backslash except for a drive root; case preserved.</summary>
        public static string Normalize(string path)
        {
            var full = Path.GetFullPath(StripPrefix(path));
            if (full.Length > 3 && full.EndsWith(@"\")) full = full.TrimEnd('\\');
            if (full.Length == 2 && full[1] == ':') full += @"\";
            return full;
        }

        /// <summary>Extended-length form for Win32 calls (\\?\ or \\?\UNC\).</summary>
        public static string Ex(string path)
        {
            if (path.StartsWith(@"\\?\")) return path;
            var n = Normalize(path);
            return n.StartsWith(@"\\") ? @"\\?\UNC\" + n.Substring(2) : @"\\?\" + n;
        }

        public static bool SameOrUnder(string child, string parent)
        {
            var c = Normalize(child).TrimEnd('\\');
            var p = Normalize(parent).TrimEnd('\\');
            return c.Equals(p, StringComparison.OrdinalIgnoreCase)
                || c.StartsWith(p + @"\", StringComparison.OrdinalIgnoreCase);
        }

        public static bool Same(string a, string b) =>
            Normalize(a).TrimEnd('\\').Equals(Normalize(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
    }
}
