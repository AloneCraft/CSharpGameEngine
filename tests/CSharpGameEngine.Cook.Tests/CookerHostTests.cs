using CSharpGameEngine.Cook.Cli;
using CSharpGameEngine.Cooking;

namespace CSharpGameEngine.Cook.Tests;

/// <summary>Cookers this test assembly offers when the host loads it as a Cook module.</summary>
public sealed class UpperCooker:IAssetCooker {
    public string Name=>"Upper";
    public IReadOnlyList<string> InputExtensions=>[".txt"];
    public CookResult Cook(CookRequest Request){
        var Name=Path.GetFileNameWithoutExtension(Request.InputPath);
        File.WriteAllText(Path.Combine(Request.OutputDirectory,Name+".out"),File.ReadAllText(Request.InputPath).ToUpperInvariant());
        Directory.CreateDirectory(Path.Combine(Request.OutputDirectory,"Sub"));
        File.WriteAllText(Path.Combine(Request.OutputDirectory,"Sub",Name+".len"),File.ReadAllText(Request.InputPath).Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return CookResult.Success(Name+".out",Path.Combine("Sub",Name+".len"));
    }
}

public sealed class ClockCooker:IAssetCooker {
    public string Name=>"Clock";
    public IReadOnlyList<string> InputExtensions=>[".clock"];
    public CookResult Cook(CookRequest Request){
        File.WriteAllText(Path.Combine(Request.OutputDirectory,"now.out"),Guid.NewGuid().ToString());
        return CookResult.Success("now.out");
    }
}

public sealed class EscapingCooker:IAssetCooker {
    public string Name=>"Escaping";
    public IReadOnlyList<string> InputExtensions=>[".escape"];
    public CookResult Cook(CookRequest Request){
        File.WriteAllText(Path.Combine(Request.OutputDirectory,"..","outside.out"),"x");
        return CookResult.Success(Path.Combine("..","outside.out"));
    }
}

public sealed class FailingCooker:IAssetCooker {
    public string Name=>"Failing";
    public IReadOnlyList<string> InputExtensions=>[".txt"];
    public CookResult Cook(CookRequest Request)=>CookResult.Failure("the input is broken");
}

/// <summary>ADR-0079: the Cook host loads a module in its own context, chooses a cooker and checks what it writes.</summary>
public sealed class CookerHostTests:IDisposable {
    private readonly string Root=Path.Combine(Path.GetTempPath(),"cge-cook-tests-"+Guid.NewGuid().ToString("N"));
    private static readonly string Module=typeof(UpperCooker).Assembly.Location;

    public CookerHostTests(){
        Directory.CreateDirectory(this.Root);
    }

    public void Dispose(){
        if(Directory.Exists(this.Root)){ Directory.Delete(this.Root,recursive:true); }
    }

    private string Input(string Name,string Text){
        var Path=System.IO.Path.Combine(this.Root,Name);
        File.WriteAllText(Path,Text);
        return Path;
    }

    [Fact]
    public void TheModulesCookers_AreFoundInNameOrder_AndAreTheHostsContractType(){
        using var Host=CookerHost.Load(Module);

        Assert.Equal(["Clock","Escaping","Failing","Upper"],Host.Cookers.Select(static Cooker=>Cooker.Name));
        // The module was loaded into its own context, but IAssetCooker is the host's: the cast in Load would have failed otherwise.
        Assert.NotSame(typeof(UpperCooker).Assembly,Host.Cookers[0].GetType().Assembly);
    }

    [Fact]
    public void ADeterministicCooker_WritesItsOutputsAndPassesVerification(){
        using var Host=CookerHost.Load(Module);
        var Output=Path.Combine(this.Root,"out");

        var Written=CookerHost.Cook(Host.Select(this.Input("level.txt","abc"),"Upper"),Path.Combine(this.Root,"level.txt"),Output,this.Root,Verify:true);

        Assert.Equal([Path.Combine("Sub","level.len"),"level.out"],Written);
        Assert.Equal("ABC",File.ReadAllText(Path.Combine(Output,"level.out")));
        Assert.Equal("3",File.ReadAllText(Path.Combine(Output,"Sub","level.len")));
    }

    [Fact]
    public void ANondeterministicCooker_FailsVerification_AndWritesNothing(){
        using var Host=CookerHost.Load(Module);
        var Output=Path.Combine(this.Root,"out");
        var Input=this.Input("a.clock","");

        var Error=Assert.Throws<InvalidOperationException>(()=>CookerHost.Cook(Host.Select(Input,null),Input,Output,this.Root,Verify:true));

        Assert.Contains("not deterministic",Error.Message);
        Assert.False(Directory.Exists(Output));
        // Without verification the same cooker runs (the check is the caller's choice).
        Assert.Equal(["now.out"],CookerHost.Cook(Host.Select(Input,null),Input,Output,this.Root,Verify:false));
    }

    [Fact]
    public void AnOutputOutsideTheOutputFolder_IsRejected(){
        using var Host=CookerHost.Load(Module);
        var Input=this.Input("a.escape","");

        var Error=Assert.Throws<InvalidOperationException>(()=>CookerHost.Cook(Host.Select(Input,null),Input,Path.Combine(this.Root,"out"),this.Root,Verify:false));

        Assert.Contains("outside its output folder",Error.Message);
    }

    [Fact]
    public void ACookerThatFails_ReportsItsMessages(){
        using var Host=CookerHost.Load(Module);
        var Input=this.Input("a.txt","x");

        var Error=Assert.Throws<InvalidOperationException>(()=>CookerHost.Cook(Host.Select(Input,"Failing"),Input,Path.Combine(this.Root,"out"),this.Root,Verify:false));

        Assert.Contains("the input is broken",Error.Message);
    }

    [Fact]
    public void Selection_ByExtensionNeedsExactlyOneCooker_AndByNameNeedsAnExistingOne(){
        using var Host=CookerHost.Load(Module);

        Assert.Equal("Clock",Host.Select("x.CLOCK",null).Name);
        Assert.Contains("Several cookers",Assert.Throws<InvalidOperationException>(()=>Host.Select("x.txt",null)).Message);
        Assert.Contains("No cooker",Assert.Throws<InvalidOperationException>(()=>Host.Select("x.png",null)).Message);
        Assert.Contains("no cooker named 'Nope'",Assert.Throws<InvalidOperationException>(()=>Host.Select("x.txt","Nope")).Message);
    }
}
