using System;
using System.IO;
using CSharpGameEngine.VSExtension;
using Xunit;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// 実際のブリッジプロジェクトの <c>bin/</c> 出力に似せた一時フォルダーを使い、<see cref="BridgeExecutableLocator"/> のパス選択ロジックを直接検証する（ADR-0010 追補：<c>dotnet run --project</c> ではなく、ビルド済み apphost <c>.exe</c> を起動する）。実際の dotnet/MSBuild プロセスは起動しないため、高速かつ決定論的に実行できる。
/// </summary>
public sealed class BridgeExecutableLocatorTests:IDisposable {
    private const string BridgeProjectRelativePath=@"tools\Sample.Bridge.Cli";
    private readonly string RepoRoot;

    public BridgeExecutableLocatorTests(){
        this.RepoRoot=Path.Combine(Path.GetTempPath(),"csge-bridgelocator-tests-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.RepoRoot);
    }

    public void Dispose(){
        try {
            Directory.Delete(this.RepoRoot,recursive:true);
        } catch(IOException){
        }
    }

    [Fact]
    public void BundledBridge_WinsOverARepositoryBuild(){
        this.CreateExe("Release",RidSubfolder:null,DateTime.UtcNow);
        var Extension=Path.Combine(this.RepoRoot,"extension");
        var Bundled=Path.Combine(Extension,"Bridges","Sample.Bridge.Cli","Sample.Bridge.Cli.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(Bundled)!);
        File.WriteAllText(Bundled,"apphost");
        File.WriteAllText(Path.ChangeExtension(Bundled,".runtimeconfig.json"),"{}");

        var Result=BridgeExecutableLocator.FindExecutable(Extension,this.RepoRoot,BridgeProjectRelativePath);

        Assert.Equal(Bundled,Result);
    }

    [Fact]
    public void NoBuiltExecutable_ReturnsNull(){
        var Result=BridgeExecutableLocator.FindExecutable(this.RepoRoot,BridgeProjectRelativePath);

        Assert.Null(Result);
    }

    [Fact]
    public void AppHostWithoutRuntimeConfig_ReturnsNull(){
        var DirectoryPath=Path.Combine(this.RepoRoot,BridgeProjectRelativePath,"bin","Debug","net10.0");
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(Path.Combine(DirectoryPath,"Sample.Bridge.Cli.exe"),"stale apphost");

        var Result=BridgeExecutableLocator.FindExecutable(this.RepoRoot,BridgeProjectRelativePath);

        Assert.Null(Result);
    }

    [Fact]
    public void DebugBuildOnly_IsFound(){
        var ExePath= this.CreateExe("Debug",RidSubfolder:null,DateTime.UtcNow);

        var Result=BridgeExecutableLocator.FindExecutable(this.RepoRoot,BridgeProjectRelativePath);

        Assert.Equal(ExePath,Result);
    }

    [Fact]
    public void RuntimeIdentifierSubfolder_IsFound(){
        var ExePath= this.CreateExe("Debug","win-x64",DateTime.UtcNow);

        var Result=BridgeExecutableLocator.FindExecutable(this.RepoRoot,BridgeProjectRelativePath);

        Assert.Equal(ExePath,Result);
    }

    [Fact]
    public void MultipleCandidates_PicksNewestByWriteTime_EvenWhenReleaseIsNewer(){
        var Now=DateTime.UtcNow;
        this.CreateExe("Debug",RidSubfolder:null,Now);
        var NewerReleaseExePath= this.CreateExe("Release",RidSubfolder:null,Now.AddMinutes(5));

        var Result=BridgeExecutableLocator.FindExecutable(this.RepoRoot,BridgeProjectRelativePath);

        Assert.Equal(NewerReleaseExePath,Result);
    }

    [Fact]
    public void MultipleCandidates_PicksNewestByWriteTime_EvenWhenDebugIsNewer(){
        var Now=DateTime.UtcNow;
        this.CreateExe("Release",RidSubfolder:null,Now);
        var NewerDebugExePath= this.CreateExe("Debug",RidSubfolder:null,Now.AddMinutes(5));

        var Result=BridgeExecutableLocator.FindExecutable(this.RepoRoot,BridgeProjectRelativePath);

        Assert.Equal(NewerDebugExePath,Result);
    }

    private string CreateExe(string Configuration,string?RidSubfolder,DateTime WriteTimeUtc){
        var Directory=RidSubfolder is null
            ?Path.Combine(this.RepoRoot,BridgeProjectRelativePath,"bin",Configuration,"net10.0")
            :Path.Combine(this.RepoRoot,BridgeProjectRelativePath,"bin",Configuration,"net10.0",RidSubfolder);
        System.IO.Directory.CreateDirectory(Directory);

        var ExePath=Path.Combine(Directory,"Sample.Bridge.Cli.exe");
        File.WriteAllText(ExePath,"fake exe bytes");
        File.WriteAllText(Path.Combine(Directory,"Sample.Bridge.Cli.runtimeconfig.json"),"{}");
        File.SetLastWriteTimeUtc(ExePath,WriteTimeUtc);

        return ExePath;
    }
}
