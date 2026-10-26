namespace CSharpGameEngine.CodeAnalysis.Tests;

/// <summary>
/// C# キーワードと同じ名前の Scene JSON メンバー、コンストラクターパラメーター、列挙メンバー（<c>@class</c> というフィールドなど）は、逐語的識別子の接頭辞を付けて生成
/// Scene に書き込む。以前は生成コードがコンパイルできなかった。
/// </summary>
public sealed class SceneJsonKeywordMemberTests {
    private const string Components="""
        namespace Sample;
        public readonly struct Range {
            public Range(float @in,float @out){ this.@in=@in; this.@out=@out; }
            public readonly float @in;
            public readonly float @out;
        }
        public enum Kind { Plain, @default }
        public struct Odd { public float @class; public int @event; public Range Span; public Kind K; }
        internal struct KeepGeneratorAlive : CSharpGameEngine.Entities.ISystem {
            public bool Update(ref Odd odd, ref CSharpGameEngine.Entities.State state) => true;
        }
        """;
    private const string Driver="""
        using System.Globalization;
        using CSharpGameEngine.Entities;
        using Sample;
        namespace Game;
        public static class Driver {
            public static string Describe(){
                World World=new();
                SceneInstance Instance=World.InstantiateScene<MainScene>();
                Entity Root=World.GetSceneRoot(Instance);
                if(!World.TryGetComponent(Root,out Odd O)){ return "missing component"; }
                return string.Join("|",O.@class.ToString("R",CultureInfo.InvariantCulture),O.@event,O.Span.@in,O.Span.@out,O.K);
            }
        }
        """;
    private const string JsonScene="""
        {
          "schemaVersion": 1,
          "namespace": "Game",
          "className": "MainScene",
          "root": {
            "id": "7c0c2c9e-6c56-4f46-9d0f-0c6f2c8f0001",
            "name": "Root",
            "components": [
              { "type": "Sample.Odd", "members": { "class": 2.5, "event": 3, "Span": { "in": 1, "out": 4 }, "K": "default" } }
            ]
          }
        }
        """;
    [Fact]
    public void MembersAndParametersNamedLikeKeywords_Build(){
        var Assembly=GameSystemGeneratorTestHelper.CompileAndLoad(
            GameSystemGeneratorTestHelper.CreateCompilation([.. GameSystemGeneratorTestHelper.SceneDslSources,Components,Driver]),
            [new InMemoryAdditionalText(@"C:\Game\Scenes\MainScene.json",JsonScene)]);
        Assert.Equal("2.5|3|1|4|default",(string)GameSystemGeneratorTestHelper.Invoke(Assembly,"Game.Driver","Describe")!);
    }
}
