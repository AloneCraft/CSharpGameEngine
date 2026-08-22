using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Windows;

namespace UserEcsProject.Scenes;

// ============================================================================
// ADR-0040 Phase 6 のデモ: 個別配置プリミティブ({Transform, PrimitiveMesh})を宣言する、
// MainScene.cs とは独立した Startup Scene 群。GameSystemGenerator は [StartupScene] を複数許容する
// ため、MainScene.cs を一切変更せずに済む(既存の Lvl_FirstPerson の見た目は不変)。
// Systems/RuntimeSystem.cs の LevelLoad が MainScene の Level から生成する StageRenderer/カメラを
// 共有し、そこへこのシーンのプリミティブが追加でロースターへ載る。
//
// 各プリミティブを Root 直下の Children ではなく、それぞれ専用の Root(単一ノード、Components のみ)
// として宣言している点に注意: Children にすると自動付与される Parent コンポーネントにより
// {Transform, PrimitiveMesh, Parent} という別の Archetype になり、Scene DSL 由来の Archetype は
// System 由来の全 Arch etype より後ろに登録される(ADR-0038)ため、LevelLoad の
// {AppHost, Level} オーバーロードがロースターを確定させた後に処理されてしまい、
// 描画から漏れる(実際に一度この形でハマって確認した)。Root を単一ノードにすることで
// {Transform, PrimitiveMesh} という LevelLoad の1つ目のオーバーロードと完全に同じ Archetype
// (Archetype2、System 側の宣言順で早期に登録される)に収まり、正しく拾われる。
// ============================================================================

internal partial class PrimitiveShowcaseBoxScene:IScene {
    public Node Root=>new(){
        Name="ShowcaseBox",
        Components=[
            new Transform {
                        Position=new Logical.Vector3(3f,0.5f,0f),
                        Rotation=new Logical.Vector3(0f,0f,0f),
                        Scale=new Logical.Vector3(1f,1f,1f),
                    },
            new PrimitiveMesh { Type=PrimitiveType.Box },
        ],
    };
}

internal partial class PrimitiveShowcaseSphereScene:IScene {
    public Node Root=>new(){
        Name="ShowcaseSphere",
        Components=[
            new Transform {
                        Position=new Logical.Vector3(5f,0.5f,0f),
                        Rotation=new Logical.Vector3(0f,0f,0f),
                        Scale=new Logical.Vector3(1f,1f,1f),
                    },
            new PrimitiveMesh { Type=PrimitiveType.Sphere },
        ],
    };
}
