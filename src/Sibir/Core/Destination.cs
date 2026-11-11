using System;
using System.IO;
using System.Linq;
using Sibir.Native;

namespace Sibir.Core
{
    internal static class Destination
    {
        public const string Folder = "Сибирь";

        public static DriveFacts DriveOf(string path, IDriveEnv env)
        {
            var vol = env.VolumeIdOf(path);
            return env.Drives().FirstOrDefault(d => string.Equals(d.VolumeId, vol, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>capital / regime_city for forbidden roots; message is ready for display.</summary>
        public static bool IsForbidden(string root, Settings s, IDriveEnv env, out string reason, out string message)
        {
            reason = message = null;
            var vol = env.VolumeIdOf(root);
            if (s.ProtectCapital && string.Equals(vol, env.VolumeIdOf(env.SystemDrive), StringComparison.OrdinalIgnoreCase))
            {
                reason = "capital";
                message = string.Format(Strings.Capital, env.SystemDrive.Substring(0, 1).ToUpperInvariant());
                return true;
            }
            var drive = DriveOf(root, env);
            var letter = drive?.Letter ?? Path.GetPathRoot(LongPath.Normalize(root)).Substring(0, 2).ToUpperInvariant();
            if (s.RegimeDrives.Any(r => string.Equals(r.TrimEnd('\\'), letter, StringComparison.OrdinalIgnoreCase)))
            {
                reason = "regime_city";
                message = string.Format(Strings.RegimeCity, letter);
                return true;
            }
            return false;
        }

        public static string DefaultRoot(string source, Settings s, IDriveEnv env)
        {
            var srcVol = env.VolumeIdOf(source);
            bool Usable(string root) =>
                !string.Equals(env.VolumeIdOf(root), srcVol, StringComparison.OrdinalIgnoreCase)
                && !IsForbidden(root, s, env, out _, out _);

            if (!string.IsNullOrEmpty(s.LastRoot) && DriveOf(s.LastRoot, env) != null && Usable(s.LastRoot))
                return s.LastRoot;

            var best = env.Drives()
                .Where(d => d.IsFixed && !d.IsRemovable && !d.IsNetwork && Usable(d.Root))
                .OrderByDescending(d => d.FreeBytes)
                .FirstOrDefault();
            return best == null ? null : Path.Combine(best.Root, Folder);
        }

        public static string MapTarget(string source, string root, bool byName)
        {
            var src = LongPath.Normalize(source);
            if (byName) return Path.Combine(LongPath.Normalize(root), Path.GetFileName(src));
            var rest = src.StartsWith(@"\\")
                ? "UNC" + src.Substring(1)
                : src.Substring(0, 1).ToUpperInvariant() + src.Substring(2);
            return LongPath.Normalize(root).TrimEnd('\\') + @"\" + rest.TrimStart('\\');
        }
    }
}
