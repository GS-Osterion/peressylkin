using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using Sibir.Native;

namespace Sibir.Core
{
    /// <summary>
    /// Classified cases. Windows ACL: administrators read and change, the user may only append.
    /// A normal process never reads it; it appends with FILE_APPEND_DATA only.
    /// </summary>
    internal sealed class SecretArchive : Archive
    {
        public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sibir", ".secret.jsonl");

        private static readonly Lazy<bool> IsElevated = new Lazy<bool>(Shell.Elevation.IsAdmin);
        public static bool Elevated => IsElevated.Value;

        public SecretArchive(string filePath) : base(filePath) { }

        public static FileSecurity SecretAcl()
        {
            var s = new FileSecurity();
            s.SetAccessRuleProtection(true, false);
            s.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
            s.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
            s.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User, FileSystemRights.AppendData | FileSystemRights.Synchronize, AccessControlType.Allow));
            // OWNER RIGHTS: cancels the owner's implicit READ_CONTROL/WRITE_DAC (a file created without rights belongs to the user).
            s.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier("S-1-3-4"), FileSystemRights.Synchronize, AccessControlType.Allow));
            return s;
        }

        public override void Write(ArchiveRecord r)
        {
            using (ArchiveLock.Take(Path.GetDirectoryName(FilePath)))
            {
                if (Elevated && File.Exists(FilePath)) { EnsureAcl(); AppendLine(r); return; }
                var line = Utf8.GetBytes("\n" + Json.Serialize(r) + "\n");   // cannot read the tail: always start a fresh line
                using (var h = Open(FilePath, NativeMethods.FILE_APPEND_DATA | NativeMethods.SYNCHRONIZE, NativeMethods.OPEN_ALWAYS))
                    if (!NativeMethods.WriteFile(h, line, line.Length, out _, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        /// <summary>True if a normal process can read the file: it was replaced or its ACL removed.</summary>
        public bool IsCompromised()
        {
            if (Elevated || !File.Exists(FilePath)) return false;
            // Any of these lets the process read it now or grant itself read later.
            foreach (var access in new[] { NativeMethods.FILE_READ_DATA, NativeMethods.READ_CONTROL, NativeMethods.WRITE_DAC, NativeMethods.WRITE_OWNER, NativeMethods.FILE_WRITE_DATA })
                using (var h = NativeMethods.CreateFile(LongPath.Ex(FilePath), access, NativeMethods.FILE_SHARE_ALL, IntPtr.Zero, NativeMethods.OPEN_EXISTING, 0, IntPtr.Zero))
                    if (!h.IsInvalid) return true;
            return false;
        }

        /// <summary>Administrator: put the secret ACL back if it was changed.</summary>
        public void EnsureAcl()
        {
            if (!Elevated || !File.Exists(FilePath)) return;
            var want = SecretAcl();
            var have = File.GetAccessControl(FilePath, AccessControlSections.Access);
            if (have.GetSecurityDescriptorSddlForm(AccessControlSections.Access) != want.GetSecurityDescriptorSddlForm(AccessControlSections.Access))
                File.SetAccessControl(FilePath, want);
            File.SetAttributes(FilePath, File.GetAttributes(FilePath) | FileAttributes.Hidden);
        }

        public override IReadOnlyList<ArchiveRecord> Current()
        {
            if (!Elevated && File.Exists(FilePath)) throw new UnauthorizedAccessException(Strings.Classified);
            return base.Current();
        }

        public override void Remove(ISet<string> ids)
        {
            if (!Elevated) throw new UnauthorizedAccessException(Strings.Classified);
            base.Remove(ids);
            EnsureAcl();
        }

        protected override void WriteTemp(string tmp, byte[] content)
        {
            // A temp planted by a normal process keeps its own ACL under CREATE_ALWAYS: delete it, then create strictly new.
            NativeMethods.DeleteFile(LongPath.Ex(tmp));
            using (var h = Open(tmp, NativeMethods.GENERIC_WRITE, NativeMethods.CREATE_NEW))
                if (!NativeMethods.WriteFile(h, content, content.Length, out _, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        /// <summary>Opens (creating with the secret ACL and Hidden if absent) with exactly <paramref name="access"/>.</summary>
        private static SafeFileHandle Open(string path, uint access, uint disposition)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var sd = SecretAcl().GetSecurityDescriptorBinaryForm();
            var sdPtr = Marshal.AllocHGlobal(sd.Length);
            var saPtr = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.SECURITY_ATTRIBUTES>());
            try
            {
                Marshal.Copy(sd, 0, sdPtr, sd.Length);
                Marshal.StructureToPtr(new NativeMethods.SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf<NativeMethods.SECURITY_ATTRIBUTES>(), lpSecurityDescriptor = sdPtr }, saPtr, false);
                var h = NativeMethods.CreateFile(LongPath.Ex(path), access, NativeMethods.FILE_SHARE_ALL, saPtr, disposition, NativeMethods.FILE_ATTRIBUTE_HIDDEN, IntPtr.Zero);
                if (h.IsInvalid) throw new UnauthorizedAccessException(new Win32Exception(Marshal.GetLastWin32Error()).Message + ": " + path);
                return h;
            }
            finally { Marshal.FreeHGlobal(saPtr); Marshal.FreeHGlobal(sdPtr); }
        }
    }
}
