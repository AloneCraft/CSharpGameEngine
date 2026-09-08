using System;
using System.Numerics;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Provider-neutral CPU vertex/index payload for preview scene construction - no UVs/texture. Shared by
/// <see cref="ExternalGltfLoader"/> (glTF, ADR-0019) and <see cref="ExternalFbxLoader"/> (FBX,
/// ADR-0010) - both parsed out-of-process via <see cref="ExternalBridgeProcess"/> - so
/// <see cref="RenderPreviewSceneBuilder"/> stays format-agnostic.
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
