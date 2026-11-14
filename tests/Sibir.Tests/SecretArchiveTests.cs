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
            _s.EnsureAcl();
            var want = SecretArchive.SecretAcl().GetSecurityDescriptorSddlForm(System.Security.AccessControl.AccessControlSections.Access);
            Assert.Equal(want, File.GetAccessControl(_path).GetSecurityDescriptorSddlForm(System.Security.AccessControl.AccessControlSections.Access));
        }
    }
}
