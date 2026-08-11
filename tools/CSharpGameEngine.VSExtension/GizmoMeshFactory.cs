using System;
using System.Numerics;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Procedurally builds a small placeholder cube <see cref="SimpleMesh"/> in memory (no disk I/O, no
/// bridge call) for <c>[SceneDefinition]</c>-authored nodes, which have no mesh-asset concept at all
/// (ADR-0031). Deliberately NOT routed through <c>CSharpGameEngine.Geometry.ProceduralMeshFactory
/// .CreateBox</c>: that method's vertex/normal math goes through the RUF spatial abstraction
/// (<c>CSharpGameEngine.Spatial.Abstractions</c>/<c>.Unity</c>, both net10.0), which this net472 VSIX
/// cannot reference in-process - the same TFM wall ADR-0031 already documents for
/// <c>CSharpGameEngine.Entities</c>. A fixed-size axis-aligned cube is constant geometry, not an
/// algorithm that can drift, so duplicating just this one shape locally (as plain
/// <see cref="System.Numerics.Vector3"/> math, not RUF) is the same "duplicate the trivial thing,
/// don't bridge it" call ADR-0023 already made for the wire-protocol DTOs. Face/vertex/index layout
/// mirrors <c>ProceduralMeshFactory.CreateBox</c> exactly (6 faces x 4 vertices, one normal per face),
/// just expressed directly in XYZ instead of RUF.
/// </summary>
internal static class GizmoMeshFactory {
    private static readonly Vector4 DefaultTint=new(0.4f,0.65f,0.95f,1f);

    internal static SimpleMesh Create(float Size=0.3f,Vector4?Tint=null){
        if(Size<=0f){
            throw new ArgumentOutOfRangeException(nameof(Size),"size must be positive.");
        }

        var half=Size/2f;

        (Vector3 Normal,Vector3 U,Vector3 V)[]Faces=[
            (Vector3.UnitZ,Vector3.UnitX,Vector3.UnitY),      // +Z
            (-Vector3.UnitZ,-Vector3.UnitX,Vector3.UnitY),    // -Z
            (Vector3.UnitX,-Vector3.UnitZ,Vector3.UnitY),     // +X
            (-Vector3.UnitX,Vector3.UnitZ,Vector3.UnitY),     // -X
            (Vector3.UnitY,Vector3.UnitX,-Vector3.UnitZ),     // +Y
            (-Vector3.UnitY,Vector3.UnitX,Vector3.UnitZ),     // -Y
        ];

        Vector3[]Positions=new Vector3[Faces.Length*4];
        Vector3[]Normals=new Vector3[Faces.Length*4];
        var Indices=new uint[Faces.Length*6];

        for(var face=0;face<Faces.Length;face++){
            (Vector3 Normal,Vector3 U,Vector3 V)=Faces[face];
            Vector3 Center=Normal*half;
            Vector3 UHalf=U*half;
            Vector3 VHalf=V*half;
            var VertexBase=face*4;

            Positions[VertexBase+0]=Center-UHalf-VHalf;
            Positions[VertexBase+1]=Center+UHalf-VHalf;
            Positions[VertexBase+2]=Center+UHalf+VHalf;
            Positions[VertexBase+3]=Center-UHalf+VHalf;

            for(var i=0;i<4;i++){
                Normals[VertexBase+i]=Normal;
            }

            var IndexBase=face*6;
            Indices[IndexBase+0]=(uint)(VertexBase+0);
            Indices[IndexBase+1]=(uint)(VertexBase+1);
            Indices[IndexBase+2]=(uint)(VertexBase+2);
            Indices[IndexBase+3]=(uint)(VertexBase+0);
            Indices[IndexBase+4]=(uint)(VertexBase+2);
            Indices[IndexBase+5]=(uint)(VertexBase+3);
        }

        (Vector3 BoundsCenter,var BoundsRadius)=SimpleMesh.ComputeBoundingSphere(Positions);

        return new SimpleMesh {
            Positions=Positions,
            Normals=Normals,
            Indices=Indices,
            BaseColorFactor=Tint??DefaultTint,
            BoundsCenter=BoundsCenter,
            BoundsRadius=BoundsRadius,
        };
    }
}
