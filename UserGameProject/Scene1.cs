using CSharpGameEngine.Entities;

namespace UserGameProject;

// ============================================================================
// USER-WRITTEN REGION
//
// この領域がSceneの正本(Scene Authoring Phase A、ADR-0030)。ADR-0031の「シーンプレビュー」機能
// (コードエディタのコンテキストメニュー -> シーンプレビュー)を試すための単独フィクスチャで、
// Program.csの実際のゲームループには組み込まない(samples/SceneAuthoringDemo/MainScene.csと同じ、
// 「単独で存在し、ビルド・プレビューの動作確認のためだけに存在する」位置づけ)。
//
// 既存のComponents.csの型のみを使う(新規Component型は追加しない)。
// ============================================================================

[SceneDefinition]
public static partial class Scene1 {
    private static partial SceneNodeSymbol Root { get;}

    private static partial SceneNodeSymbol MainCamera { get;}

    private static partial SceneNodeSymbol Player { get;}

    private static partial SceneNodeSymbol Lighting { get;}

    private static partial SceneNodeSymbol Sun { get;}

    private static partial SceneNodeSymbol Ambient { get;}

    public static void Define(SceneBuilder Scene){
        Scene.Create(Root,Root=>{
            Root.Create(MainCamera,MainCamera=>{
                MainCamera.Set(new Position { X=0f,Y=2f,Z=-10f });
                MainCamera.Set(new Rotation { X=0f,Y=0f,Z=0f,W=1f });
                MainCamera.Set(new Scale { X=1f,Y=1f,Z=1f });
                MainCamera.Set(new CameraRig { Yaw=0f,Pitch=0f });
            });

            Root.Create(Player,Player=>{
                Player.Set(new Position { X=0f,Y=0f,Z=0f });
                Player.Set(new Rotation { X=0f,Y=0f,Z=0f,W=1f });
                Player.Set(new Scale { X=1f,Y=1f,Z=1f });
                Player.Set(new PlayerTag());
                Player.Set(new Health { Value=100 });
            });

            Root.Create(Lighting,Lighting=>{
                // Sun/Ambientをまとめるためだけのグルーピングアンカー。Position以外は持たない。
                Lighting.Set(new Position { X=0f,Y=8f,Z=0f });

                Lighting.Create(Sun,Sun=>{
                    Sun.Set(new Position { X=-2f,Y=0f,Z=0f });
                    Sun.Set(new DirectionalLightData {
                        DirectionTowardLightX=0.4f,
                        DirectionTowardLightY=0.7f,
                        DirectionTowardLightZ=0.5f,
                        ColorR=1f,
                        ColorG=0.95f,
                        ColorB=0.9f,
                        Intensity=1.0f,
                    });
                });

                Lighting.Create(Ambient,Ambient=>{
                    Ambient.Set(new Position { X=2f,Y=0f,Z=0f });
                    Ambient.Set(new AmbientLightData { ColorR=0.12f,ColorG=0.12f,ColorB=0.15f });
                });
            });
        });
    }
}
