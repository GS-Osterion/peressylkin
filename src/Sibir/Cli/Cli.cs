using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Sibir.Core;
using Sibir.Native;

namespace Sibir.Cli
{
    internal static class ExitCodes
    {
        public const int Ok = 0, BadArgs = 1, Refused = 2, Locked = 3, Failed = 4, NeedsAdmin = 5, Unfinished = 6;
    }

    internal sealed class CliContext
    {
        public Archive Archive { get; set; }
        /// <summary>Folder of the case archive file: protected and marked with the archive plaque.</summary>
        public string ArchiveDir => Path.GetDirectoryName(Archive.FilePath);
        public IDriveEnv Drives { get; set; }
        public ILauncherEnv Launchers { get; set; }
        public string SettingsPath { get; set; }
        public string StampIconPath { get; set; }
        public IFaultHook Hook { get; set; }

        public static CliContext Real()
        {
            var icon = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sibir", "stamp.ico");
            return new CliContext
            {
                Archive = new Archive(Archive.DefaultPath), Drives = new RealDriveEnv(), Launchers = new RealLauncherEnv(),
                SettingsPath = Settings.DefaultPath, StampIconPath = File.Exists(icon) ? icon : null,
            };
        }
    }

    /// <summary>Non-interactive command mode for agents and scripts (spec §6).</summary>
    internal static class Cli
    {
        private static readonly string[] Commands = { "check", "exile", "rehab", "status", "list", "recover", "schema", "selftest", "install", "uninstall", "help", "--help" };
        private static readonly HashSet<string> Flags = new HashSet<string> { "--json", "--yes", "--strict", "--ignore-launcher", "--by-name", "--no-stamp", "--allow-capital" };

        public static readonly string[] Reasons =
        {
            "ok", "already_exiled", "in_siberia", "peressylkin_property", "case_open", "target_is_source", "capital", "regime_city", "protected", "same_drive",
            "network_target", "no_space", "removable_target", "launcher", "store_app", "registered_app", "nested", "target_exists",
            "locked", "needs_admin", "copy_failed", "verify_failed", "not_exiled", "no_destination", "bad_args", "not_found", "cancelled",
        };

        public static bool IsCliCommand(string[] args) => args.Length > 0 && Commands.Contains(args[0].ToLowerInvariant());

        private sealed class Args
        {
            public string Command; public List<string> Paths = new List<string>(); public HashSet<string> Flags = new HashSet<string>(); public string To;
            public bool Has(string f) => Flags.Contains(f);
        }

        private static Args Parse(string[] argv)
        {
            var a = new Args { Command = argv[0].ToLowerInvariant() };
            for (int i = 1; i < argv.Length; i++)
            {
                var s = argv[i];
                if (s == "--to") { if (++i >= argv.Length) return null; a.To = argv[i]; }
                else if (s.StartsWith("--")) { if (!Flags.Contains(s)) return null; a.Flags.Add(s); }
                else a.Paths.Add(s);
            }
            return a;
        }

        public static int Run(string[] argv, TextWriter stdout, TextWriter stderr, CliContext ctx)
        {
            var a = argv.Length == 0 ? null : Parse(argv);
            bool json = argv.Contains("--json");
            var w = new Out(stdout, json);
            if (a == null || ((a.Command == "check" || a.Command == "exile" || a.Command == "rehab" || a.Command == "status") && a.Paths.Count == 0))
            {
                w.Row(new Dictionary<string, object> { ["verdict"] = "refused", ["reason"] = "bad_args", ["message"] = "Неверные аргументы. Peressylkin.exe help" });
                return ExitCodes.BadArgs;
            }
            try
            {
                switch (a.Command)
                {
                    case "schema": stdout.WriteLine(Ascii(new JavaScriptSerializer().Serialize(Schema()))); return 0;
                    case "help": case "--help": stdout.WriteLine(Help); return 0;
                    case "status": return Status(a, ctx, w);
                    case "list": return List(ctx, w);
                    case "recover": return Recover(a, ctx, w);
                    case "check": case "exile": return Exile(a, ctx, w, stderr, a.Command == "exile" && a.Has("--yes"));
                    case "rehab": return Rehab(a, ctx, w, stderr);
                    case "selftest": return SelfTest.Run(a.To, a.Has("--json"), stdout);
                    case "install":
                        using (var k = Shell.ShellIntegration.UserClasses())
                        {
                            var exe = Shell.ShellIntegration.InstallFiles(System.Reflection.Assembly.GetEntryAssembly().Location);
                            Shell.ShellIntegration.Install(k, exe);
                            Plaque.EnsureArchive(Shell.ShellIntegration.InstallDir);
                            w.Result(exe, "done", "ok", "Встал на учёт");
                        }
                        return 0;
                    case "uninstall":
                        using (var k = Shell.ShellIntegration.UserClasses()) Shell.ShellIntegration.Uninstall(k);
                        w.Result(Shell.ShellIntegration.InstalledExe, "done", "ok", "Снят с учёта (архив сохранён)");
                        return 0;
                }
            }
            catch (Exception e)
            {
                w.Row(new Dictionary<string, object> { ["verdict"] = "failed", ["reason"] = "copy_failed", ["message"] = e.Message });
                return ExitCodes.Failed;
            }
            return ExitCodes.BadArgs;
        }

        // ---------- commands ----------

        private static int Exile(Args a, CliContext ctx, Out w, TextWriter err, bool doIt)
        {
            if (doIt && ctx.Archive.Unfinished().Count > 0) return UnfinishedRow(ctx, w);
            var settings = Settings.Load(ctx.SettingsPath);
            if (a.Has("--allow-capital")) settings.ProtectCapital = false;
            var root = a.To ?? Destination.DefaultRoot(a.Paths[0], settings, ctx.Drives);
            if (root == null)
            {
                foreach (var p in a.Paths) w.Result(p, "refused", "no_destination", Strings.NoDestination);
                return ExitCodes.Refused;
            }
            var opt = new CheckOptions { ByName = a.Has("--by-name"), IgnoreLauncher = a.Has("--ignore-launcher"), Settings = settings, ArchiveDir = ctx.ArchiveDir };
            var checks = Checks.Run(a.Paths, root, opt, ctx.Drives, ctx.Archive, p => LauncherDetect.Detect(p, ctx.Launchers));
            if (doIt && checks.Any(c => c.Verdict != Verdict.Refused)) { Plaque.EnsureArchive(ctx.ArchiveDir); Plaque.EnsureSiberia(root); }
            int code = 0;
            var mover = NewMover(a, ctx);
            foreach (var c in checks)
            {
                if (c.Verdict == Verdict.Refused || !doIt)
                {
                    w.Result(c.Path, c.Verdict.ToString().ToLowerInvariant(), c.Reason, c.Message, c.Target, c.Bytes);
                    if (c.Verdict == Verdict.Refused) code = Math.Max(code, ExitCodes.Refused);
                    continue;
                }
                var r = mover.Exile(c.Path, c.Target, Progress(w, err, c.Path), CancellationToken.None);
                code = Math.Max(code, Report(w, r));
                if (r.Ok) { settings.LastRoot = LongPath.Normalize(root); Settings.Load(ctx.SettingsPath).Also(s => { s.LastRoot = settings.LastRoot; s.Save(ctx.SettingsPath); }); }
            }
            return code;
        }

        private static int Rehab(Args a, CliContext ctx, Out w, TextWriter err)
        {
            bool doIt = a.Has("--yes");
            if (doIt && ctx.Archive.Unfinished().Count > 0) return UnfinishedRow(ctx, w);
            int code = 0;
            var mover = NewMover(a, ctx);
            foreach (var p in a.Paths)
            {
                if (!doIt)
                {
                    var rec = ctx.Archive.FindExiled(p);
                    if (rec == null) { w.Result(p, "refused", "not_exiled", Strings.NotExiled); code = Math.Max(code, ExitCodes.Refused); }
                    else w.Result(p, "ok", "ok", "Готов к реабилитации", rec.Target, rec.Bytes);
                    continue;
                }
                code = Math.Max(code, Report(w, mover.Rehab(p, Progress(w, err, p), CancellationToken.None)));
            }
            return code;
        }

        private static int Status(Args a, CliContext ctx, Out w)
        {
            int code = 0;
            foreach (var p in a.Paths)
            {
                var rec = ctx.Archive.FindExiled(p);
                if (rec == null) { w.Result(p, "refused", "not_exiled", Strings.NotExiled); code = ExitCodes.Refused; continue; }
                var row = Row(rec.Source, "exiled", "ok", Junction.IsReparsePoint(rec.Source) ? "Спецпоселенец" : Strings.CaseOpen, rec.Target, rec.Bytes);
                row["time"] = rec.TimeUtc.ToString("o");
                w.Row(row);
            }
            return code;
        }

        private static int List(CliContext ctx, Out w)
        {
            foreach (var r in ctx.Archive.Current())
                w.Row(new Dictionary<string, object> { ["path"] = r.Source, ["target"] = r.Target, ["status"] = r.Status, ["bytes"] = r.Bytes, ["operation"] = r.Operation, ["time"] = r.TimeUtc.ToString("o") });
            return 0;
        }

        private static int Recover(Args a, CliContext ctx, Out w)
        {
            if (!a.Has("--yes"))
            {
                foreach (var r in ctx.Archive.Unfinished()) w.Result(r.Source, "unfinished", r.Status, Strings.Recover, r.Target, r.Bytes);
                return ctx.Archive.Unfinished().Count > 0 ? ExitCodes.Unfinished : 0;
            }
            int code = 0;
            foreach (var r in NewMover(a, ctx).Recover())
            {
                w.Result(r.Source, r.Ok ? "done" : "failed", r.Reason, r.Message, r.Target, r.Bytes);
                if (!r.Ok) code = ExitCodes.Failed;
            }
            return code;
        }

        // ---------- helpers ----------

        private static Mover NewMover(Args a, CliContext ctx) =>
            new Mover(ctx.Archive, new MoverOptions { Strict = a.Has("--strict"), Stamp = !a.Has("--no-stamp"), StampIconPath = ctx.StampIconPath }, ctx.Hook);

        private static int UnfinishedRow(CliContext ctx, Out w)
        {
            foreach (var r in ctx.Archive.Unfinished())
                w.Result(r.Source, "refused", "unfinished", Strings.Recover + ": есть незакрытое дело. Peressylkin.exe recover --yes", r.Target, r.Bytes);
            return ExitCodes.Unfinished;
        }

        private static int Report(Out w, MoveResult r)
        {
            if (r.Ok) { w.Result(r.Source, "done", "ok", r.Message, r.Target, r.Bytes); return 0; }
            string msg = r.Message;
            if (r.Reason == "needs_admin")
                msg += ". Попросите человека выполнить: Start-Process '" + System.Reflection.Assembly.GetEntryAssembly()?.Location + "' -Verb RunAs";
            w.Result(r.Source, r.Reason == "locked" || r.Reason == "not_exiled" || r.Reason == "target_exists" || r.Reason == "no_space" || r.Reason == "not_found" ? "refused" : "failed",
                r.Reason, msg, r.Target, r.Bytes, r.Processes);
            switch (r.Reason)
            {
                case "locked": return ExitCodes.Locked;
                case "needs_admin": return ExitCodes.NeedsAdmin;
                case "not_exiled": case "target_exists": case "no_space": case "not_found": return ExitCodes.Refused;
                default: return ExitCodes.Failed;
            }
        }

        private static IProgress<CopyProgress> Progress(Out w, TextWriter err, string path)
        {
            if (!w.Json) return null;
            var last = DateTime.MinValue;
            return new SyncProgress(p =>
            {
                if ((DateTime.UtcNow - last).TotalMilliseconds < 250 && p.BytesDone < p.BytesTotal) return;
                last = DateTime.UtcNow;
                err.WriteLine(Ascii(new JavaScriptSerializer().Serialize(new { progress = new { path, bytesDone = p.BytesDone, bytesTotal = p.BytesTotal } })));
            });
        }

        private sealed class SyncProgress : IProgress<CopyProgress>
        {
            private readonly Action<CopyProgress> _a; public SyncProgress(Action<CopyProgress> a) { _a = a; }
            public void Report(CopyProgress v) { lock (this) _a(v); }
        }

        private static Dictionary<string, object> Row(string path, string verdict, string reason, string message, string target = null, long bytes = 0, string[] processes = null) =>
            new Dictionary<string, object> { ["path"] = path, ["verdict"] = verdict, ["reason"] = reason, ["message"] = message, ["target"] = target, ["bytes"] = bytes, ["processes"] = processes ?? new string[0] };

        private sealed class Out
        {
            private readonly TextWriter _w; public readonly bool Json;
            public Out(TextWriter w, bool json) { _w = w; Json = json; }
            public void Result(string path, string verdict, string reason, string message, string target = null, long bytes = 0, string[] processes = null) =>
                Row(Cli.Row(path, verdict, reason, message, target, bytes, processes));
            public void Row(Dictionary<string, object> row)
            {
                if (Json) { _w.WriteLine(Ascii(new JavaScriptSerializer().Serialize(row))); return; }
                var v = row.TryGetValue("verdict", out var vv) ? vv as string : row.TryGetValue("status", out var st) ? st as string : "";
                var mark = v == "ok" || v == "done" || v == "exiled" ? "[+]" : v == "warn" ? "[!]" : v == "refused" || v == "failed" ? "[x]" : "[ ]";
                row.TryGetValue("path", out var p); row.TryGetValue("message", out var m); row.TryGetValue("target", out var t);
                _w.WriteLine($"{mark} {p} — {m ?? v}{(t != null ? " → " + t : "")}");
            }
        }

        /// <summary>Escapes every non-ASCII char so output survives any console code page.</summary>
        internal static string Ascii(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s) if (ch < 128) sb.Append(ch); else sb.Append("\\u").Append(((int)ch).ToString("x4"));
            return sb.ToString();
        }

        private static object Schema() => new Dictionary<string, object>
        {
            ["tool"] = "Peressylkin (Перессылкин, Ссылка в Сибирь): move folders to another drive, leave an NTFS junction at the old path",
            ["commands"] = new Dictionary<string, object>
            {
                ["check"] = "check <path>... [--to <root>] [--by-name] [--ignore-launcher] [--allow-capital] — dry run, changes nothing",
                ["exile"] = "exile <path>... [--to <root>] [--yes] [--strict] [--ignore-launcher] [--by-name] [--no-stamp] [--allow-capital] — without --yes behaves like check",
                ["rehab"] = "rehab <path>... [--yes] — move back and remove the junction",
                ["status"] = "status <path>... — is it exiled, where, when",
                ["list"] = "list — whole archive",
                ["recover"] = "recover [--yes] — finish or roll back interrupted operations",
                ["schema"] = "schema — this description",
                ["selftest"] = "selftest [--to <root on another drive>] — built-in test range",
                ["install"] = "install — copy Peressylkin.exe to %LOCALAPPDATA%\\Sibir and add Explorer menu items (HKCU, no admin)",
                ["uninstall"] = "uninstall — remove Explorer menu items (archive kept)",
            },
            ["flags"] = Flags.ToArray(),
            ["result_fields"] = new[] { "path", "verdict (ok|warn|refused|done|failed|unfinished|exiled)", "reason", "message (human, Russian)", "target", "bytes", "processes" },
            ["progress"] = "with --json, stderr lines {\"progress\":{\"path\",\"bytesDone\",\"bytesTotal\"}}",
            ["reasons"] = Reasons,
            ["exit_codes"] = new Dictionary<string, string> { ["0"] = "ok", ["1"] = "bad arguments", ["2"] = "refused by checks", ["3"] = "locked by a process", ["4"] = "failed, rolled back", ["5"] = "needs administrator — ask the human", ["6"] = "unfinished cases — run recover" },
            ["safe_order"] = "check → exile --yes; never pass --ignore-launcher or --allow-capital without the user's consent",
        };

        private const string Help =
@"Peressylkin.exe — Перессылкин, Ссылка в Сибирь
  check  <path>... [--to <root>]           предварительное следствие (ничего не меняет)
  exile  <path>... [--to <root>] --yes     сослать (без --yes — только проверка)
         [--strict] [--ignore-launcher] [--by-name] [--no-stamp] [--allow-capital]
  rehab  <path>... --yes                   реабилитировать
  status <path>... | list | recover [--yes] | schema | selftest [--to <root>]
  --json  машинный вывод. В PowerShell: & Peressylkin.exe ... | Out-String";
    }

    internal static class Extensions
    {
        public static T Also<T>(this T x, Action<T> a) { a(x); return x; }
    }
}
