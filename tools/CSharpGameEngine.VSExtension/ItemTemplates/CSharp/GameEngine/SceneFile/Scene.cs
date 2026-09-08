using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Windows;
using CSharpGameEngine.Runtime.Lighting;

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
            new LightShadow(),
            new HeightFog(),
        ],
        Children=[
            new Node {
                Name="Floor",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(0f,-0.5f,0f),
                        Scale=new LogicalVector3(10f,1f,10f),
                    },
                    new PrimitiveMesh { Type=PrimitiveType.Box },
                ],
            },
            new Node {
                Name="PlayerStart",
                Components=[
                    new Transform {
                        Position=new LogicalVector3(0f,1.7f,-4f),
                    },
                    new PlayerStart(),
                ],
            },
        ],
    };
}
