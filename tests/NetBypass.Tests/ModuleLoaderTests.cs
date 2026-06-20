using NetBypass.Core.Services;
using Xunit;

namespace NetBypass.Tests;

public sealed class ModuleLoaderTests
{
    [Theory]
    [InlineData("0.0.0.0 example.com")]
    [InlineData("127.0.0.1 example.com")]
    [InlineData("192.168.1.10 example.com")]
    [InlineData("1.2.3.4 localhost")]
    public void LoadFile_RejectsLocalMappings(string entry)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, $"# id: test\n# name: Test\n# category: Test\n{entry}");
            Assert.Throws<FormatException>(() => new ModuleLoader().LoadFile(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
