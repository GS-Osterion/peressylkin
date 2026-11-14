using Sibir.Native;

namespace Sibir.Core
{
    /// <summary>Network drives are "abroad": the program never works with them.</summary>
    internal static class Abroad
    {
        /// <summary>"network_source" for a network path, "network_target" for a link pointing abroad, else null.</summary>
        public static string ReasonFor(string path, IDriveEnv env)
        {
            if (env.IsNetworkPath(path)) return "network_source";
            if (Junction.IsReparsePoint(path))
            {
                var t = Junction.GetTarget(path);
                if (t != null && env.IsNetworkPath(t)) return "network_target";
            }
            return null;
        }
    }
}
