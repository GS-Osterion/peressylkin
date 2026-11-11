namespace Sibir.Tests
{
    public class StringsTests
    {
        [Fact]
        public void Size_FormatsRussian()
        {
            Assert.Equal("12,3 ГБ", Strings.Size(13_207_024_435));
            Assert.Equal("850 МБ", Strings.Size(891_289_600));
            Assert.Equal("0 МБ", Strings.Size(0));
        }
    }
}
