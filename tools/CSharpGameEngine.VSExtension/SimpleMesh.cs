using System;
using System.Numerics;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// プレビューシーンを構築するための、プロバイダーに依存しない CPU 頂点/インデックスデータ（UV/テクスチャなし）。<see cref="ExternalBridgeProcess"/> を経由して別プロセスで解析された glTF (ADR-0019) の <see cref="ExternalGltfLoader"/> と FBX (ADR-0010) の <see cref="ExternalFbxLoader"/> が共有するため、<see cref="RenderPreviewSceneBuilder"/> は形式に依存しない。
/// </summary>
internal sealed class SimpleMesh {
    public required Vector3[]Positions { get; init;}

    public required Vector3[]Normals { get; init;}

    public required uint[]Indices { get; init;}

    public required Vector4 BaseColorFactor { get; init;}

    public required Vector3 BoundsCenter { get; init;}

    public required float BoundsRadius { get; init;}

    public static(Vector3 Center,float Radius)ComputeBoundingSphere(Vector3[]Positions){
        var Min=Positions[0];
        var Max=Positions[0];
        foreach(var P in Positions){
            Min=Vector3.Min(Min,P);
            Max=Vector3.Max(Max,P);
        }

        var Center=(Min+Max)*0.5f;
        var Radius=0f;
        foreach(var P in Positions){
            Radius=Math.Max(Radius,Vector3.Distance(Center,P));
        }

        return(Center,Radius);
    }
}
