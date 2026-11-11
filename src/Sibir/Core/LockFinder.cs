using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Sibir.Native;

namespace Sibir.Core
{
    /// <summary>Who resists the move: rename probe + Windows Restart Manager.</summary>
    internal static class LockFinder
    {
        public const string ProbeSuffix = ".~проверка";

        /// <summary>Rename the folder and immediately back. False if something inside is held open.</summary>
        public static bool CanRename(string folder)
        {
            var a = LongPath.Ex(folder);
            var b = a + ProbeSuffix;
            try { Directory.Move(a, b); }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            for (int i = 0; ; i++)
            {
                try { Directory.Move(b, a); return true; }
                catch (Exception) when (i < 50) { Thread.Sleep(100); }
            }
        }

        public static string[] WhoHolds(string folder)
        {
            var files = new List<string>();
            try
            {
                foreach (var f in Directory.EnumerateFiles(LongPath.Ex(folder), "*", SearchOption.AllDirectories))
                {
                    files.Add(f);
                    if (files.Count >= 2000) break;
                }
            }
            catch (Exception) { }
            if (files.Count == 0) return new string[0];

            if (NativeMethods.RmStartSession(out var session, 0, new StringBuilder(64)) != 0) return new string[0];
            try
            {
                if (NativeMethods.RmRegisterResources(session, (uint)files.Count, files.ToArray(), 0, null, 0, null) != 0) return new string[0];
                uint needed, count = 0, reasons = 0;
                int rc = NativeMethods.RmGetList(session, out needed, ref count, null, ref reasons);
                if (rc != 234 /* ERROR_MORE_DATA */ || needed == 0) return new string[0];
                var info = new NativeMethods.RM_PROCESS_INFO[needed];
                count = needed;
                if (NativeMethods.RmGetList(session, out needed, ref count, info, ref reasons) != 0) return new string[0];
                return info.Take((int)count).Select(p =>
                {
                    try { return Process.GetProcessById(p.Process.dwProcessId).ProcessName + ".exe"; }
                    catch { return p.strAppName; }
                }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            }
            finally { NativeMethods.RmEndSession(session); }
        }
    }
}
