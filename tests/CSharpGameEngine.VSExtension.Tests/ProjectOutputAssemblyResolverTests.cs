using System;
using System.IO;
using CSharpGameEngine.VSExtension;
using Xunit;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// Exercises <see cref="ProjectOutputAssemblyResolver"/>'s pure path-combination and glob-fallback
/// logic directly (mirrors <see cref="BridgeExecutableLocatorTests"/>'s style) - never touches
/// <see cref="EnvDTE.Project"/>, which requires a running VS host (<see cref="ProjectOutputAssemblyResolver.TryResolve"/>
/// itself is not covered here for that reason).
/// </summary>
public sealed class ProjectOutputAssemblyResolverTests:IDisposable {
    private readonly string ProjectDirectory;

    public ProjectOutputAssemblyResolverTests(){
        ProjectDirectory=Path.Combine(Path.GetTempPath(),"csge-outputresolver-tests-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ProjectDirectory);
    }

    public void Dispose(){
        try {
            Directory.Delete(ProjectDirectory,recursive:true);
        } catch(IOException){
        }
    }

    [Fact]
    public void CombineOutputPath_JoinsProjectDirectoryOutputPathAndFileName(){
        var Result=ProjectOutputAssemblyResolver.CombineOutputPath(@"G:\repo\UserGameProject",@"bin\Debug\net10.0-windows10.0.19041.0\win-x64\","UserGameProject.dll");

        Assert.Equal(Path.GetFullPath(@"G:\repo\UserGameProject\bin\Debug\net10.0-windows10.0.19041.0\win-x64\UserGameProject.dll"),Result);
    }

    [Fact]
    public void FindNewestByGlob_NoBuiltAssembly_ReturnsNull(){
        var Result=ProjectOutputAssemblyResolver.FindNewestByGlob(ProjectDirectory,"Sample.dll");

        Assert.Null(Result);
    }

    [Fact]
    public void FindNewestByGlob_SingleCandidate_IsFound(){
        var Expected=CreateAssembly("Debug","net10.0",DateTime.UtcNow);

        var Result=ProjectOutputAssemblyResolver.FindNewestByGlob(ProjectDirectory,"Sample.dll");

        Assert.Equal(Expected,Result);
    }

    [Fact]
    public void FindNewestByGlob_MultipleCandidates_PicksNewestByWriteTime(){
        DateTime Now=DateTime.UtcNow;
        CreateAssembly("Debug","net10.0",Now);
        var Newest=CreateAssembly("Debug","net10.0-windows10.0.19041.0\\win-x64",Now.AddMinutes(5));

        var Result=ProjectOutputAssemblyResolver.FindNewestByGlob(ProjectDirectory,"Sample.dll");

        Assert.Equal(Newest,Result);
    }

    private string CreateAssembly(string Configuration,string TfmSubpath,DateTime WriteTimeUtc){
        var directory=Path.Combine(ProjectDirectory,"bin",Configuration,TfmSubpath);
        Directory.CreateDirectory(directory);

        var AssemblyPath=Path.Combine(directory,"Sample.dll");
        File.WriteAllText(AssemblyPath,"fake dll bytes");
        File.SetLastWriteTimeUtc(AssemblyPath,WriteTimeUtc);

        return AssemblyPath;
    }
}
