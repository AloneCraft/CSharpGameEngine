using System.Xml.Linq;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// Visual Studio はデザイン時ビルド (CompileDesignTime) をバックグラウンドで実行する。Pass2 を行わずに MarkupCompilePass1 を実行するため、BAML、パッケージ、配置は生成されない。そこでビルド出力を確認すると誤検知となって Error List に表示される。WPF BAML チェックが毎回の F5 で「resource generation is incomplete」と報告していたのはそのためである。
/// </summary>
public sealed class VsixBuildTargetTests {
    [Fact]
    public void ProjectTargetsThatFailTheBuild_NeverRunInDesignTimeBuilds(){
        XDocument Project=XDocument.Load(Path.Combine(RepositoryRoot(),"tools","CSharpGameEngine.VSExtension","CSharpGameEngine.VSExtension.csproj"));
        var Failing=Project.Descendants("Target")
            .Where(static Target=>Target.Descendants("Error").Any())
            .ToList();

        Assert.NotEmpty(Failing);
        Assert.All(Failing,static Target=>Assert.True(
            ((string?)Target.Attribute("Condition")??string.Empty).Replace(" ",string.Empty).Contains("'$(DesignTimeBuild)'!='true'"),
            $"Target {(string?)Target.Attribute("Name")} can fail a design-time build."));
    }

    private static string RepositoryRoot(){
        DirectoryInfo?Directory=new(AppContext.BaseDirectory);
        while(Directory is not null&&!File.Exists(Path.Combine(Directory.FullName,"CSharpGameEngine.slnx"))){ Directory=Directory.Parent; }
        return Directory?.FullName??throw new DirectoryNotFoundException();
    }
}
