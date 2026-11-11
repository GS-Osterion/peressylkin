using System.IO;
using System.Text;
using Sibir.Native;

namespace Sibir.Core
{
    /// <summary>Red stamp icon on an exiled folder: desktop.ini in the target + System attribute on the junction.</summary>
    internal static class Stamp
    {
        public const string FileName = "desktop.ini";

        /// <summary>True if we created desktop.ini (an existing one is never touched).</summary>
        public static bool Apply(string link, string targetDir, string iconPath)
        {
            var ini = LongPath.Ex(Path.Combine(targetDir, FileName));
            if (File.Exists(ini)) return false;
            File.WriteAllText(ini, "[.ShellClassInfo]\r\nIconResource=" + iconPath + ",0\r\n", Encoding.Unicode);
            File.SetAttributes(ini, FileAttributes.Hidden | FileAttributes.System);
            var l = LongPath.Ex(link);
            File.SetAttributes(l, File.GetAttributes(l) | FileAttributes.System);
            return true;
        }

        public static void Remove(string dir)
        {
            var ini = LongPath.Ex(Path.Combine(dir, FileName));
            if (!File.Exists(ini)) return;
            File.SetAttributes(ini, FileAttributes.Normal);
            File.Delete(ini);
        }
    }
}
