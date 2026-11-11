using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace Sibir.Core
{
    internal sealed class Settings
    {
        public string LastRoot { get; set; }
        public bool ProtectCapital { get; set; } = true;
        public List<string> RegimeDrives { get; set; } = new List<string>();   // "G:"

        public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sibir", "settings.json");

        public static Settings Load(string path)
        {
            try
            {
                var s = new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(path, Encoding.UTF8));
                if (s == null) return new Settings();
                s.RegimeDrives = s.RegimeDrives ?? new List<string>();
                return s;
            }
            catch { return new Settings(); }
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, new JavaScriptSerializer().Serialize(this), new UTF8Encoding(false));
        }
    }
}
