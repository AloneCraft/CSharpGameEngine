using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CSharpGameEngine.Entities;
namespace CSharpGameEngine.FeatureStages;
public readonly record struct StageSceneRegistration(string Id,Func<StageSceneRunResult>Execute);
public sealed record StageSceneRunResult(string Schema,string Pack,string SceneId,string SceneType,int EntityCount,string StateHash,IReadOnlyList<string>Assertions,bool Passed);
public static class StagePackHost {
    private static readonly JsonSerializerOptions JsonOptions=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase,WriteIndented=true};
    public static StageSceneRegistration Scene<TScene>(string Id)where TScene:IScene,ISceneDefinition=>new(Id,static()=>RunScene<TScene>());
    public static int Run(string Pack,string[]Args,IReadOnlyList<StageSceneRegistration>Scenes){
        if(Args.Length==0||Args[0] is "list" or "--help" or "-h"){
            Console.WriteLine(JsonSerializer.Serialize(new {schema="csharp-game-engine-stage-pack-v1",pack=Pack,scenes=Scenes.Select(static Scene=>Scene.Id)},JsonOptions));
            return 0;
        }
        if(Args.Length!=2||Args[0]!="run"){
            Console.Error.WriteLine("Usage: list | run <scene-id>");
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
    private static StageSceneRunResult RunScene<TScene>()where TScene:IScene,ISceneDefinition{
        var World=new World();
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
