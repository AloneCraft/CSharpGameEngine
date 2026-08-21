using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Windows;

namespace $rootnamespace$;

/// <summary>
/// 基本的な床・照明・プレイヤー開始地点を持つシーンです。
/// </summary>
internal partial class $safeitemname$ : IScene {
    public Node Root => new() {
        Name="BasicStage",
        Components=[
            new AmbientLight(),
            new Transform(),
            new DirectionalLight(),
            new HeightFog(),
        ],
        Children=[
            new Node {
                Name="Floor",
                Components=[
                    new Transform {
                        PositionY=-0.5f,
                        RotationW=1f,
                        ScaleX=10f,
                        ScaleY=1f,
                        ScaleZ=10f,
                    },
                    new PrimitiveMesh { Type=PrimitiveType.Box },
                ],
            },
            new Node {
                Name="PlayerStart",
                Components=[
                    new Transform {
                        PositionY=1.7f,
                        PositionZ=-4f,
                        RotationW=1f,
                    },
                    new PlayerStart(),
                ],
            },
        ],
    };
}