using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Sibir.Native;

namespace Sibir.Core
{
    internal enum Step { Resist, Copy, Verify, SwapRenameSource, SwapRenameTarget, SwapLink, Stamp, Control, Cleanup }

    internal interface IFaultHook { void Before(Step step, string source); }

    internal sealed class MoverOptions
    {
        public bool Strict { get; set; }
        public bool Stamp { get; set; } = true;
        public string StampIconPath { get; set; }
    }

    internal sealed class MoveResult
    {
        public string Source { get; set; }
        public string Target { get; set; }
        public bool Ok { get; set; }
        public string Reason { get; set; }
        public string Message { get; set; }
        public long Bytes { get; set; }
        public string[] Processes { get; set; } = new string[0];
    }

    internal sealed class VerifyException : Exception { public VerifyException(string m) : base(m) { } }

    /// <summary>Exile (move + junction), rehabilitation, rollback ("amnesty") and crash recovery. Spec §5.</summary>
    internal sealed class Mover
    {
        public const string StagingSuffix = ".~пересылка", OldSuffix = ".~этап", BackSuffix = ".~возвращение";

        private readonly Archive _archive;
        private readonly MoverOptions _opt;
        private readonly IFaultHook _hook;

        /// <summary>Tests only: let a hook exception escape as if the process died.</summary>
        internal bool SimulateCrash { get; set; }

        private readonly SecretArchive _secret;
        private readonly HashSet<string> _secretIds = new HashSet<string>();

        /// <param name="secret">Only from an elevated process: classified cases are rehabilitated and recovered from it.</param>
        public Mover(Archive archive, MoverOptions opt, IFaultHook hook = null, SecretArchive secret = null)
        {
            _archive = archive; _opt = opt; _hook = hook; _secret = secret;
        }

        /// <summary>Remember that this case lives in the secret archive: its progress must not leak into the public one.</summary>
        private ArchiveRecord Secret(ArchiveRecord r)
        {
            if (r != null) _secretIds.Add(r.Id);
            return r;
        }

        private void Save(ArchiveRecord r) => (_secretIds.Contains(r.Id) ? _secret : _archive).Write(r);

        private void AfterSecretRehab(ArchiveRecord r)
        {
            if (_secretIds.Contains(r.Id)) new Secrecy(_archive, _secret, null, null).RefreshRoot(SecretMark.RootOf(r.Target, null));
        }

        private void At(Step s, string src) => _hook?.Before(s, src);

        private static Func<string, bool> ExcludeStamp(bool created) =>
            created ? rel => string.Equals(rel, Stamp.FileName, StringComparison.OrdinalIgnoreCase) : (Func<string, bool>)null;

        // ---------------- exile ----------------

        public MoveResult Exile(string source, string target, IProgress<CopyProgress> progress, CancellationToken ct)
        {
            string src = LongPath.Normalize(source), tgt = LongPath.Normalize(target);
            string staging = tgt + StagingSuffix, old = src + OldSuffix;
            var res = new MoveResult { Source = src, Target = tgt };

            At(Step.Resist, src);
            var resist = Resistance(src, res);
            if (resist != null) return resist;
            if (Fs.DirExists(staging) || Fs.DirExists(old) || !Fs.IsEmptyDir(tgt))
                return Fail(res, "target_exists", Strings.TargetExists);

            var m = Manifest.Build(src);
            res.Bytes = m.TotalBytes;
            var rec = new ArchiveRecord { Id = Guid.NewGuid().ToString("N"), Operation = "exile", Source = src, Target = tgt, Bytes = m.TotalBytes, Status = "in_transit", TimeUtc = DateTime.UtcNow };
            Save(rec);
            try
            {
                At(Step.Copy, src);
                Directory.CreateDirectory(LongPath.Ex(Path.GetDirectoryName(tgt)));
                Copier.Copy(src, staging, m, progress, ct);

                At(Step.Verify, src);
                var copied = Manifest.Build(staging);
                var diff = copied.FirstDifference(Manifest.Build(src), true) ?? copied.FirstDifference(m, true);
                if (diff == null && _opt.Strict) diff = Copier.HashDiff(src, staging, m);
                if (diff != null) throw new VerifyException(diff);

                At(Step.SwapRenameSource, src);
                try { Directory.Move(LongPath.Ex(src), LongPath.Ex(old)); }
                catch (IOException e) { throw new LockedException(e.Message); }
                catch (UnauthorizedAccessException e) { throw new LockedException(e.Message); }

                At(Step.SwapRenameTarget, src);
                if (Fs.DirExists(tgt)) Directory.Delete(LongPath.Ex(tgt), false);   // empty placeholder
                Directory.Move(LongPath.Ex(staging), LongPath.Ex(tgt));

                At(Step.SwapLink, src);
                Junction.Create(src, tgt);

                At(Step.Stamp, src);
                if (_opt.Stamp && _opt.StampIconPath != null && Stamp.Apply(src, tgt, _opt.StampIconPath))
                {
                    rec.StampCreated = true;
                    Save(rec.With("in_transit"));
                }

                At(Step.Control, src);
                var control = Manifest.Build(src, ExcludeStamp(rec.StampCreated)).FirstDifference(Manifest.Build(old), true);
                if (control != null) throw new VerifyException(control);

                At(Step.Cleanup, src);
                Fs.DeleteTree(old);
                Save(rec.With("exiled"));
                res.Ok = true; res.Reason = "ok";
                res.Message = string.Format(Strings.Done, Strings.Size(res.Bytes));
                return res;
            }
            catch (Exception e) when (!SimulateCrash)
            {
                var (reason, msg) = Classify(e);
                string[] procs = reason == "locked" ? LockFinder.WhoHolds(Fs.DirExists(old) ? old : src) : null;
                AmnestyExile(rec, src, tgt);
                return Fail(res, reason, Strings.Amnesty + " " + msg, procs);
            }
        }

        private sealed class LockedException : Exception { public LockedException(string m) : base(m) { } }

        /// <summary>Null if the folder can be moved; otherwise "locked" (a process holds it) or "needs_admin" (no rights).</summary>
        private static MoveResult Resistance(string folder, MoveResult res)
        {
            if (LockFinder.CanRename(folder)) return null;
            var holders = LockFinder.WhoHolds(folder);
            if (holders.Length == 0 && !LockFinder.CanWriteNextTo(folder)) return Fail(res, "needs_admin", Strings.NeedsAdmin);
            return Fail(res, "locked", null, holders);
        }

        private static (string, string) Classify(Exception e)
        {
            switch (e)
            {
                case VerifyException v: return ("verify_failed", Strings.VerifyFailed + ": " + v.Message);
                case LockedException _: return ("locked", string.Format(Strings.Locked, "другой процесс"));
                case UnauthorizedAccessException _: return ("needs_admin", Strings.NeedsAdmin);
                case OperationCanceledException _: return ("cancelled", "Этап отменён");
                default: return ("copy_failed", Strings.CopyFailed + ": " + e.Message);
            }
        }

        /// <summary>Put the source back exactly as it was; remove our copies. Never deletes the target unless the source is a real folder again.</summary>
        private void AmnestyExile(ArchiveRecord rec, string src, string tgt)
        {
            string staging = tgt + StagingSuffix, old = src + OldSuffix;
            if (Junction.IsReparsePoint(src)) Junction.Delete(src);
            if (Fs.DirExists(old) && !Fs.DirExists(src)) Directory.Move(LongPath.Ex(old), LongPath.Ex(src));
            if (Fs.DirExists(src) && !Junction.IsReparsePoint(src))
            {
                Fs.DeleteTree(staging);
                Fs.DeleteTree(tgt);
                Save(rec.With("amnestied"));
            }
        }

        // ---------------- rehabilitation ----------------

        public MoveResult Rehab(string link, IProgress<CopyProgress> progress, CancellationToken ct)
        {
            string src = LongPath.Normalize(link), back = src + BackSuffix;
            var res = new MoveResult { Source = src };
            var rec = _archive.FindExiled(src) ?? Secret(_secret?.FindExiled(src));
            if (rec == null) return Fail(res, "not_exiled", Strings.NotExiled);
            string tgt = rec.Target;
            res.Target = tgt;
            if (!Junction.IsReparsePoint(src) && Fs.DirExists(src)) return Fail(res, "target_exists", Strings.TargetExists);
            if (!Fs.DirExists(tgt)) return Fail(res, "not_found", "В Сибири гражданин не найден: " + tgt);
            if (Fs.DirExists(back)) return Fail(res, "target_exists", Strings.TargetExists);

            var exclude = ExcludeStamp(rec.StampCreated);
            var m = Manifest.Build(tgt, exclude);
            res.Bytes = m.TotalBytes;
            try
            {
                var free = new DriveInfo(Path.GetPathRoot(src)).AvailableFreeSpace;
                if (free < m.TotalBytes + m.TotalBytes / 20)
                    return Fail(res, "no_space", string.Format(Strings.HomeNoSpace, Strings.Size(m.TotalBytes + m.TotalBytes / 20), Strings.Size(free)));
            }
            catch (ArgumentException) { }

            At(Step.Resist, src);
            var resist = Resistance(tgt, res) ?? (LockFinder.CanWriteNextTo(src) ? null : Fail(res, "needs_admin", Strings.NeedsAdmin));
            if (resist != null) return resist;

            var r2 = rec.With("rehabilitating"); r2.Operation = "rehab";
            Save(r2);
            try
            {
                At(Step.Copy, src);
                Copier.Copy(tgt, back, m, progress, ct);
                At(Step.Verify, src);
                var diff = Manifest.Build(back).FirstDifference(Manifest.Build(tgt, exclude), true);
                if (diff == null && _opt.Strict) diff = Copier.HashDiff(tgt, back, m);
                if (diff != null) throw new VerifyException(diff);
                At(Step.SwapLink, src);
                if (Junction.IsReparsePoint(src)) Junction.Delete(src);
                At(Step.SwapRenameTarget, src);
                Directory.Move(LongPath.Ex(back), LongPath.Ex(src));
                At(Step.Cleanup, src);
                Fs.DeleteTree(tgt);
                Save(r2.With("rehabilitated"));
                AfterSecretRehab(r2);
                res.Ok = true; res.Reason = "ok"; res.Message = Strings.RehabDone;
                return res;
            }
            catch (Exception e) when (!SimulateCrash)
            {
                var (reason, msg) = Classify(e);
                RestoreExiled(r2, src, tgt);
                return Fail(res, reason, Strings.Amnesty + " " + msg);
            }
        }

        /// <summary>Failed rehabilitation: keep the folder in Siberia and the link in place.</summary>
        private void RestoreExiled(ArchiveRecord rec, string src, string tgt)
        {
            var back = src + BackSuffix;
            if (!Fs.DirExists(src) && Fs.DirExists(tgt)) Junction.Create(src, tgt);
            if (Junction.IsReparsePoint(src)) Fs.DeleteTree(back);
            Save(rec.With("exiled"));
        }

        // ---------------- recovery ----------------

        public List<MoveResult> Recover()
        {
            var results = new List<MoveResult>();
            foreach (var rec in _archive.Unfinished().Concat(_secret == null ? new List<ArchiveRecord>() : _secret.Unfinished().Select(Secret)).ToList())
            {
                string src = rec.Source, tgt = rec.Target, old = src + OldSuffix, back = src + BackSuffix;
                var res = new MoveResult { Source = src, Target = tgt, Bytes = rec.Bytes };
                try
                {
                    if (rec.Status == "in_transit")
                    {
                        bool linked = Junction.IsReparsePoint(src) && LongPath.Same(Junction.GetTarget(src) ?? "", tgt) && Fs.DirExists(tgt);
                        bool complete = linked && (!Fs.DirExists(old) ||
                            Manifest.Build(tgt, ExcludeStamp(rec.StampCreated)).FirstDifference(Manifest.Build(old), true) == null);
                        if (complete)
                        {
                            Fs.DeleteTree(old);
                            Save(rec.With("exiled"));
                            res.Ok = true; res.Reason = "ok"; res.Message = string.Format(Strings.Done, Strings.Size(rec.Bytes));
                        }
                        else
                        {
                            AmnestyExile(rec, src, tgt);
                            res.Ok = true; res.Reason = "amnestied"; res.Message = Strings.Amnesty;
                        }
                    }
                    else if (rec.Status == "rehabilitating")
                    {
                        if (Fs.DirExists(src) && !Junction.IsReparsePoint(src) && !Fs.DirExists(back))
                        {
                            Fs.DeleteTree(tgt);
                            Save(rec.With("rehabilitated"));
                            AfterSecretRehab(rec);
                            res.Ok = true; res.Reason = "ok"; res.Message = Strings.RehabDone;
                        }
                        else
                        {
                            RestoreExiled(rec, src, tgt);
                            res.Ok = true; res.Reason = "exiled"; res.Message = "Реабилитация отменена, гражданин остаётся в Сибири";
                        }
                    }
                }
                catch (Exception e) { res.Ok = false; res.Reason = "copy_failed"; res.Message = e.Message; }
                results.Add(res);
            }
            return results;
        }

        private static MoveResult Fail(MoveResult r, string reason, string message, string[] processes = null)
        {
            r.Ok = false; r.Reason = reason;
            r.Processes = processes ?? new string[0];
            r.Message = message ?? string.Format(Strings.Locked, r.Processes.Length > 0 ? string.Join(", ", r.Processes) : "неизвестный процесс");
            return r;
        }
    }
}
