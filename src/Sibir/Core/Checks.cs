using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sibir.Native;

namespace Sibir.Core
{
    internal enum Verdict { Ok, Warn, Refused }

    internal sealed class CheckResult
    {
        public string Path { get; set; }
        public Verdict Verdict { get; set; }
        public string Reason { get; set; }
        public string Message { get; set; }
        public string Target { get; set; }
        public long Bytes { get; set; }
        public LauncherHit Launcher { get; set; }
        public string[] Processes { get; set; } = new string[0];
    }

    internal sealed class CheckOptions
    {
        public bool ByName { get; set; }
        public bool IgnoreLauncher { get; set; }
        public Settings Settings { get; set; }
        /// <summary>Folder holding the case archive; it and its ancestors are never exiled.</summary>
        public string ArchiveDir { get; set; } = Plaque.DefaultArchiveDir;
    }

    /// <summary>Step 0: read-only pre-flight checks (spec §5).</summary>
    internal static class Checks
    {
        private static string Env(Environment.SpecialFolder f) => Environment.GetFolderPath(f);

        /// <summary>Folders that may never be exiled themselves (exact match).</summary>
        public static IReadOnlyList<string> ProtectedRoots() => new[]
        {
            Env(Environment.SpecialFolder.ProgramFiles), Env(Environment.SpecialFolder.ProgramFilesX86),
            Env(Environment.SpecialFolder.CommonApplicationData), Env(Environment.SpecialFolder.UserProfile),
            System.IO.Path.GetDirectoryName(Env(Environment.SpecialFolder.UserProfile)),
        }.Where(p => !string.IsNullOrEmpty(p)).ToList();

        public static bool IsProtected(string path)
        {
            var n = LongPath.Normalize(path);
            if (n.Length <= 3) return true;                                             // drive root
            if (LongPath.SameOrUnder(n, Env(Environment.SpecialFolder.Windows))) return true;
            var pf = Env(Environment.SpecialFolder.ProgramFiles);
            if (LongPath.SameOrUnder(n, System.IO.Path.Combine(pf, "WindowsApps"))) return true;
            if (LongPath.SameOrUnder(n, System.IO.Path.GetPathRoot(n) + "XboxGames")) return true;
            return ProtectedRoots().Any(p => LongPath.Same(n, p));
        }

        /// <summary>The case archive, anything inside it, any folder containing it, or any folder with a plaque.</summary>
        public static bool IsProperty(string path, string archiveDir) =>
            LongPath.SameOrUnder(path, archiveDir) || LongPath.SameOrUnder(archiveDir, path) || Plaque.IsMarked(path);

        private static bool UnderReparsePoint(string path)
        {
            for (var p = LongPath.Normalize(path); !string.IsNullOrEmpty(p); p = System.IO.Path.GetDirectoryName(p))
                if (Junction.IsReparsePoint(p)) return true;
            return false;
        }

        private static bool InSecretPlace(string path)
        {
            for (var p = LongPath.Normalize(path); !string.IsNullOrEmpty(p); p = System.IO.Path.GetDirectoryName(p))
                if (SecretMark.Has(p)) return true;
            return false;
        }

        public static List<CheckResult> Run(IReadOnlyList<string> sources, string root, CheckOptions opt, IDriveEnv env, Archive archive, Func<string, LauncherHit> detect)
        {
            var settings = opt.Settings ?? new Settings();
            var normalized = sources.Select(LongPath.ResolveSubst).ToList();   // a subst alias must not slip past path-based protection
            var exiled = archive.Current().Where(r => r.Status == "exiled").ToList();
            var rootN = LongPath.ResolveSubst(root);
            var rootVol = env.VolumeIdOf(rootN);
            var rootDrive = Destination.DriveOf(rootN, env);
            long planned = 0;
            var results = new List<CheckResult>();

            foreach (var src in normalized)
            {
                var r = new CheckResult { Path = src, Verdict = Verdict.Ok, Reason = "ok", Message = Strings.Ok, Target = Destination.MapTarget(src, rootN, opt.ByName) };
                results.Add(r);
                void Refuse(string reason, string message) { r.Verdict = Verdict.Refused; r.Reason = reason; r.Message = message; }

                if (env.IsNetworkPath(src)) { Refuse("network_source", Strings.Abroad); continue; }   // abroad: never touch it
                if (!Directory.Exists(LongPath.Ex(src)) && !Junction.IsReparsePoint(src)) { Refuse("not_found", "Гражданин не найден по указанному адресу"); continue; }
                if (normalized.Any(o => !LongPath.Same(o, src) && LongPath.SameOrUnder(src, o))) { Refuse("nested", Strings.Nested); continue; }
                if (IsProtected(src)) { Refuse("protected", Strings.Protected); continue; }
                if (IsProperty(src, opt.ArchiveDir)) { Refuse("peressylkin_property", Strings.Property); continue; }
                if (UnderReparsePoint(src)) { Refuse("already_exiled", Strings.AlreadyExiled); continue; }
                if (exiled.Any(e => LongPath.Same(e.Source, src))) { Refuse("case_open", Strings.CaseOpen); continue; }
                if (exiled.Any(e => LongPath.SameOrUnder(src, e.Target)) || LongPath.SameOrUnder(src, rootN)) { Refuse("in_siberia", Strings.InSiberia); continue; }
                if (InSecretPlace(src)) { Refuse("in_siberia", Strings.InSiberia); continue; }                   // classified case: invisible to this process's archive
                if (exiled.Any(e => LongPath.SameOrUnder(e.Target, src))) { Refuse("peressylkin_property", Strings.Property); continue; }   // a Siberia (even 1.0.0, no plaque) holding exiled folders
                if (settings.SiberiaRoots.Concat(new[] { rootN }).Any(sr => LongPath.SameOrUnder(sr, src) && SecretMark.Has(sr))) { Refuse("peressylkin_property", Strings.Property); continue; }   // a Siberia holding classified folders
                if (LongPath.SameOrUnder(rootN, src)) { Refuse("target_is_source", Strings.TargetIsSource); continue; }
                if (env.IsNetworkPath(rootN)) { Refuse("network_target", Strings.Abroad); continue; }
                if (string.Equals(env.VolumeIdOf(src), rootVol, StringComparison.OrdinalIgnoreCase)) { Refuse("same_drive", Strings.SameDrive); continue; }
                if (Destination.IsForbidden(rootN, settings, env, out var fr, out var fm)) { Refuse(fr, fm); continue; }

                var hit = detect(src);
                r.Launcher = hit;
                if (hit != null && hit.Kind == LauncherKind.StoreApp) { Refuse("store_app", Strings.StoreApp); continue; }
                if (hit != null && hit.Kind == LauncherKind.Managed && !opt.IgnoreLauncher) { Refuse("launcher", string.Format(Strings.Launcher, hit.Launcher) + ". " + hit.Hint); continue; }

                if (!Fs.IsEmptyDir(r.Target)) { Refuse("target_exists", Strings.TargetExists); continue; }

                r.Bytes = env.SizeOf(src);
                long free = rootDrive?.FreeBytes ?? 0;
                long need = planned + r.Bytes + r.Bytes / 20;
                if (rootDrive == null || free < need) { Refuse("no_space", string.Format(Strings.NoSpace, Strings.Size(r.Bytes + r.Bytes / 20), Strings.Size(Math.Max(0, free - planned)))); continue; }
                planned += r.Bytes;

                if (hit != null && hit.Kind == LauncherKind.Registered) { r.Verdict = Verdict.Warn; r.Reason = "registered_app"; r.Message = Strings.RegisteredApp; }
                else if (rootDrive.IsRemovable) { r.Verdict = Verdict.Warn; r.Reason = "removable_target"; r.Message = Strings.Removable; }
            }
            return results;
        }
    }
}
