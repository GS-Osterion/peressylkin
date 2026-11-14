using System;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;

namespace Sibir.Core
{
    /// <summary>One cross-process lock for every write to the case archives (append and rewrite).</summary>
    internal static class ArchiveLock
    {
        private const string Name = @"Local\Sibir.Archive";

        public static IDisposable Take()
        {
            var sec = new MutexSecurity();
            // An elevated creator's default DACL would lock normal processes out: grant the user explicitly.
            sec.AddAccessRule(new MutexAccessRule(WindowsIdentity.GetCurrent().User, MutexRights.Synchronize | MutexRights.Modify, AccessControlType.Allow));
            sec.AddAccessRule(new MutexAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), MutexRights.FullControl, AccessControlType.Allow));
            Mutex m;
            try { m = new Mutex(false, Name, out _, sec); }
            catch (UnauthorizedAccessException) { m = Mutex.OpenExisting(Name, MutexRights.Synchronize | MutexRights.Modify); }
            try { m.WaitOne(); }
            catch (AbandonedMutexException) { }   // previous holder died: the lock is ours
            return new Held(m);
        }

        private sealed class Held : IDisposable
        {
            private Mutex _m;
            public Held(Mutex m) { _m = m; }
            public void Dispose()
            {
                if (_m == null) return;
                _m.ReleaseMutex(); _m.Dispose(); _m = null;
            }
        }
    }
}
