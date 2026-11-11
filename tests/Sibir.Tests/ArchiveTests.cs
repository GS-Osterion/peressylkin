using System;
using System.IO;
using Sibir.Core;

namespace Sibir.Tests
{
    public class ArchiveTests
    {
        private static ArchiveRecord Rec(string id, string src, string status) =>
            new ArchiveRecord { Id = id, Operation = "exile", Source = src, Target = @"T:\Сибирь\x", Bytes = 10, Status = status, TimeUtc = DateTime.UtcNow };

        [Fact]
        public void Archive_LastStateWins()
        {
            using var t = new TempDir();
            var a = new Archive(t.Sub(@"sub\archive.jsonl"));
            a.Write(Rec("id1", @"C:\a", "in_transit"));
            a.Write(Rec("id1", @"C:\a", "exiled"));
            var cur = a.Current();
            Assert.Equal(1, cur.Count);
            Assert.Equal("exiled", cur[0].Status);
        }

        [Fact]
        public void Archive_FindExiled_CaseInsensitive()
        {
            using var t = new TempDir();
            var a = new Archive(t.Sub("a.jsonl"));
            a.Write(Rec("1", @"C:\Users\X\Avorion", "exiled"));
            Assert.NotNull(a.FindExiled(@"c:\users\x\AVORION\"));
            Assert.Null(a.FindExiled(@"C:\Users\X\Other"));
        }

        [Fact]
        public void Archive_Unfinished()
        {
            using var t = new TempDir();
            var a = new Archive(t.Sub("a.jsonl"));
            a.Write(Rec("1", @"C:\a", "in_transit"));
            a.Write(Rec("2", @"C:\b", "rehabilitating"));
            a.Write(Rec("3", @"C:\c", "exiled"));
            Assert.Equal(2, a.Unfinished().Count);
        }

        [Fact]
        public void Archive_SurvivesCorruptLine()
        {
            using var t = new TempDir();
            var p = t.Sub("a.jsonl");
            var a = new Archive(p);
            a.Write(Rec("1", @"C:\a", "exiled"));
            File.AppendAllText(p, "{\"Id\":\"x\"\n");
            a.Write(Rec("2", @"C:\b", "exiled"));
            Assert.Equal(2, a.Current().Count);
        }

        [Fact]
        public void Archive_CyrillicPaths_RoundTrip()
        {
            using var t = new TempDir();
            var a = new Archive(t.Sub("a.jsonl"));
            a.Write(Rec("1", @"C:\Игры\Сохранения 1", "exiled"));
            Assert.Equal(@"C:\Игры\Сохранения 1", new Archive(t.Sub("a.jsonl")).Current()[0].Source);
        }
    }
}
