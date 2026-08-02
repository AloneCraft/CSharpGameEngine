using System.IO;
using CSharpGameEngine.VSExtension;
using Xunit;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class KnownFoldersTests
{
    [Fact]
    public void GetDownloadsFolder_ReturnsAnExistingDirectory()
    {
        string path = KnownFolders.GetDownloadsFolder();

        Assert.True(Directory.Exists(path), $"Resolved downloads folder does not exist: {path}");
    }
}
