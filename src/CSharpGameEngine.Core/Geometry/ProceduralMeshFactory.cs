using System;
using System.Collections.Generic;
using System.Numerics;
using CSharpGameEngine.Spatial.Abstractions;
using CSharpGameEngine.Spatial.Unity;

namespace CSharpGameEngine.Geometry;

/// <summary>
/// Builds simple placeholder meshes directly in code - no importer needed. Moved here from
/// <c>CSharpGameEngine.Graphics.D3D12</c> (ADR-0021) so the shape-palette feature's primitives
/// (<see cref="CreateSphere"/>/<see cref="CreateCylinder"/>/<see cref="CreateCapsule"/>/
/// <see cref="CreateCone"/>/<see cref="CreateTorus"/>/<see cref="CreateDisk"/>) live in one
/// graphics-independent project (pure <see cref="System.Numerics"/>, no D3D12/TerraFX types) that
/// both <c>CSharpGameEngine.Graphics.D3D12</c> and the out-of-process
/// <c>tools/CSharpGameEngine.Primitive.Cli</c> bridge can reference, instead of duplicating the
/// generation math on the net472 VSIX side (ADR-0019's "always go through an out-of-process bridge,
/// never in-process" principle). Winding order is picked for consistency but does not currently
/// affect visibility: every consumer disables backface culling.
///
/// Vertex/normal math is derived in RUF (<see cref="RufVector"/>) space and converted to
/// <see cref="Vector3"/> only at the point each <c>Create*</c> method builds its
/// <see cref="ProceduralMeshData"/> (ADR-0026). <see cref="UnityCoordinateConvention"/> is a fixed
/// identity mapping (R=X, U=Y, F=Z), so this conversion is bit-identical to the previous
/// direct-<see cref="Vector3"/> implementation - it does not change any output value.
/// </summary>
public static class ProceduralMeshFactory {
    private const int DefaultSegments=24;
    private const int DefaultHemisphereRings=8;
    private const int DefaultMinorSegments=12;

    /// <summary>An XZ-plane rectangle centered at the origin, Y=0, facing +Y. Used as the ground, and as the shape palette's "矩形" primitive.</summary>
    public static ProceduralMeshData CreatePlane(float Width,float Depth){
        if(Width<=0f||Depth<=0f){
            throw new ArgumentOutOfRangeException(nameof(Width),"width and depth must both be positive.");
        }

        var HalfWidth=Width/2f;
        var HalfDepth=Depth/2f;

        RufVector[]Positions=[
            new RufVector(-HalfWidth,0f,-HalfDepth),
            new RufVector(HalfWidth,0f,-HalfDepth),
            new RufVector(HalfWidth,0f,HalfDepth),
            new RufVector(-HalfWidth,0f,HalfDepth),
        ];

        RufVector[]Normals=[RufVector.Up,RufVector.Up,RufVector.Up,RufVector.Up];

        uint[]Indices=[0,1,2,0,2,3];

        return new ProceduralMeshData { Positions=ToXyzArray(Positions),Normals=ToXyzArray(Normals),Indices=Indices };
    }

    /// <summary>An axis-aligned box centered at the origin (24 vertices, one normal per face) sized <paramref name="Size"/> on each axis.</summary>
    public static ProceduralMeshData CreateBox(Vector3 Size){
        if(Size.X<=0f||Size.Y<=0f||Size.Z<=0f){
            throw new ArgumentOutOfRangeException(nameof(Size),"size components must all be positive.");
        }

        RufVector Half=new RufVector(Size.X,Size.Y,Size.Z)/2f;

        (RufVector Normal,RufVector U,RufVector V)[]Faces=[
            (RufVector.Forward,RufVector.Right,RufVector.Up),     // +Z
            (-RufVector.Forward,-RufVector.Right,RufVector.Up),   // -Z
            (RufVector.Right,-RufVector.Forward,RufVector.Up),    // +X
            (-RufVector.Right,RufVector.Forward,RufVector.Up),    // -X
            (RufVector.Up,RufVector.Right,-RufVector.Forward),    // +Y
            (-RufVector.Up,RufVector.Right,RufVector.Forward),    // -Y
        ];

        RufVector[]Positions=new RufVector[Faces.Length*4];
        RufVector[]Normals=new RufVector[Faces.Length*4];
        var Indices=new uint[Faces.Length*6];

        for(var Face=0;Face<Faces.Length;Face++){
            (RufVector Normal,RufVector U,RufVector V)=Faces[Face];
            RufVector Center=ScaleComponents(Normal,Half);
            RufVector UHalf=ScaleComponents(U,Half);
            RufVector VHalf=ScaleComponents(V,Half);
            var VertexBase=Face*4;

            Positions[VertexBase+0]=Center-UHalf-VHalf;
            Positions[VertexBase+1]=Center+UHalf-VHalf;
            Positions[VertexBase+2]=Center+UHalf+VHalf;
            Positions[VertexBase+3]=Center-UHalf+VHalf;

            for(var I=0;I<4;I++){
                Normals[VertexBase+I]=Normal;
            }

            var IndexBase=Face*6;
            Indices[IndexBase+0]=(uint)(VertexBase+0);
            Indices[IndexBase+1]=(uint)(VertexBase+1);
            Indices[IndexBase+2]=(uint)(VertexBase+2);
            Indices[IndexBase+3]=(uint)(VertexBase+0);
            Indices[IndexBase+4]=(uint)(VertexBase+2);
            Indices[IndexBase+5]=(uint)(VertexBase+3);
        }

        return new ProceduralMeshData { Positions=ToXyzArray(Positions),Normals=ToXyzArray(Normals),Indices=Indices };
    }

    /// <summary>
    /// A UV sphere centered at the origin. Poles are single vertices fanned to the first/last
    /// interior ring (rather than the naive "duplicate pole per column, quad the top/bottom band"
    /// approach) specifically so no triangle is ever degenerate.
    /// </summary>
    public static ProceduralMeshData CreateSphere(float Radius){
        if(Radius<=0f){
            throw new ArgumentOutOfRangeException(nameof(Radius),"radius must be positive.");
        }

        const int LatitudeSegments=16;
        const int LongitudeSegments=DefaultSegments;
        var RingStride=LongitudeSegments+1;
        var RingCount=LatitudeSegments-1;

        List<RufVector>Positions=[];
        List<RufVector>Normals=[];
        List<uint>Indices=[];

        var NorthPoleIndex=Positions.Count;
        Positions.Add(new RufVector(0,Radius,0));
        Normals.Add(RufVector.Up);

        var FirstRingIndex=Positions.Count;
        for(var Lat=1;Lat<LatitudeSegments;Lat++){
            var Theta=Lat*MathF.PI/LatitudeSegments;
            var SinTheta=MathF.Sin(Theta);
            var CosTheta=MathF.Cos(Theta);
            for(var Lon=0;Lon<=LongitudeSegments;Lon++){
                var Phi=Lon*2f*MathF.PI/LongitudeSegments;
                RufVector Normal=new(MathF.Cos(Phi)*SinTheta,CosTheta,MathF.Sin(Phi)*SinTheta);
                Positions.Add(Normal*Radius);
                Normals.Add(Normal);
            }
        }

        var SouthPoleIndex=Positions.Count;
        Positions.Add(new RufVector(0,-Radius,0));
        Normals.Add(-RufVector.Up);

        for(var Lon=0;Lon<LongitudeSegments;Lon++){
            Indices.Add((uint)NorthPoleIndex);
            Indices.Add((uint)(FirstRingIndex+Lon));
            Indices.Add((uint)(FirstRingIndex+Lon+1));
        }

        for(var Ring=0;Ring<RingCount-1;Ring++){
            var RingStart=FirstRingIndex+(Ring*RingStride);
            AddQuadBand(Indices,RingStart,RingStart+RingStride,LongitudeSegments);
        }

        var LastRingStart=FirstRingIndex+((RingCount-1)*RingStride);
        for(var Lon=0;Lon<LongitudeSegments;Lon++){
            Indices.Add((uint)(LastRingStart+Lon));
            Indices.Add((uint)SouthPoleIndex);
            Indices.Add((uint)(LastRingStart+Lon+1));
        }

        return new ProceduralMeshData { Positions=ToXyzArray(Positions),Normals=ToXyzArray(Normals),Indices=[.. Indices]};
    }

    /// <summary>An axis-aligned (Y) capped cylinder centered at the origin.</summary>
    public static ProceduralMeshData CreateCylinder(float Radius,float Height){
        if(Radius<=0f){
            throw new ArgumentOutOfRangeException(nameof(Radius),"radius must be positive.");
        }

        if(Height<=0f){
            throw new ArgumentOutOfRangeException(nameof(Height),"height must be positive.");
        }

        const int Segments=DefaultSegments;
        var HalfHeight=Height/2f;

        List<RufVector>Positions=[];
        List<RufVector>Normals=[];
        List<uint>Indices=[];

        var SideTopStart=AddRing(Positions,Normals,Segments,Radius,HalfHeight,Radial:true);
        var SideBottomStart=AddRing(Positions,Normals,Segments,Radius,-HalfHeight,Radial:true);
        AddQuadBand(Indices,SideTopStart,SideBottomStart,Segments);

        var TopCenterIndex=Positions.Count;
        Positions.Add(new RufVector(0,HalfHeight,0));
        Normals.Add(RufVector.Up);
        var TopRingStart=AddRing(Positions,Normals,Segments,Radius,HalfHeight,Radial:false,CapNormal:RufVector.Up);
        for(var I=0;I<Segments;I++){
            Indices.Add((uint)TopCenterIndex);
            Indices.Add((uint)(TopRingStart+I));
            Indices.Add((uint)(TopRingStart+I+1));
        }

        var BottomCenterIndex=Positions.Count;
        Positions.Add(new RufVector(0,-HalfHeight,0));
        Normals.Add(-RufVector.Up);
        var BottomRingStart=AddRing(Positions,Normals,Segments,Radius,-HalfHeight,Radial:false,CapNormal:-RufVector.Up);
        for(var I=0;I<Segments;I++){
            Indices.Add((uint)BottomCenterIndex);
            Indices.Add((uint)(BottomRingStart+I+1));
            Indices.Add((uint)(BottomRingStart+I));
        }

        return new ProceduralMeshData { Positions=ToXyzArray(Positions),Normals=ToXyzArray(Normals),Indices=[.. Indices]};
    }

    /// <summary>An axis-aligned (Y) cone centered at the origin: apex at +height/2, capped circular base at -height/2.</summary>
    public static ProceduralMeshData CreateCone(float Radius,float Height){
        if(Radius<=0f){
            throw new ArgumentOutOfRangeException(nameof(Radius),"radius must be positive.");
        }

        if(Height<=0f){
            throw new ArgumentOutOfRangeException(nameof(Height),"height must be positive.");
        }

        const int Segments=DefaultSegments;
        var HalfHeight=Height/2f;
        RufVector Apex=new(0,HalfHeight,0);

        List<RufVector>Positions=[];
        List<RufVector>Normals=[];
        List<uint>Indices=[];

        // Flat-shaded side: each triangle gets its own three vertices (some duplication) so the
        // per-face normal (derived from the cross product, not interpolated) is exact.
        for(var I=0;I<Segments;I++){
            var Angle0=I*2f*MathF.PI/Segments;
            var Angle1=(I+1)*2f*MathF.PI/Segments;
            RufVector P0=new(MathF.Cos(Angle0)*Radius,-HalfHeight,MathF.Sin(Angle0)*Radius);
            RufVector P1=new(MathF.Cos(Angle1)*Radius,-HalfHeight,MathF.Sin(Angle1)*Radius);
            RufVector FaceNormal=Spatial<UnityCoordinateConvention>.Cross(P1-Apex,P0-Apex).Normalize();

            var BaseIndex=Positions.Count;
            Positions.Add(Apex);Normals.Add(FaceNormal);
            Positions.Add(P0);Normals.Add(FaceNormal);
            Positions.Add(P1);Normals.Add(FaceNormal);
            Indices.Add((uint)BaseIndex);
            Indices.Add((uint)(BaseIndex+1));
            Indices.Add((uint)(BaseIndex+2));
        }

        var BaseCenterIndex=Positions.Count;
        Positions.Add(new RufVector(0,-HalfHeight,0));
        Normals.Add(-RufVector.Up);
        var BaseRingStart=AddRing(Positions,Normals,Segments,Radius,-HalfHeight,Radial:false,CapNormal:-RufVector.Up);
        for(var I=0;I<Segments;I++){
            Indices.Add((uint)BaseCenterIndex);
            Indices.Add((uint)(BaseRingStart+I+1));
            Indices.Add((uint)(BaseRingStart+I));
        }

        return new ProceduralMeshData { Positions=ToXyzArray(Positions),Normals=ToXyzArray(Normals),Indices=[.. Indices]};
    }

    /// <summary>A torus centered at the origin in the XZ plane. <paramref name="MinorRadius"/> (tube radius) must be smaller than <paramref name="MajorRadius"/> (ring radius) to avoid self-intersection.</summary>
    public static ProceduralMeshData CreateTorus(float MajorRadius,float MinorRadius){
        if(MajorRadius<=0f){
            throw new ArgumentOutOfRangeException(nameof(MajorRadius),"majorRadius must be positive.");
        }

        if(MinorRadius<=0f){
            throw new ArgumentOutOfRangeException(nameof(MinorRadius),"minorRadius must be positive.");
        }

        if(MinorRadius>=MajorRadius){
            throw new ArgumentOutOfRangeException(nameof(MinorRadius),"minorRadius must be smaller than majorRadius to avoid self-intersection.");
        }

        const int MajorSegments=DefaultSegments;
        const int MinorSegments=DefaultMinorSegments;
        var RingStride=MinorSegments+1;

        List<RufVector>Positions=[];
        List<RufVector>Normals=[];
        List<uint>Indices=[];

        for(var I=0;I<=MajorSegments;I++){
            var Theta=I*2f*MathF.PI/MajorSegments;
            var CosTheta=MathF.Cos(Theta);
            var SinTheta=MathF.Sin(Theta);

            for(var J=0;J<=MinorSegments;J++){
                var Phi=J*2f*MathF.PI/MinorSegments;
                var CosPhi=MathF.Cos(Phi);
                var SinPhi=MathF.Sin(Phi);
                var TubeRadius=MajorRadius+(MinorRadius*CosPhi);

                Positions.Add(new RufVector(TubeRadius*CosTheta,MinorRadius*SinPhi,TubeRadius*SinTheta));
                Normals.Add(new RufVector(CosPhi*CosTheta,SinPhi,CosPhi*SinTheta));
            }
        }

        for(var I=0;I<MajorSegments;I++){
            AddQuadBand(Indices,I*RingStride,(I+1)*RingStride,MinorSegments);
        }

        return new ProceduralMeshData { Positions=ToXyzArray(Positions),Normals=ToXyzArray(Normals),Indices=[.. Indices]};
    }

    /// <summary>A flat circular disk centered at the origin, Y=0, facing +Y.</summary>
    public static ProceduralMeshData CreateDisk(float Radius){
        if(Radius<=0f){
            throw new ArgumentOutOfRangeException(nameof(Radius),"radius must be positive.");
        }

        const int Segments=DefaultSegments;

        List<RufVector>Positions=[RufVector.Zero];
        List<RufVector>Normals=[RufVector.Up];
        List<uint>Indices=[];

        var RingStart=AddRing(Positions,Normals,Segments,Radius,0f,Radial:false,CapNormal:RufVector.Up);
        for(var I=0;I<Segments;I++){
            Indices.Add(0);
            Indices.Add((uint)(RingStart+I));
            Indices.Add((uint)(RingStart+I+1));
        }

        return new ProceduralMeshData { Positions=ToXyzArray(Positions),Normals=ToXyzArray(Normals),Indices=[.. Indices]};
    }

    /// <summary>
    /// A capped capsule (cylinder with hemispherical caps) centered at the origin, axis Y.
    /// <paramref name="Height"/> is the total end-to-end height including both hemispheres, so it
    /// must exceed <c>2 * radius</c> for the cylindrical mid-section to have positive length.
    /// </summary>
    public static ProceduralMeshData CreateCapsule(float Radius,float Height){
        if(Radius<=0f){
            throw new ArgumentOutOfRangeException(nameof(Radius),"radius must be positive.");
        }

        if(Height<=(2f*Radius)){
            throw new ArgumentOutOfRangeException(nameof(Height),"height must exceed 2*radius so the cylindrical section between the hemispherical caps has positive length.");
        }

        const int HemisphereRings=DefaultHemisphereRings;
        const int Segments=DefaultSegments;
        var RingStride=Segments+1;
        var CylinderHalfHeight=(Height/2f)-Radius;

        List<RufVector>Positions=[];
        List<RufVector>Normals=[];
        List<uint>Indices=[];

        var TopPoleIndex=Positions.Count;
        Positions.Add(new RufVector(0,CylinderHalfHeight+Radius,0));
        Normals.Add(RufVector.Up);

        var TopFirstRing=AddHemisphereRings(Positions,Normals,Segments,HemisphereRings,Radius,CylinderHalfHeight,UpperHalf:true);

        var CylTopRing=AddRing(Positions,Normals,Segments,Radius,CylinderHalfHeight,Radial:true);
        var CylBottomRing=AddRing(Positions,Normals,Segments,Radius,-CylinderHalfHeight,Radial:true);

        var BottomFirstRing=AddHemisphereRings(Positions,Normals,Segments,HemisphereRings,Radius,-CylinderHalfHeight,UpperHalf:false);

        var BottomPoleIndex=Positions.Count;
        Positions.Add(new RufVector(0,-(CylinderHalfHeight+Radius),0));
        Normals.Add(-RufVector.Up);

        for(var Lon=0;Lon<Segments;Lon++){
            Indices.Add((uint)TopPoleIndex);
            Indices.Add((uint)(TopFirstRing+Lon));
            Indices.Add((uint)(TopFirstRing+Lon+1));
        }

        for(var Ring=0;Ring<HemisphereRings-1;Ring++){
            var A0=TopFirstRing+(Ring*RingStride);
            AddQuadBand(Indices,A0,A0+RingStride,Segments);
        }

        AddQuadBand(Indices,TopFirstRing+((HemisphereRings-1)*RingStride),CylTopRing,Segments);
        AddQuadBand(Indices,CylTopRing,CylBottomRing,Segments);
        AddQuadBand(Indices,CylBottomRing,BottomFirstRing,Segments);

        for(var Ring=0;Ring<HemisphereRings-1;Ring++){
            var A0=BottomFirstRing+(Ring*RingStride);
            AddQuadBand(Indices,A0,A0+RingStride,Segments);
        }

        var LastBottomRing=BottomFirstRing+((HemisphereRings-1)*RingStride);
        for(var Lon=0;Lon<Segments;Lon++){
            Indices.Add((uint)(LastBottomRing+Lon));
            Indices.Add((uint)BottomPoleIndex);
            Indices.Add((uint)(LastBottomRing+Lon+1));
        }

        return new ProceduralMeshData { Positions=ToXyzArray(Positions),Normals=ToXyzArray(Normals),Indices=[.. Indices]};
    }

    /// <summary>
    /// Appends <paramref name="HemisphereRings"/> interior latitude rings (excluding the pole and
    /// the equator, both added separately by the caller) for one hemisphere of <see cref="CreateCapsule"/>.
    /// </summary>
    private static int AddHemisphereRings(List<RufVector>Positions,List<RufVector>Normals,int Segments,int HemisphereRings,float Radius,float YOffset,bool UpperHalf){
        var FirstRingIndex=Positions.Count;
        for(var Lat=1;Lat<=HemisphereRings;Lat++){
            var Fraction=Lat/(float)(HemisphereRings+1); // (0, 1)
            var Theta=UpperHalf
                ?Fraction*(MathF.PI/2f)                  // (0, pi/2): pole -> equator
                :(MathF.PI/2f)+(Fraction*(MathF.PI/2f)); // (pi/2, pi): equator -> pole
            var SinTheta=MathF.Sin(Theta);
            var CosTheta=MathF.Cos(Theta);

            for(var Lon=0;Lon<=Segments;Lon++){
                var Phi=Lon*2f*MathF.PI/Segments;
                RufVector Normal=new(MathF.Cos(Phi)*SinTheta,CosTheta,MathF.Sin(Phi)*SinTheta);
                Positions.Add(new RufVector(Normal.R*Radius,YOffset+(Normal.U*Radius),Normal.F*Radius));
                Normals.Add(Normal);
            }
        }

        return FirstRingIndex;
    }

    /// <summary>Appends one ring of <paramref name="Segments"/>+1 vertices (a duplicate seam vertex closes the loop, keeping index math uniform with <see cref="AddQuadBand"/>). <paramref name="Radial"/> selects a purely radial normal (cylinder side) vs. a caller-supplied flat <paramref name="CapNormal"/> (cap/disk faces).</summary>
    private static int AddRing(List<RufVector>Positions,List<RufVector>Normals,int Segments,float Radius,float Y,bool Radial,RufVector CapNormal=default){
        var RingStart=Positions.Count;
        for(var I=0;I<=Segments;I++){
            var Angle=I*2f*MathF.PI/Segments;
            var X=MathF.Cos(Angle);
            var Z=MathF.Sin(Angle);
            Positions.Add(new RufVector(X*Radius,Y,Z*Radius));
            Normals.Add(Radial?new RufVector(X,0,Z):CapNormal);
        }

        return RingStart;
    }

    /// <summary>Quads a band between two same-length (<paramref name="Segments"/>+1 vertex) rings.</summary>
    private static void AddQuadBand(List<uint>Indices,int RingAStart,int RingBStart,int Segments){
        for(var I=0;I<Segments;I++){
            var A=RingAStart+I;
            var B=A+1;
            var C=RingBStart+I;
            var D=C+1;
            Indices.Add((uint)A);
            Indices.Add((uint)C);
            Indices.Add((uint)B);
            Indices.Add((uint)B);
            Indices.Add((uint)C);
            Indices.Add((uint)D);
        }
    }

    /// <summary>Component-wise scale (R*R, U*U, F*F) - not exposed on <see cref="RufVector"/> itself (only scalar multiply is), needed here only for scaling an axis-aligned face direction by a per-axis extent.</summary>
    private static RufVector ScaleComponents(RufVector Value,RufVector Scale)=>new(Value.R*Scale.R,Value.U*Scale.U,Value.F*Scale.F);

    /// <summary>Converts RUF-space vertex/normal data to physical XYZ storage at the output boundary (ADR-0026 "境界の原則"): identity under <see cref="UnityCoordinateConvention"/>, so this does not change any value.</summary>
    private static Vector3[]ToXyzArray(IReadOnlyList<RufVector>Values){
        Vector3[]Result=new Vector3[Values.Count];
        for(var I=0;I<Values.Count;I++){
            Result[I]=(Vector3)Spatial<UnityCoordinateConvention>.ToXyz(Values[I]);
        }

        return Result;
    }
}
