using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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

        [Fact]
        public void Archive_Remove_DropsAllLinesOfCase()
        {
            using var t = new TempDir();
            var p = t.Sub("a.jsonl");
            var a = new Archive(p);
            a.Write(Rec("id1", @"C:\a", "in_transit"));
            a.Write(Rec("id1", @"C:\a", "exiled"));
            a.Write(Rec("id2", @"C:\b", "exiled"));
            a.Remove(new HashSet<string> { "id1" });
            Assert.Equal(1, a.Current().Count);
            Assert.Equal("id2", a.Current()[0].Id);
            Assert.False(File.ReadAllText(p).Contains("id1"), "no trace of id1");
            Assert.Equal(1, Directory.GetFiles(t.Path).Length, "no tmp/bak files: ");
        }

        [Fact]
        public void Archive_Remove_KeepsLinesWrittenConcurrently()
        {
            using var t = new TempDir();
            var a = new Archive(t.Sub("a.jsonl"));
            a.Write(Rec("victim", @"C:\v", "rehabilitated"));
            var writers = Enumerable.Range(0, 4).Select(w => Task.Run(() =>
            {
                for (int i = 0; i < 50; i++) new Archive(a.FilePath).Write(Rec(w + "-" + i, @"C:\x" + i, "exiled"));
            })).ToArray();
            for (int i = 0; i < 20; i++) a.Remove(new HashSet<string> { "victim" });
            Task.WaitAll(writers);
            Assert.Equal(200, a.Current().Count);
        }

        [Fact]
        public void Archive_Remove_WhileReaderOpen()
        {
            using var t = new TempDir();
            var a = new Archive(t.Sub("a.jsonl"));
            a.Write(Rec("1", @"C:\a", "rehabilitated"));
            a.Write(Rec("2", @"C:\b", "exiled"));
            using (new FileStream(a.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                a.Remove(new HashSet<string> { "1" });
            Assert.Equal(1, a.Current().Count);
        }

        [Fact]
        public void Settings_Load_MigratesLastRootIntoSiberiaRoots()
        {
            using var t = new TempDir();
            var p = t.Sub("settings.json");
            File.WriteAllText(p, "{\"LastRoot\":\"E:\\\\Stuff\\\\Сибирь\"}");
            Assert.Contains(@"E:\Stuff\Сибирь", Settings.Load(p).SiberiaRoots);
        }

        [Fact]
        public void Archive_StampRemoved_RoundTrips()
        {
            using var t = new TempDir();
            var a = new Archive(t.Sub("a.jsonl"));
            var r = Rec("1", @"C:\a", "exiled"); r.StampRemoved = true;
            a.Write(r);
            Assert.True(a.Current()[0].StampRemoved);
        }
    }
}
