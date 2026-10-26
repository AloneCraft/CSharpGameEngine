using System;
using System.Collections.Generic;
using System.Numerics;
using CSharpGameEngine.Spatial.Abstractions;
using CSharpGameEngine.Spatial.Unity;

namespace CSharpGameEngine.Geometry;

/// <summary>
/// 単純なプレースホルダーメッシュをコードから直接生成する。インポーターは不要。<c>CSharpGameEngine.Graphics.D3D12</c>から移動し（ADR-0021）、
/// シェイプパレット機能の基本形状（<see cref="CreateSphere"/>／<see cref="CreateCylinder"/>／<see cref="CreateCapsule"/>／
/// <see cref="CreateCone"/>／<see cref="CreateTorus"/>／<see cref="CreateDisk"/>）を、グラフィックス非依存の単一プロジェクトにまとめた。
/// 純粋な<see cref="System.Numerics"/>だけを使用し、D3D12／TerraFX型には依存しない。このため、
/// <c>CSharpGameEngine.Graphics.D3D12</c>とプロセス外の<c>tools/CSharpGameEngine.Primitive.Cli</c>ブリッジの両方から参照できる。
/// net472のVSIX側で生成計算を重複させる必要もない（「常にプロセス外ブリッジ経由とし、プロセス内で実行しない」というADR-0019の原則）。
/// 頂点の巻き順は一貫性のために選ぶが、現在は可視性に影響しない。すべての利用側で裏面カリングを無効にしている。
///
/// 頂点／法線の計算はRUF空間（<see cref="RufVector"/>）で行い、各<c>Create*</c>メソッドが
/// <see cref="ProceduralMeshData"/>を構築する時点でのみ<see cref="Vector3"/>へ変換する（ADR-0026）。
/// <see cref="UnityCoordinateConvention"/>は固定の恒等写像（R=X、U=Y、F=Z）なので、この変換は従来の直接的な
/// <see cref="Vector3"/>実装とビット単位で一致し、出力値を変更しない。
/// </summary>
public static class ProceduralMeshFactory {
    private const int DefaultSegments=24;
    private const int DefaultHemisphereRings=8;
    private const int DefaultMinorSegments=12;

    /// <summary>原点を中心に置き、Y=0 の XZ 平面上で +Y を向く矩形。地面と、形状パレットの「矩形」プリミティブに使用する。</summary>
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

    /// <summary>原点を中心とする軸平行ボックス（24頂点、各面に1つの法線）。各軸の寸法は<paramref name="Size"/>。</summary>
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
    /// 原点を中心とするUV球。三角形が退化しないよう、極点は1頂点とし、最初／最後の内側リングに扇状につなぐ。
    /// 極点を列ごとに複製して上下の帯を四角形でつなぐ単純な方法は使わない。
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

    /// <summary>原点を中心とする、Y軸に沿った軸平行の上下端付きシリンダー。</summary>
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

    /// <summary>原点を中心とする、Y軸に沿った軸平行の円錐。頂点は+height/2、蓋付きの円形底面は-height/2。</summary>
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

        // フラットシェーディングの側面では、各三角形に専用の3頂点を割り当てる（一部重複）。
        // 各面の法線は補間ではなく外積から求めるため、正確な値になる。
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

    /// <summary>XZ平面上で原点を中心とするトーラス。自己交差を防ぐため、<paramref name="MinorRadius"/>（管の半径）は<paramref name="MajorRadius"/>（リングの半径）より小さくする。</summary>
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

    /// <summary>原点を中心とする平らな円盤。Y=0にあり、+Y方向を向く。</summary>
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
    /// Y軸に沿って原点を中心とする、上下端付きカプセル（半球状の端を持つシリンダー）。
    /// <paramref name="Height"/>は両端の半球を含む全高なので、円筒部分に正の長さを持たせるには<c>2 * radius</c>を超える必要がある。
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
    /// <see cref="CreateCapsule"/>が作る半球の内側に、<paramref name="HemisphereRings"/>個の緯度リングを追加する。
    /// 極点と赤道は呼び出し元が別に追加するため、ここでは含めない。
    /// </summary>
    private static int AddHemisphereRings(List<RufVector>Positions,List<RufVector>Normals,int Segments,int HemisphereRings,float Radius,float YOffset,bool UpperHalf){
        var FirstRingIndex=Positions.Count;
        for(var Lat=1;Lat<=HemisphereRings;Lat++){
            var Fraction=Lat/(float)(HemisphereRings+1); // (0, 1)
            var Theta=UpperHalf
                ?Fraction*(MathF.PI/2f)                  // (0, pi/2)：極点から赤道へ。
                :(MathF.PI/2f)+(Fraction*(MathF.PI/2f)); // (pi/2, pi)：赤道から極点へ。
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

    /// <summary>
    /// リングを追加する。頂点数は<paramref name="Segments"/>+1（継ぎ目を閉じる重複頂点を含めるため、<see cref="AddQuadBand"/>とインデックス計算を統一でき
    /// る）。<paramref name="Radial"/>では、半径方向の法線（シリンダー側面）と、呼び出し元が指定する平面法線<paramref name="CapNormal"/>（端面／円盤面）の
    /// どちらを使うか選ぶ。
    /// </summary>
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

    /// <summary>同じ長さ（<paramref name="Segments"/>+1頂点）の2つのリング間を四角形でつなぐ。</summary>
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

    /// <summary>成分ごとのスケール（R*R、U*U、F*F）。<see cref="RufVector"/>自体はスカラー倍のみを公開するため、この軸平行な面の方向に各軸の寸法を適用する用途に限って使う。</summary>
    private static RufVector ScaleComponents(RufVector Value,RufVector Scale)=>new(Value.R*Scale.R,Value.U*Scale.U,Value.F*Scale.F);

    /// <summary>出力境界で RUF 空間の頂点/法線データを物理 XYZ の格納形式へ変換する (ADR-0026「境界の原則」)。<see cref="UnityCoordinateConvention"/> では恒等変換となり、値は変化しない。</summary>
    private static Vector3[]ToXyzArray(IReadOnlyList<RufVector>Values){
        Vector3[]Result=new Vector3[Values.Count];
        for(var I=0;I<Values.Count;I++){
            Result[I]=(Vector3)Spatial<UnityCoordinateConvention>.ToXyz(Values[I]);
        }

        return Result;
    }
}
