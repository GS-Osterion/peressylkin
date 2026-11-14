using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Sibir.Native;

namespace Sibir.Core
{
    /// <summary>
    /// One lock for every write to the case archives (append and rewrite): an exclusive, delete-on-close lock file
    /// next to them. Works across sessions (RDP, SSH, service) and between elevated and normal processes,
    /// unlike a Local\ mutex, and needs no global-object privilege. Re-entrant per thread.
    /// </summary>
    internal static class ArchiveLock
    {
        public const string FileName = ".lock";
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(1);

        [ThreadStatic] private static Dictionary<string, Held> _held;

        public static IDisposable Take(string dir) => Take(dir, DefaultTimeout);

        public static IDisposable Take(string dir, TimeSpan timeout)
        {
            var key = LongPath.Normalize(dir).ToUpperInvariant();
            _held = _held ?? new Dictionary<string, Held>();
            if (_held.TryGetValue(key, out var h)) { h.Depth++; return h; }

            Directory.CreateDirectory(LongPath.Ex(dir));
            var path = Path.Combine(dir, FileName);
            var sw = Stopwatch.StartNew();
            while (true)
            {
                try
                {
                    var fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
                    h = new Held(key, fs);
                    _held[key] = h;
                    return h;
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)   // held by someone, or being deleted
                {
                    if (sw.Elapsed >= timeout) throw new IOException("Архив занят другим процессом Перессылкина: " + dir, e);
                }
                Thread.Sleep(15);
            }
        }

        private sealed class Held : IDisposable
        {
            private readonly string _key;
            private FileStream _fs;
            public int Depth = 1;
            public Held(string key, FileStream fs) { _key = key; _fs = fs; }
            public void Dispose()
            {
                if (_fs == null || --Depth > 0) return;
                _held.Remove(_key);
                _fs.Dispose(); _fs = null;
            }
        }
    }
}
