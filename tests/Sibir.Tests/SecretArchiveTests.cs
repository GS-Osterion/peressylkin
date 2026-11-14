using System;
using System.IO;
using Sibir.Core;

namespace Sibir.Tests
{
    public class SecretArchiveTests : IDisposable
    {
        private readonly TempDir _t = new TempDir();
        private readonly string _path;
        private readonly SecretArchive _s;

        public SecretArchiveTests() { _path = _t.Sub(@"Sibir\.secret.jsonl"); _s = new SecretArchive(_path); }
        public void Dispose() => _t.Dispose();

        private static ArchiveRecord Rec(string id, string status = "exiled") =>
            new ArchiveRecord { Id = id, Operation = "exile", Source = @"C:\a" + id, Target = @"T:\Сибирь\a" + id, Bytes = 1, Status = status, TimeUtc = DateTime.UtcNow };

        private static void NeedNormal() { if (SecretArchive.Elevated) Assert.Skip("run without administrator rights"); }
        private static void NeedAdmin() { if (!SecretArchive.Elevated) Assert.Skip("run as administrator"); }

        [Fact] public void NotElevated_AppendCreatesUnreadableHiddenFile()
        {
            NeedNormal();
            _s.Write(Rec("1"));
            _s.Write(Rec("2"));   // second append on the existing file works
            Assert.Throws<UnauthorizedAccessException>(() => File.ReadAllText(_path));
            Assert.True((File.GetAttributes(_path) & FileAttributes.Hidden) != 0, "hidden");
            Assert.False(_s.IsCompromised(), "own file is not compromised");
        }

        [Fact] public void NotElevated_AclCannotBeRead()
        {
            NeedNormal();
            _s.Write(Rec("1"));
            Assert.Throws<UnauthorizedAccessException>(() => File.GetAccessControl(_path));
        }

        [Fact] public void PlainFileInPlace_IsCompromised()
        {
            NeedNormal();
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            File.WriteAllText(_path, "");
            Assert.True(_s.IsCompromised());
        }

        [Fact] public void PlantedFileWithWriteDac_IsCompromised()
        {
            NeedNormal();
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            File.WriteAllText(_path, "");
            var sec = new System.Security.AccessControl.FileSecurity();
            sec.SetAccessRuleProtection(true, false);
            var me = System.Security.Principal.WindowsIdentity.GetCurrent().User;
            var rights = System.Security.AccessControl.FileSystemRights.AppendData | System.Security.AccessControl.FileSystemRights.Synchronize | System.Security.AccessControl.FileSystemRights.ChangePermissions;
            sec.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(me, rights, System.Security.AccessControl.AccessControlType.Allow));
            sec.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(new System.Security.Principal.SecurityIdentifier("S-1-3-4"), rights, System.Security.AccessControl.AccessControlType.Allow));
            File.SetAccessControl(_path, sec);   // no read, no READ_CONTROL — but it can give itself read later
            Assert.True(_s.IsCompromised());
        }

        [Fact] public void Elevated_RemoveIgnoresPlantedTemp()
        {
            NeedAdmin();
            _s.Write(Rec("1")); _s.Write(Rec("2"));
            File.WriteAllText(_path + ".~tmp", "planted");   // readable temp planted by a normal process
            _s.Remove(new System.Collections.Generic.HashSet<string> { "1" });
            Assert.False(File.Exists(_path + ".~tmp"));
            Assert.True(SecretArchive.HasSecretAcl(_path), "secret ACL");
        }

        [Fact] public void NoFile_NotCompromised() { NeedNormal(); Assert.False(_s.IsCompromised()); }

        [Fact] public void NotElevated_CurrentThrows()
        {
            NeedNormal();
            _s.Write(Rec("1"));
            Assert.Throws<UnauthorizedAccessException>(() => _s.Current());
        }

        [Fact] public void Elevated_ReadsAndDuplicatesCollapse()
        {
            NeedAdmin();
            _s.Write(Rec("1", "in_transit"));
            _s.Write(Rec("1", "exiled"));
            _s.Write(Rec("2"));
            Assert.Equal(2, _s.Current().Count);
            _s.Remove(new System.Collections.Generic.HashSet<string> { "1" });
            Assert.Equal(1, _s.Current().Count);
        }

        [Fact] public void Elevated_EnsureAclRepairsReadableFile()
        {
            NeedAdmin();
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            File.WriteAllText(_path, "");
            Assert.False(SecretArchive.HasSecretAcl(_path), "plain file is not secret");
            _s.EnsureAcl();
            Assert.True(SecretArchive.HasSecretAcl(_path), "secret ACL");
        }
    }
}
