using System.Text.Json;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>
/// ADR-0078/M0112：4 つのテンプレートレベルには M0110 と M0111 で追加された環境要素（ボリューメトリッククラウド、Lumen に似た GI と反射用のグローバル距離場、スカイライトと大気、影を落とす太陽、ポストプロセスボリューム）が含まれる。また、UE オリジナルにスカイスフィアがあるレベル（ThirdPerson、FirstPerson、TopDown では `SM_SkySphere`、Vehicle にはない：`tools/UnrealExport/probe_sky_and_clouds.py`）には、それも含まれる。値は UE のもの（太陽の lux、露出範囲）。
/// </summary>
public sealed class TemplateLightingSceneTests {
    private static string RepositoryRoot(){
        var Directory=new DirectoryInfo(AppContext.BaseDirectory);
        while(Directory is not null&&!File.Exists(Path.Combine(Directory.FullName,"CSharpGameEngine.slnx"))){ Directory=Directory.Parent; }
        return Directory?.FullName??throw new InvalidOperationException("The repository root was not found.");
    }
    private static JsonElement Level(string Template,string Name)=>JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(),"Templates","CSharpGameEngine.Templates."+Template,"Scenes",Name+".json"))).RootElement.GetProperty("root");
    private static IEnumerable<JsonElement>Nodes(JsonElement Node){
        yield return Node;
        foreach(var Child in Node.GetProperty("children").EnumerateArray()){
            foreach(var Descendant in Nodes(Child)){ yield return Descendant; }
        }
    }
    private static JsonElement?Component(JsonElement Root,string Type){
        foreach(var Node in Nodes(Root)){
            foreach(var Item in Node.GetProperty("components").EnumerateArray()){
                if(Item.GetProperty("type").GetString()=="CSharpGameEngine.Runtime.Lighting."+Type){ return Item.GetProperty("members"); }
            }
        }
        return null;
    }
    public static TheoryData<string,string,bool,float>Levels=>new() {
        { "ThirdPerson","Lvl_ThirdPerson",true,3f },
        { "FirstPerson","Lvl_FirstPerson",true,3f },
        { "TopDown","Lvl_TopDown",true,6f },
        { "Vehicle","Lvl_VehicleBasic",false,10f },
    };
    [Theory]
    [MemberData(nameof(Levels))]
    public void EveryLevelHasTheCloudsTheDistanceFieldTheSkyAndAShadowedSun(string Template,string Name,bool HasSkySphere,float SunLux){
        var Root=Level(Template,Name);
        foreach(var Part in new[]{"SkyAtmosphere","SkyLight","ExponentialHeightFog","VolumetricCloud","GlobalDistanceField","PostProcessVolume","DirectionalLight","LightShadow","GlobalIlluminationVolume"}){
            Assert.True(Component(Root,Part) is not null,$"{Template} has no {Part}");
        }
        Assert.Equal(HasSkySphere,Component(Root,"SkySphere") is not null);
        Assert.Equal(SunLux,Component(Root,"DirectionalLight")!.Value.GetProperty("Illuminance").GetProperty("Lux").GetSingle());
    }
}
