using System.IO;
using Sibir.Native;

namespace Sibir.Tests
{
    public class JunctionTests
    {
        [Fact]
        public void Junction_CreateReadDelete()
        {
            using var t = new TempDir();
            var target = t.Sub("target"); var link = t.Sub("link");
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "a.txt"), "привет");
            Junction.Create(link, target);
            Assert.Equal("привет", File.ReadAllText(Path.Combine(link, "a.txt")));
            Assert.True(Junction.IsReparsePoint(link));
            Assert.False(Junction.IsReparsePoint(target));
            Assert.Equal(LongPath.Normalize(target), Junction.GetTarget(link));
            File.WriteAllText(Path.Combine(link, "b.txt"), "b");
            Assert.True(File.Exists(Path.Combine(target, "b.txt")));
            Junction.Delete(link);
            Assert.False(Directory.Exists(link));
            Assert.True(File.Exists(Path.Combine(target, "a.txt")));
            Assert.True(File.Exists(Path.Combine(target, "b.txt")));
        }
    }

    public class LongPathTests
    {
        [Fact]
        public void LongPath_Normalize()
        {
            Assert.Equal(@"c:\X", LongPath.Normalize(@"c:\X\"));
            Assert.Equal(@"C:\", LongPath.Normalize(@"C:\"));
            Assert.True(LongPath.SameOrUnder(@"C:\a\B\c", @"c:\A\b"));
            Assert.True(LongPath.SameOrUnder(@"C:\a", @"c:\A\"));
            Assert.False(LongPath.SameOrUnder(@"C:\ab", @"C:\a"));
        }

        [Fact]
        public void LongPath_Ex()
        {
            using var t = new TempDir();
            var deep = t.Path;
            while (deep.Length < 300) deep = Path.Combine(deep, "очень_длинное_имя_папки");
            Assert.True(LongPath.Ex(deep).StartsWith(@"\\?\"));
            Assert.Equal(@"\\?\UNC\srv\share\x", LongPath.Ex(@"\\srv\share\x"));
            Directory.CreateDirectory(LongPath.Ex(deep));
            File.WriteAllText(LongPath.Ex(Path.Combine(deep, "f.txt")), "x");
            Assert.True(File.Exists(LongPath.Ex(Path.Combine(deep, "f.txt"))));
        }
    }
}
