using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Generated;
namespace CSharpGameEngine.FeatureStages;
public readonly record struct StageSceneRegistration(string Id,Func<StageSceneRunResult>Execute,Func<IReadOnlyList<StageVisual>>CollectVisuals);
public sealed record StageSceneRunResult(string Schema,string Pack,string SceneId,string SceneType,int EntityCount,string StateHash,IReadOnlyList<string>Assertions,bool Passed);
public static class StagePackHost {
    private static readonly JsonSerializerOptions JsonOptions=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase,WriteIndented=true};
    public static StageSceneRegistration Scene<TScene>(string Id)where TScene:ISceneDefinition=>new(Id,static()=>RunScene<TScene>(),static()=>CollectVisuals<TScene>());
    public static int Run(string Pack,string[]Args,IReadOnlyList<StageSceneRegistration>Scenes){
        if(Args.Length==0||Args[0] is "list" or "--help" or "-h"){
            Console.WriteLine(JsonSerializer.Serialize(new {schema="csharp-game-engine-stage-pack-v1",pack=Pack,scenes=Scenes.Select(static Scene=>Scene.Id)},JsonOptions));
            return 0;
        }
        if(Args.Length>=2&&Args[0]=="show"){ return Show(Pack,Args,Scenes); }
        if(Args.Length!=2||Args[0]!="run"){
            Console.Error.WriteLine("Usage: list | run <scene-id> | show <scene-id> [--frames N] [--hidden] [--warp] [--width W] [--height H] [--capture <png>]");
            return 2;
        }
        var Registration=Scenes.FirstOrDefault(Scene=>Scene.Id==Args[1]);
        if(string.IsNullOrWhiteSpace(Registration.Id)){
            Console.Error.WriteLine($"Unknown scene ID: {Args[1]}");
            return 2;
        }
        try {
            var Result=Registration.Execute()with{Pack=Pack,SceneId=Registration.Id};
            Console.WriteLine(JsonSerializer.Serialize(Result,JsonOptions));
            return Result.Passed?0:1;
        } catch(Exception Ex){
            Console.Error.WriteLine(Ex);
            return 1;
        }
    }
    /// <summary>`show`: シーンの見た目を実際の窓に描く。`run` と違い、窓を閉じるまで（`--frames` があればその枚数まで）続く。</summary>
    private static int Show(string Pack,string[]Args,IReadOnlyList<StageSceneRegistration>Scenes){
        var Registration=Scenes.FirstOrDefault(Scene=>Scene.Id==Args[1]);
        if(string.IsNullOrWhiteSpace(Registration.Id)){
            Console.Error.WriteLine($"Unknown scene ID: {Args[1]}");
            return 2;
        }
        int Width=1280,Height=720;int?Frames=null;var Hidden=false;var Warp=false;string?Capture=null;
        for(var Index=2;Index<Args.Length;Index++){
            switch(Args[Index]){
                case "--frames" when Index+1<Args.Length&&int.TryParse(Args[Index+1],out var Count)&&Count>0:Frames=Count;Index++;break;
                case "--width" when Index+1<Args.Length&&int.TryParse(Args[Index+1],out var W)&&W>0:Width=W;Index++;break;
                case "--height" when Index+1<Args.Length&&int.TryParse(Args[Index+1],out var H)&&H>0:Height=H;Index++;break;
                case "--capture" when Index+1<Args.Length:Capture=Args[Index+1];Index++;break;
                case "--hidden":Hidden=true;break;
                case "--warp":Warp=true;break;
                default:
                    Console.Error.WriteLine($"Unknown or incomplete option: {Args[Index]}");
                    return 2;
            }
        }
        if(Capture is not null&&Frames is null){ Frames=3; }
        try {
            return StagePackViewer.Show(Pack,Registration.Id,Registration.CollectVisuals(),new StageViewOptions(Width,Height,Frames,Hidden,Warp,Capture));
        } catch(Exception Ex){
            Console.Error.WriteLine(Ex);
            return 1;
        }
    }
    /// <summary>シーンを実体化し、編集用の1フレームを回して、全 <see cref="StageVisual"/> を取り出す。</summary>
    private static IReadOnlyList<StageVisual>CollectVisuals<TScene>()where TScene:ISceneDefinition{
        using var Owner=new WorldOwner();
        Owner.World.InstantiateScene<TScene>();
        var State=Owner.CreateState();
        State.Enabled=true;
        GeneratedSystemSet Systems=default;
        StageVisualSink.Items.Clear();
        GeneratedGameLoop.RunEditorFrame(ref State,ref Systems);
        return [.. StageVisualSink.Items];
    }
    private static StageSceneRunResult RunScene<TScene>()where TScene:ISceneDefinition{
        var World=new Entities.World();
        var Instance=World.InstantiateScene<TScene>();
        var Slot=World.SceneInstances.Resolve(Instance);
        var Root=World.GetSceneRoot(Instance);
        var EntityCount=Slot?.Entities.Count??0;
        var RootAlive=World.IsAlive(Root);
        var OwnershipMatches=World.GetOwningScene(Root)==Instance;
        var BeforeUnload=$"{typeof(TScene).FullName}|{Instance.Slot}|{Instance.Version}|{Root.Id}|{EntityCount}|{RootAlive}|{OwnershipMatches}";
        var StateHash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(BeforeUnload))).ToLowerInvariant();
        World.UnloadScene(Instance);
        var SceneUnloaded=!World.IsSceneAlive(Instance);
        var RootDestroyed=!World.IsAlive(Root);
        var Assertions=new[]{
            $"entity-count-positive:{EntityCount>0}",
            $"root-alive:{RootAlive}",
            $"ownership:{OwnershipMatches}",
            $"scene-unloaded:{SceneUnloaded}",
            $"root-destroyed:{RootDestroyed}",
        };
        var Passed=EntityCount>0&&RootAlive&&OwnershipMatches&&SceneUnloaded&&RootDestroyed;
        return new StageSceneRunResult("csharp-game-engine-stage-scene-result-v1",string.Empty,string.Empty,typeof(TScene).FullName??typeof(TScene).Name,EntityCount,StateHash,Assertions,Passed);
    }
}
