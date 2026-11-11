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
        public DateTime TimeUtc { get; set; }

        public ArchiveRecord With(string status)
        {
            var c = (ArchiveRecord)MemberwiseClone();
            c.Status = status; c.TimeUtc = DateTime.UtcNow;
            return c;
        }
    }

    /// <summary>Append-only JSONL journal ("Хранить вечно"). Later lines with the same Id supersede earlier ones.</summary>
    internal sealed class Archive
    {
        public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sibir", "archive.jsonl");

        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        private static readonly Encoding Utf8 = new UTF8Encoding(false);
        private readonly string _path;

        public Archive(string filePath) { _path = filePath; }

        public string FilePath => _path;

        public void Write(ArchiveRecord r)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            var line = Utf8.GetBytes(Json.Serialize(r) + "\n");
            using (var fs = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read))
            {
                // A previous crash may have left a partial line without newline: start fresh line.
                if (fs.Length > 0 && LastByte() != (byte)'\n') fs.WriteByte((byte)'\n');
                fs.Write(line, 0, line.Length);
                fs.Flush(true);
            }
        }

        private byte LastByte()
        {
            using (var r = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                r.Seek(-1, SeekOrigin.End);
                return (byte)r.ReadByte();
            }
        }

        public IReadOnlyList<ArchiveRecord> Current()
        {
            var order = new List<string>();
            var map = new Dictionary<string, ArchiveRecord>();
            if (!File.Exists(_path)) return order.Select(i => map[i]).ToList();
            string text;
            using (var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
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

        public ArchiveRecord FindExiled(string source) =>
            Current().LastOrDefault(r => r.Status == "exiled" && LongPath.Same(r.Source, source));

        public IReadOnlyList<ArchiveRecord> Unfinished() =>
            Current().Where(r => r.Status == "in_transit" || r.Status == "rehabilitating").ToList();
    }
}
