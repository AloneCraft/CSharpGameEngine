using System.IO;
using CSharpGameEngine.VSExtension;
using Xunit;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class KnownFoldersTests {
    [Fact]
    public void GetDownloadsFolder_ReturnsAnExistingDirectory(){
        var Path=KnownFolders.GetDownloadsFolder();

        Assert.True(Directory.Exists(Path), $"Resolved downloads folder does not exist: {Path}");
    }
}
