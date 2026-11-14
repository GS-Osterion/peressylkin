using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using Sibir.Native;

namespace Sibir.Core
{
    internal sealed class ArchiveRecord
    {
        public string Id { get; set; }
        public string Operation { get; set; }   // exile | rehab
        public string Source { get; set; }
        public string Target { get; set; }
        public long Bytes { get; set; }
        public string Status { get; set; }      // in_transit | exiled | amnestied | rehabilitating | rehabilitated
        public bool StampCreated { get; set; }
        /// <summary>Our stamp was taken off when the case was classified (declassify puts it back).</summary>
        public bool StampRemoved { get; set; }
        public DateTime TimeUtc { get; set; }

        public ArchiveRecord With(string status)
        {
            var c = (ArchiveRecord)MemberwiseClone();
            c.Status = status; c.TimeUtc = DateTime.UtcNow;
            return c;
        }
    }

    /// <summary>Append-only JSONL journal ("Хранить вечно"). Later lines with the same Id supersede earlier ones.</summary>
    internal class Archive
    {
        public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sibir", "archive.jsonl");

        protected static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        private const char Nl = (char)10, Cr = (char)13;
        protected static readonly Encoding Utf8 = new UTF8Encoding(false);
        private readonly string _path;

        public Archive(string filePath) { _path = filePath; }

        public string FilePath => _path;

        public virtual void Write(ArchiveRecord r)
        {
            using (ArchiveLock.Take()) AppendLine(r);
        }

        /// <summary>Plain append; caller holds <see cref="ArchiveLock"/>.</summary>
        protected void AppendLine(ArchiveRecord r)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            var line = Utf8.GetBytes(Json.Serialize(r) + "\n");
            using (var fs = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read | FileShare.Delete))
            {
                // A previous crash may have left a partial line without newline: start fresh line.
                if (fs.Length > 0 && LastByte() != (byte)'\n') fs.WriteByte((byte)'\n');
                fs.Write(line, 0, line.Length);
                fs.Flush(true);
            }
        }

        private byte LastByte()
        {
            using (var r = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                r.Seek(-1, SeekOrigin.End);
                return (byte)r.ReadByte();
            }
        }

        public virtual IReadOnlyList<ArchiveRecord> Current()
        {
            var order = new List<string>();
            var map = new Dictionary<string, ArchiveRecord>();
            if (!File.Exists(_path)) return order.Select(i => map[i]).ToList();
            string text;
            using (var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sr = new StreamReader(fs, Utf8)) text = sr.ReadToEnd();
            foreach (var line in text.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                ArchiveRecord r;
                try { r = Json.Deserialize<ArchiveRecord>(line); } catch { continue; }
                if (r?.Id == null || r.Source == null) continue;
                if (!map.ContainsKey(r.Id)) order.Add(r.Id);
                map[r.Id] = r;
            }
            return order.Select(i => map[i]).ToList();
        }

        /// <summary>Erase every line of these cases: rewrite via a temp file in the same folder, no backup.</summary>
        public virtual void Remove(ISet<string> ids)
        {
            using (ArchiveLock.Take())
            {
                if (!File.Exists(_path)) return;
                string text;
                using (var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sr = new StreamReader(fs, Utf8)) text = sr.ReadToEnd();
                var kept = new StringBuilder();
                foreach (var line in text.Split(Nl))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    ArchiveRecord r = null;
                    try { r = Json.Deserialize<ArchiveRecord>(line); } catch { }
                    if (r?.Id != null && ids.Contains(r.Id)) continue;
                    kept.Append(line.TrimEnd(Cr)).Append(Nl);
                }
                var tmp = _path + ".~tmp";
                WriteTemp(tmp, Utf8.GetBytes(kept.ToString()));
                File.Replace(tmp, _path, null);
            }
        }

        protected virtual void WriteTemp(string tmp, byte[] content)
        {
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(content, 0, content.Length);
                fs.Flush(true);
            }
        }

        public ArchiveRecord FindExiled(string source) =>
            Current().LastOrDefault(r => r.Status == "exiled" && LongPath.Same(r.Source, source));

        public IReadOnlyList<ArchiveRecord> Unfinished() =>
            Current().Where(r => r.Status == "in_transit" || r.Status == "rehabilitating").ToList();
    }
}
