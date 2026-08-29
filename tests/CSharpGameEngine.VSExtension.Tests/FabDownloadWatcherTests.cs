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
[Collection(FileSystemWatcherTestCollection.Name)]
public sealed class FabDownloadWatcherTests:IDisposable {
    private readonly string WatchedFolder;
    private readonly string DestinationFolder;

    public FabDownloadWatcherTests(){
        var Root=Path.Combine(Path.GetTempPath(),"csge-fabwatch-tests-"+Guid.NewGuid().ToString("N"));
        this.WatchedFolder=Path.Combine(Root,"Downloads");
        this.DestinationFolder=Path.Combine(Root,"Assets","Fab");
        Directory.CreateDirectory(this.WatchedFolder);
    }

    public void Dispose(){
        try {
            Directory.Delete(Path.GetDirectoryName(this.WatchedFolder)!,recursive:true);
        } catch(IOException){
        }
    }

    [Fact]
    public void MatchingExtension_GetsCopiedToDestination(){
        List<string>Copied=[];
        using FabDownloadWatcher Watcher=new(this.WatchedFolder, this.DestinationFolder,Copied.Add,_=>{ });
        Watcher.Start();

        var SourcePath=Path.Combine(this.WatchedFolder,"RockProp.glb");
        File.WriteAllText(SourcePath,"fake glb bytes");

        var DestinationPath=WaitFor(()=>Copied.Count>0?Copied[0]:null,TimeSpan.FromSeconds(10));

        Assert.NotNull(DestinationPath);
        Assert.Equal(Path.Combine(this.DestinationFolder,"RockProp.glb"),DestinationPath);
        Assert.True(File.Exists(DestinationPath));
        Assert.Equal("fake glb bytes",File.ReadAllText(DestinationPath!));
    }

    [Fact]
    public void NonMatchingExtension_IsIgnored(){
        List<string>Copied=[];
        List<string>Errors=[];
        using FabDownloadWatcher Watcher=new(this.WatchedFolder, this.DestinationFolder,Copied.Add,Errors.Add);
        Watcher.Start();

        File.WriteAllText(Path.Combine(this.WatchedFolder,"installer.exe"),"not a fab asset");

        // Give the watcher a fair chance to (incorrectly) react before asserting nothing happened.
        Thread.Sleep(1000);

        Assert.Empty(Copied);
        Assert.Empty(Errors);
    }

    [Fact]
    public void Stop_PreventsFurtherCopies(){
        List<string>Copied=[];
        using FabDownloadWatcher Watcher=new(this.WatchedFolder, this.DestinationFolder,Copied.Add,_=>{ });
        Watcher.Start();
        Watcher.Stop();

        File.WriteAllText(Path.Combine(this.WatchedFolder,"AfterStop.glb"),"should not be copied");

        Thread.Sleep(1000);

        Assert.Empty(Copied);
    }

    [Fact]
    public void DuplicateFileName_GetsUniqueSuffix(){
        Directory.CreateDirectory(this.DestinationFolder);
        File.WriteAllText(Path.Combine(this.DestinationFolder,"Prop.glb"),"existing file - must not be overwritten");

        List<string>Copied=[];
        using FabDownloadWatcher Watcher=new(this.WatchedFolder, this.DestinationFolder,Copied.Add,_=>{ });
        Watcher.Start();

        File.WriteAllText(Path.Combine(this.WatchedFolder,"Prop.glb"),"newly downloaded file");

        var DestinationPath=WaitFor(()=>Copied.Count>0?Copied[0]:null,TimeSpan.FromSeconds(10));

        Assert.NotNull(DestinationPath);
        Assert.NotEqual(Path.Combine(this.DestinationFolder,"Prop.glb"),DestinationPath);
        Assert.Equal("existing file - must not be overwritten",File.ReadAllText(Path.Combine(this.DestinationFolder,"Prop.glb")));
        Assert.Equal("newly downloaded file",File.ReadAllText(DestinationPath!));
    }

    private static string?WaitFor(Func<string?>GetValue,TimeSpan Timeout){
        var Deadline=DateTime.UtcNow+Timeout;
        while(DateTime.UtcNow<Deadline){
            var Value=GetValue();
            if(Value is not null){
                return Value;
            }

            Thread.Sleep(100);
        }

        return null;
    }
}
