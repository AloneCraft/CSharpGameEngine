using System.Diagnostics;
using System.Text.Json;
namespace CSharpGameEngine.CodeAnalysis.Tests;
public sealed class NumericProfileOutputTests {
    [Fact]
    public async Task SdkProfileLibraryPathsStayIndependentOfGameTargetAndDepthWithoutExhaustingLegacyPathLimit(){
        var Root=new DirectoryInfo(AppContext.BaseDirectory);
        while(Root is not null&&!File.Exists(Path.Combine(Root.FullName,"CSharpGameEngine.slnx"))){ Root=Root.Parent; }
        Assert.NotNull(Root);
        var DirectoryPath=Path.Combine(Path.GetTempPath(),"CgeSdkPaths-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
        try{
            var Project=Path.Combine(DirectoryPath,"Game.proj");
            var NumericProps=Path.Combine(Root!.FullName,"build","CSharpGameEngine.NumericProfile.props");
            var SdkTargets=Path.Combine(Root.FullName,"packaging","CSharpGameEngine.Sdk","build","CSharpGameEngine.Sdk.NumericProfiles.targets");
            File.WriteAllText(Project,$"<Project><PropertyGroup><Configuration>Debug</Configuration><CgeNumericProfileLateImport>true</CgeNumericProfileLateImport><BaseIntermediateOutputPath>obj\\</BaseIntermediateOutputPath><IntermediateOutputPath>obj\\Debug\\net10.0-windows10.0.19041.0\\win-x64\\$(CgeNumericMode)\\$(CgeDepthMapping)\\</IntermediateOutputPath></PropertyGroup><Import Project=\"{NumericProps}\"/><Import Project=\"{SdkTargets}\"/><ItemGroup><Reference Include=\"CSharpGameEngine.Numerics\" HintPath=\"default-numerics.dll\"/><Reference Include=\"CSharpGameEngine.World\" HintPath=\"default-world.dll\"/><Reference Include=\"OtherLibrary\" HintPath=\"other.dll\"/></ItemGroup><Target Name=\"ResolveAssemblyReferences\"/></Project>");
            var Paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var Mode in new[]{"Float","Double"}){
                string? Previous=null;
                foreach(var Depth in new[]{"Identity","Logarithmic"}){
                    var Start=new ProcessStartInfo("dotnet"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true};
                    foreach(var Argument in new[]{"msbuild",Project,"-nologo","-getProperty:CgeSdkProfileDirectory","-p:CgeNumericMode="+Mode,"-p:CgeDepthMapping="+Depth}){ Start.ArgumentList.Add(Argument); }
                    using var Process=System.Diagnostics.Process.Start(Start)!;
                    var Output=Process.StandardOutput.ReadToEndAsync();var Errors=Process.StandardError.ReadToEndAsync();
                    using var Timeout=new CancellationTokenSource(TimeSpan.FromSeconds(45));
                    try{ await Process.WaitForExitAsync(Timeout.Token); }finally{ if(!Process.HasExited){ Process.Kill(entireProcessTree:true); } }
                    Assert.True(Process.ExitCode==0,await Errors+await Output);
                    var Value=(await Output).Trim();
                    Assert.StartsWith(DirectoryPath,Value,StringComparison.OrdinalIgnoreCase);
                    var CompilerPath=Path.Combine(Value,"CSharpGameEngine.Numerics","obj","Debug",Mode=="Float"?"float32":"float64","net10.0","CSharpGameEngine.Numerics.GeneratedMSBuildEditorConfig.editorconfig");
                    Assert.True(CompilerPath.Length<260,CompilerPath);
                    if(Previous is null){ Assert.True(Paths.Add(Value)); }else{ Assert.Equal(Previous,Value); }
                    Previous=Value;
                }
                var Expected=new[]{"CSharpGameEngine.Numerics","CSharpGameEngine.World"}.ToDictionary(Name=>Name,Name=>Path.Combine(Previous!,Name,"bin","Debug",Mode=="Float"?"float32":"float64","net10.0",Name+".dll"));
                foreach(var PathValue in Expected.Values){ Directory.CreateDirectory(Path.GetDirectoryName(PathValue)!);File.WriteAllText(PathValue,"reference selection fixture"); }
                var DesignStart=new ProcessStartInfo("dotnet"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true};
                foreach(var Argument in new[]{"msbuild",Project,"-nologo","-t:ResolveAssemblyReferences","-getItem:Reference","-p:DesignTimeBuild=true","-p:CgeNumericMode="+Mode}){ DesignStart.ArgumentList.Add(Argument); }
                using var DesignProcess=System.Diagnostics.Process.Start(DesignStart)!;
                var DesignOutput=DesignProcess.StandardOutput.ReadToEndAsync();var DesignErrors=DesignProcess.StandardError.ReadToEndAsync();
                using var DesignTimeout=new CancellationTokenSource(TimeSpan.FromSeconds(45));
                try{ await DesignProcess.WaitForExitAsync(DesignTimeout.Token); }finally{ if(!DesignProcess.HasExited){ DesignProcess.Kill(entireProcessTree:true); } }
                Assert.True(DesignProcess.ExitCode==0,await DesignErrors+await DesignOutput);
                using var References=JsonDocument.Parse(await DesignOutput);
                var Items=References.RootElement.GetProperty("Items").GetProperty("Reference").EnumerateArray().ToArray();
                foreach(var Pair in Expected){ Assert.Equal(Pair.Value,Assert.Single(Items,Item=>Item.GetProperty("Identity").GetString()==Pair.Key).GetProperty("HintPath").GetString()); }
                Assert.Contains(Items,Item=>Item.GetProperty("Identity").GetString()=="OtherLibrary");
            }
        }finally{ Directory.Delete(DirectoryPath,true); }
    }
    [Theory]
    [InlineData("src/CSharpGameEngine.World/CSharpGameEngine.World.csproj",false)]
    [InlineData("UserEcsProject/UserEcsProject.csproj",true)]
    public async Task SelectedProfilesKeepFinalAssembliesReferencesAndIntermediateFilesSeparate(string Project,bool IncludeDepthMapping){
        var Root=new DirectoryInfo(AppContext.BaseDirectory);
        while(Root is not null&&!File.Exists(Path.Combine(Root.FullName,"CSharpGameEngine.slnx"))){ Root=Root.Parent; }
        Assert.NotNull(Root);
        var Paths=new Dictionary<string,HashSet<string>>{
            ["TargetPath"]=new(StringComparer.OrdinalIgnoreCase),["TargetRefPath"]=new(StringComparer.OrdinalIgnoreCase),
            ["IntermediateOutputPath"]=new(StringComparer.OrdinalIgnoreCase),["OutDir"]=new(StringComparer.OrdinalIgnoreCase)};
        var Numeric=new[]{Array.Empty<string>(),["-p:CgeNumericMode=Float"],["-p:CgeNumericMode=Double"],["-p:CgeFixedBits=64","-p:CgeFractionalBits=32"],["-p:CgeFixedBits=192","-p:CgeFractionalBits=96"]};
        var Profiles=IncludeDepthMapping?Numeric.Concat(Numeric.Select(static Settings=>Settings.Append("-p:CgeDepthMapping=Identity").ToArray())):Numeric;
        foreach(var Settings in Profiles){
            var Start=new ProcessStartInfo("dotnet"){WorkingDirectory=Root!.FullName,RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true};
            foreach(var Argument in new[]{"msbuild",Project,"-nologo","-getProperty:TargetPath,TargetRefPath,IntermediateOutputPath,OutDir","-p:Configuration=Debug"}.Concat(Settings)){ Start.ArgumentList.Add(Argument); }
            var Japanese=Settings.Contains("-p:CgeDepthMapping=Identity",StringComparer.Ordinal);
            if(IncludeDepthMapping){ Start.ArgumentList.Add("-getItem:PropertyPageSchema");Start.ArgumentList.Add("-p:VSLang="+(Japanese?"1041":"1033")); }
            using var Process=System.Diagnostics.Process.Start(Start)!;
            var Output=Process.StandardOutput.ReadToEndAsync();var Errors=Process.StandardError.ReadToEndAsync();
            using var Timeout=new CancellationTokenSource(TimeSpan.FromSeconds(45));
            try{ await Process.WaitForExitAsync(Timeout.Token); }finally{ if(!Process.HasExited){ Process.Kill(entireProcessTree:true); } }
            Assert.True(Process.ExitCode==0,await Errors+await Output);
            using var Document=JsonDocument.Parse(await Output);
            var Properties=Document.RootElement.GetProperty("Properties");
            if(IncludeDepthMapping){
                var Rules=Document.RootElement.GetProperty("Items").GetProperty("PropertyPageSchema").EnumerateArray()
                    .Where(static Item=>Path.GetFileName(Item.GetProperty("Identity").GetString())=="CgeRuntime.xaml").ToArray();
                var Rule=Assert.Single(Rules);Assert.Equal("Project",Rule.GetProperty("Context").GetString());
                var RulePath=Rule.GetProperty("Identity").GetString()!;Assert.True(File.Exists(RulePath));
                Assert.Equal(Japanese,RulePath.Contains("ja-JP",StringComparison.Ordinal));
            }
            foreach(var Property in Paths){
                var Value=Properties.GetProperty(Property.Key).GetString();Assert.False(string.IsNullOrWhiteSpace(Value));
                Assert.True(Property.Value.Add(Path.GetFullPath(Value!,Path.GetDirectoryName(Path.Combine(Root.FullName,Project))!)),Property.Key+" collides with another profile: "+Value);
            }
        }
    }
}
