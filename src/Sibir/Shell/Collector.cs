using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace Sibir.Shell
{
    /// <summary>
    /// Explorer starts one process per selected folder. The first one (mutex owner) collects
    /// the other paths over a named pipe until `window` of silence, then shows one dialog.
    /// </summary>
    internal static class Collector
    {
        /// <returns>All paths if this process is the collector; null if the path was handed to another process.</returns>
        public static string[] Gather(string verb, string path, TimeSpan window)
        {
            var session = Process.GetCurrentProcess().SessionId;
            var pipe = $"Sibir.{verb}.{session}";
            using (var mutex = new Mutex(true, $@"Local\Sibir.{verb}", out bool owner))
            {
                if (!owner)
                    return Send(pipe, path) ? null : new[] { path };
                try { return Collect(pipe, path, window); }
                finally { mutex.ReleaseMutex(); }
            }
        }

        private static string[] Collect(string pipe, string first, TimeSpan window)
        {
            var paths = new List<string> { first };
            while (true)
            {
                using (var server = new NamedPipeServerStream(pipe, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
                {
                    var wait = server.WaitForConnectionAsync();
                    if (!wait.Wait(window)) break;
                    using (var r = new StreamReader(server, Encoding.UTF8))
                    {
                        string line;
                        while ((line = r.ReadLine()) != null)
                            if (line.Length > 0 && !paths.Contains(line)) paths.Add(line);
                    }
                }
            }
            return paths.ToArray();
        }

        private static bool Send(string pipe, string path)
        {
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    using (var c = new NamedPipeClientStream(".", pipe, PipeDirection.Out))
                    {
                        c.Connect(100);
                        var bytes = Encoding.UTF8.GetBytes(path + "\n");
                        c.Write(bytes, 0, bytes.Length);
                        c.Flush();
                        return true;
                    }
                }
                catch (TimeoutException) { }
                catch (IOException) { Thread.Sleep(30); }
            }
            return false;
        }
    }
}
