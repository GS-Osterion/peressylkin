using System;
using System.Collections.Generic;
using System.Linq;
using Sibir.Core;
using Sibir.Native;

namespace Sibir.Tests
{
    /// <summary>Drives are fake; volume of a path = longest matching registered prefix.</summary>
    internal sealed class FakeEnv : IDriveEnv
    {
        public List<DriveFacts> DriveList = new List<DriveFacts>();
        public Dictionary<string, string> Prefixes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> NetworkVolumes = new HashSet<string>();
        public string SystemDrive { get; set; } = @"C:\";

        public FakeEnv Drive(string root, string vol, long freeGb, bool removable = false, bool network = false)
        {
            DriveList.Add(new DriveFacts { Root = root, VolumeId = vol, FreeBytes = freeGb * 1024L * 1024 * 1024, IsFixed = !removable && !network, IsRemovable = removable, IsNetwork = network });
            Prefixes[root] = vol;
            if (network) NetworkVolumes.Add(vol);
            return this;
        }

        public FakeEnv Map(string prefix, string vol) { Prefixes[LongPath.Normalize(prefix)] = vol; return this; }

        public IReadOnlyList<DriveFacts> Drives() => DriveList;

        public string VolumeIdOf(string path)
        {
            var n = LongPath.Normalize(path);
            var best = Prefixes.Keys.Where(k => LongPath.SameOrUnder(n, k)).OrderByDescending(k => k.Length).FirstOrDefault();
            return best == null ? "vol?" + n.Substring(0, 1).ToUpperInvariant() : Prefixes[best];
        }

        public bool IsNetworkPath(string path) => path.StartsWith(@"\\") || NetworkVolumes.Contains(VolumeIdOf(path));
        public long SizeOf(string folder) => Fs.SizeOf(folder);
    }
}
