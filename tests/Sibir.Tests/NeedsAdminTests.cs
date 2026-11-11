using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using Sibir.Core;
using Sibir.Native;

namespace Sibir.Tests
{
    /// <summary>A folder whose parent the user may not write to (like Program Files without admin) must give needs_admin, not "locked".</summary>
    public class NeedsAdminTests : IDisposable
    {
        private readonly TempDir _t = new TempDir();
        private readonly string _parent, _src;
        private readonly FileSystemAccessRule _deny;

        public NeedsAdminTests()
        {
            _parent = _t.Sub("guarded"); _src = Path.Combine(_parent, "App");
            Directory.CreateDirectory(_src); File.WriteAllText(Path.Combine(_src, "a.dll"), "x");
            _deny = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User,
                FileSystemRights.CreateDirectories | FileSystemRights.CreateFiles | FileSystemRights.DeleteSubdirectoriesAndFiles,
                InheritanceFlags.None, PropagationFlags.None, AccessControlType.Deny);
            var sec = Directory.GetAccessControl(_parent); sec.AddAccessRule(_deny); Directory.SetAccessControl(_parent, sec);
        }

        public void Dispose()
        {
            var sec = Directory.GetAccessControl(_parent); sec.RemoveAccessRule(_deny); Directory.SetAccessControl(_parent, sec);
            _t.Dispose();
        }

        [Fact] public void LockFinder_DeniedParent_NotWritable()
        {
            Assert.False(LockFinder.CanRename(_src));
            Assert.False(LockFinder.CanWriteNextTo(_src));
            Assert.True(LockFinder.CanWriteNextTo(_parent));
        }

        [Fact] public void Mover_DeniedParent_NeedsAdmin_NothingTouched()
        {
            var r = new Mover(new Archive(_t.Sub("a.jsonl")), new MoverOptions()).Exile(_src, _t.Sub(@"sib\App"), null, CancellationToken.None);
            Assert.Equal("needs_admin", r.Reason);
            Assert.False(Junction.IsReparsePoint(_src));
            Assert.True(File.Exists(Path.Combine(_src, "a.dll")));
            Assert.False(Directory.Exists(_t.Sub(@"sib\App")));
        }
    }
}
