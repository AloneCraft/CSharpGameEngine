using CSharpGameEngine.Entities;

namespace CSharpGameEngine.SceneAuthoring.Cli.Tests;

/// <summary>
/// <see cref="SceneRuntimeInvokerTests"/>が実際に読み込むフィクスチャ。このテストアセンブリ自身が
/// 「ユーザーのゲームプロジェクト」の役を務める(ランタイムのソースとGeneratorをリンクしているため、
/// 生成された<c>CreateRuntime</c>/<c>WorldSnapshot</c>を本物として持つ)。
/// 構造は<c>UserGameProject/Scene1.cs</c>を縮めたもの: Root -> (MainCamera, Player)。
/// </summary>
[SceneDefinition]
public static partial class TestScene {
    private static partial SceneNodeSymbol Root { get;}

    private static partial SceneNodeSymbol MainCamera { get;}

    private static partial SceneNodeSymbol Player { get;}

    public static void Define(SceneBuilder Scene){
        Scene.Create(Root,Root=>{
            Root.Create(MainCamera,MainCamera=>{
                MainCamera.Set(new Position { X=0f,Y=2f,Z=-10f });
                MainCamera.Set(new Rotation { X=0f,Y=0f,Z=0f,W=1f });
                MainCamera.Set(new Scale { X=1f,Y=1f,Z=1f });
            });

            Root.Create(Player,Player=>{
                Player.Set(new Position { X=1f,Y=0f,Z=3f });
                Player.Set(new Health { Value=100f });
            });
        });
    }
}

/// <summary>
/// GameSystemGeneratorは<c>ISystem</c>型がコンパイル内に無いと何も出力しない(無関係なプロジェクトを
/// 汚染しないためのゲート、ADR-0033)。このフィクスチャを成立させるためだけのSystem。
/// </summary>
internal struct KeepGeneratorAlive:ISystem {
    public bool Update(ref Position position,ref State state)=>true;
}
