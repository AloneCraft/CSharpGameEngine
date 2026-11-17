using System.Diagnostics;
using Xunit;
namespace CSharpGameEngine.Core.Tests;
/// <summary>
/// ゲームのビルドは tools/Cge と tools/Cook.Cli を起動し、その間に別のビルド（VS と dotnet、テスト、VS 拡張）が同じ道具を作り直すことがある。
/// VS の NuGet と dotnet の NuGet は同じ復元の入力から違う判定値を出すため、交互に復元するたびに project.assets.json が同じ中身で書き直され、
/// SDK は次のビルドで runtimeconfig.json と deps.json を書き直す。その途中で道具を起動すると 0x80008093 で落ちた（TopDown の距離場で観測）。
/// build/CSharpGameEngine.BuildTool.targets は、中身が変わらなければ出力の 2 つを書き直さない。
/// </summary>
public sealed class BuildToolRuntimeFilesTests {
    private static readonly string RepositoryRoot=FindRepositoryRoot();
    [Fact]
    [Trait("Category","Build")]
    public void ANewerRestoreLeavesTheToolsRuntimeFilesUntouchedAndKeepsTheApphost(){
        var Directory=Path.Combine(Path.GetTempPath(),"CgeBuildTool-"+Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Directory);
        try {
            var Targets=Path.Combine(RepositoryRoot,"build","CSharpGameEngine.BuildTool.targets");
            File.WriteAllText(Path.Combine(Directory,"Tool.csproj"),
                $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup>
                  <Import Project="{Targets}" />
                </Project>
                """);
            File.WriteAllText(Path.Combine(Directory,"Program.cs"),"return 0;");
            Build(Directory);
            var Output=Path.Combine(Directory,"bin","Debug","net10.0");
            string[]RuntimeFiles=[Path.Combine(Output,"Tool.runtimeconfig.json"),Path.Combine(Output,"Tool.deps.json")];
            Assert.True(File.Exists(Path.Combine(Output,"Tool.exe")),"The apphost must still be made.");
            var Before=RuntimeFiles.Select(File.GetLastWriteTimeUtc).ToArray();
            // The other NuGet's restore: the same content, a newer file.
            File.SetLastWriteTimeUtc(Path.Combine(Directory,"obj","project.assets.json"),DateTime.UtcNow.AddMinutes(1));
            Build(Directory);
            Assert.Equal(Before,RuntimeFiles.Select(File.GetLastWriteTimeUtc).ToArray());
            Assert.True(File.Exists(Path.Combine(Directory,"obj","Debug","net10.0","runtime-files","Tool.runtimeconfig.json")));
        } finally {
            try { System.IO.Directory.Delete(Directory,true); } catch(IOException){ } catch(UnauthorizedAccessException){ }
        }
    }
    private static void Build(string Directory){
        var Start=new ProcessStartInfo("dotnet"){WorkingDirectory=Directory,RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true};
        foreach(var Argument in (string[])["build","Tool.csproj","-nologo","-v:q"]){ Start.ArgumentList.Add(Argument); }
        using var Process=System.Diagnostics.Process.Start(Start)!;
        var Out=Process.StandardOutput.ReadToEndAsync();var Error=Process.StandardError.ReadToEndAsync();
        Process.WaitForExit();
        Assert.True(Process.ExitCode==0,Out.Result+Error.Result);
    }
    private static string FindRepositoryRoot(){
        var Current=new DirectoryInfo(AppContext.BaseDirectory);
        while(Current is not null){
            if(File.Exists(Path.Combine(Current.FullName,"CSharpGameEngine.slnx"))){ return Current.FullName; }
            Current=Current.Parent;
        }
        throw new DirectoryNotFoundException("CSharpGameEngine.slnxのルートが見つかりません。");
    }
}
