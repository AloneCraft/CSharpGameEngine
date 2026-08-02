using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using CSharpGameEngine.VSExtension;
using Xunit;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// Exercises the Fab drop-zone copy logic directly against temp folders instead of the real OS
/// Downloads folder (ADR-0011) - live VS UI automation proved unreliable for verifying this in a
/// running Exp instance, but the watcher itself has no VS dependency, so it's fully testable here.
/// </summary>
public sealed class FabDownloadWatcherTests : IDisposable
{
    private readonly string _watchedFolder;
    private readonly string _destinationFolder;

    public FabDownloadWatcherTests()
    {
        string root = Path.Combine(Path.GetTempPath(), "csge-fabwatch-tests-" + Guid.NewGuid().ToString("N"));
        _watchedFolder = Path.Combine(root, "Downloads");
        _destinationFolder = Path.Combine(root, "Assets", "Fab");
        Directory.CreateDirectory(_watchedFolder);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path.GetDirectoryName(_watchedFolder)!, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void MatchingExtension_GetsCopiedToDestination()
    {
        List<string> copied = [];
        using FabDownloadWatcher watcher = new(_watchedFolder, _destinationFolder, copied.Add, _ => { });
        watcher.Start();

        string sourcePath = Path.Combine(_watchedFolder, "RockProp.glb");
        File.WriteAllText(sourcePath, "fake glb bytes");

        string? destinationPath = WaitFor(() => copied.Count > 0 ? copied[0] : null, TimeSpan.FromSeconds(10));

        Assert.NotNull(destinationPath);
        Assert.Equal(Path.Combine(_destinationFolder, "RockProp.glb"), destinationPath);
        Assert.True(File.Exists(destinationPath));
        Assert.Equal("fake glb bytes", File.ReadAllText(destinationPath!));
    }

    [Fact]
    public void NonMatchingExtension_IsIgnored()
    {
        List<string> copied = [];
        List<string> errors = [];
        using FabDownloadWatcher watcher = new(_watchedFolder, _destinationFolder, copied.Add, errors.Add);
        watcher.Start();

        File.WriteAllText(Path.Combine(_watchedFolder, "installer.exe"), "not a fab asset");

        // Give the watcher a fair chance to (incorrectly) react before asserting nothing happened.
        Thread.Sleep(1000);

        Assert.Empty(copied);
        Assert.Empty(errors);
    }

    [Fact]
    public void Stop_PreventsFurtherCopies()
    {
        List<string> copied = [];
        using FabDownloadWatcher watcher = new(_watchedFolder, _destinationFolder, copied.Add, _ => { });
        watcher.Start();
        watcher.Stop();

        File.WriteAllText(Path.Combine(_watchedFolder, "AfterStop.glb"), "should not be copied");

        Thread.Sleep(1000);

        Assert.Empty(copied);
    }

    [Fact]
    public void DuplicateFileName_GetsUniqueSuffix()
    {
        Directory.CreateDirectory(_destinationFolder);
        File.WriteAllText(Path.Combine(_destinationFolder, "Prop.glb"), "existing file - must not be overwritten");

        List<string> copied = [];
        using FabDownloadWatcher watcher = new(_watchedFolder, _destinationFolder, copied.Add, _ => { });
        watcher.Start();

        File.WriteAllText(Path.Combine(_watchedFolder, "Prop.glb"), "newly downloaded file");

        string? destinationPath = WaitFor(() => copied.Count > 0 ? copied[0] : null, TimeSpan.FromSeconds(10));

        Assert.NotNull(destinationPath);
        Assert.NotEqual(Path.Combine(_destinationFolder, "Prop.glb"), destinationPath);
        Assert.Equal("existing file - must not be overwritten", File.ReadAllText(Path.Combine(_destinationFolder, "Prop.glb")));
        Assert.Equal("newly downloaded file", File.ReadAllText(destinationPath!));
    }

    private static string? WaitFor(Func<string?> getValue, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            string? value = getValue();
            if (value is not null)
            {
                return value;
            }

            Thread.Sleep(100);
        }

        return null;
    }
}
