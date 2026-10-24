using System.Text.Json;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>
/// ADR-0078/M0112: the four template levels carry the environment parts that M0110 and M0111 added (volumetric cloud, global distance field for the Lumen-like
/// GI and reflections, sky light and atmosphere, a shadowed sun, the post-process volume), and the levels whose UE original has a sky sphere (`SM_SkySphere` in
/// ThirdPerson, FirstPerson and TopDown, none in Vehicle: `tools/UnrealExport/probe_sky_and_clouds.py`) have it. The values are UE's (sun lux, exposure range).
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
