using System.Collections.Generic;
using System.Linq;
using Sibir.Native;

namespace Sibir.Core
{
    /// <summary>«Засекретить», «Рассекретить», «Сжечь дело» (spec 2026-11-14-secret-archive).</summary>
    internal sealed class Secrecy
    {
        private readonly Archive _pub;
        private readonly SecretArchive _secret;
        private readonly IEnumerable<string> _roots;
        private readonly string _icon;

        public Secrecy(Archive pub, SecretArchive secret, IEnumerable<string> knownRoots, string stampIconPath)
        {
            _pub = pub; _secret = secret; _roots = knownRoots ?? Enumerable.Empty<string>(); _icon = stampIconPath;
        }

        private static MoveResult Res(ArchiveRecord r, bool ok, string reason, string msg) =>
            new MoveResult { Source = r.Source, Target = r.Target, Bytes = r.Bytes, Ok = ok, Reason = reason, Message = msg };

        private static ISet<string> Ids(ArchiveRecord r) => new HashSet<string> { r.Id };

        public string RootOf(ArchiveRecord r) => SecretMark.RootOf(r.Target, _roots);

        /// <summary>Any user. Order: unstamp → secret line → marks → erase from the public archive. Repeating is idempotent.</summary>
        public MoveResult Classify(ArchiveRecord rec)
        {
            using (ArchiveLock.Take(System.IO.Path.GetDirectoryName(_pub.FilePath)))   // re-entrant: the writes below take it again
            {
                // The row may be stale (an open window): act only on the case as it is now.
                var now = _pub.Current().FirstOrDefault(r => r.Id == rec.Id);
                if (now == null || now.Status != rec.Status) return Res(rec, false, "not_allowed", "Дело изменилось — обновите таблицу");
                return ClassifyLocked(now);
            }
        }

        private MoveResult ClassifyLocked(ArchiveRecord rec)
        {
            bool exiled = rec.Status == "exiled";
            if (!exiled && rec.Status != "rehabilitated") return Res(rec, false, "not_allowed", "Засекретить можно только сосланное или реабилитированное дело");
            if (_secret.IsCompromised()) return Res(rec, false, "safe_broken", Strings.SafeBroken);
            if (exiled && !SecretMark.SupportsStreams(rec.Target)) return Res(rec, false, "no_streams", Strings.NoStreams);

            if (exiled && rec.StampCreated) Stamp.Unstamp(rec.Source, rec.Target);
            var copy = rec.Copy();
            copy.StampRemoved = rec.StampCreated || rec.StampRemoved;
            copy.StampCreated = false;
            _secret.Write(copy);
            if (exiled) { SecretMark.Set(rec.Target); SecretMark.Set(RootOf(rec)); }
            _pub.Remove(Ids(rec));
            return Res(rec, true, "ok", "Дело засекречено");
        }

        /// <summary>Administrator. Back to the public archive, stamp returned if classifying took it off.</summary>
        public MoveResult Declassify(ArchiveRecord rec)
        {
            var copy = rec.Copy();
            copy.StampCreated = false; copy.StampRemoved = false;
            bool exiled = rec.Status == "exiled";
            if (exiled && rec.StampRemoved && _icon != null && Junction.IsReparsePoint(rec.Source))
                copy.StampCreated = Stamp.Apply(rec.Source, rec.Target, _icon);
            _pub.Write(copy);
            _secret.Remove(Ids(rec));
            if (exiled) { SecretMark.Clear(rec.Target); RefreshRoot(RootOf(rec)); }
            return Res(rec, true, "ok", "Дело рассекречено");
        }

        /// <summary>Only rehabilitated cases in this release (any status — «политический террор», later).</summary>
        public static MoveResult Burn(ArchiveRecord rec, Archive where)
        {
            if (rec.Status != "rehabilitated") return Res(rec, false, "not_allowed", "Сжечь можно только дело реабилитированного");
            where.Remove(Ids(rec));
            return Res(rec, true, "ok", "Дело сожжено");
        }

        /// <summary>Administrator: drop the root mark once no classified exile lives under it.</summary>
        public void RefreshRoot(string root)
        {
            if (string.IsNullOrEmpty(root)) return;
            if (!_secret.Current().Any(r => (r.Status == "exiled" || r.Status == "in_transit" || r.Status == "rehabilitating") && LongPath.SameOrUnder(r.Target, root)))
                SecretMark.Clear(root);
        }

        /// <summary>A link whose case this process cannot see but whose target carries the secret mark.</summary>
        public static bool IsClassifiedLink(string path, Archive pub)
        {
            if (!Junction.IsReparsePoint(path) || pub.FindExiled(path) != null) return false;
            var t = Junction.GetTarget(path);
            return t != null && SecretMark.Has(t);
        }
    }
}
