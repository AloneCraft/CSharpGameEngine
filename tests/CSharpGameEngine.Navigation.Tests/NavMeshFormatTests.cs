using System.Buffers.Binary;
namespace CSharpGameEngine.Navigation.Tests;
/// <summary>ADR-0079/M0106: <c>.cgenav</c> の書き出しと読み込み。同じ入力から同じバイト列になり、壊れたファイルは例外を出さずに理由つきで断る。</summary>
public sealed class NavMeshFormatTests {
    private static NavMeshSettings Settings=>Geometry.DefaultSettings;
    /// <summary>左右 2 つの長方形が、1 本の境でつながる小さなナビメッシュ。</summary>
    private static NavMesh TwoRooms(){
        NavPolygon[]Polygons=[new(0,0,10,10,5),new(10,2,20,8,6)];
        NavPortal[]Portals=[new(0,1,10,2,10,8)];
        Assert.True(NavMesh.TryCreate(Settings,Geometry.P(-1,-2,-3),20,10,Polygons,Portals,out var Mesh,out var Error),Error);
        return Mesh!;
    }
    [Fact]
    public void ARoundTrip_KeepsEverythingAndIsTheSameBytesEveryTime(){
        var Mesh=TwoRooms();
        var Bytes=Mesh.ToBytes();
        Assert.Equal(Bytes,TwoRooms().ToBytes());
        Assert.True(NavMesh.TryRead(Bytes,out var Read,out var Error),Error);
        Assert.Equal(Mesh.Polygons,Read!.Polygons);
        Assert.Equal(Mesh.Portals,Read.Portals);
        Assert.Equal(Mesh.Origin,Read.Origin);
        Assert.Equal(Mesh.Settings,Read.Settings);
        Assert.Equal((Mesh.GridWidth,Mesh.GridDepth),(Read.GridWidth,Read.GridDepth));
        Assert.Equal(Bytes,Read.ToBytes());
    }
    [Fact]
    public void AMeshWithoutPolygons_IsValid(){
        Assert.True(NavMesh.TryCreate(Settings,LogicalVector3.Zero,4,4,[],[],out var Mesh,out var Error),Error);
        Assert.True(NavMesh.TryRead(Mesh!.ToBytes(),out var Read,out _));
        Assert.Empty(Read!.Polygons);
        Assert.False(Read.TryFindRoute(LogicalVector3.Zero,Geometry.P(1,0,1),out _));
    }
    [Fact]
    public void EveryTruncation_IsRefusedWithoutAnException(){
        var Bytes=TwoRooms().ToBytes();
        for(var Length=0;Length<Bytes.Length;Length++){
            var Truncated=Bytes[..Length];
            Assert.False(NavMesh.TryRead(Truncated,out var Mesh,out var Error),$"A file cut to {Length} bytes was accepted.");
            Assert.Null(Mesh);
            Assert.NotEmpty(Error);
        }
    }
    [Fact]
    public void BytesAfterTheLastPortal_AreRefused(){
        var Bytes=TwoRooms().ToBytes().Concat(new byte[]{ 0 }).ToArray();
        Assert.False(NavMesh.TryRead(Bytes,out _,out var Error));
        Assert.Contains("after",Error);
    }
    [Fact]
    public void AWrongSignatureOrVersion_IsRefused(){
        var Bytes=TwoRooms().ToBytes();
        Bytes[7]=(byte)'2';
        Assert.False(NavMesh.TryRead(Bytes,out _,out var Error));
        Assert.Contains("signature",Error);
        Assert.False(NavMesh.TryRead(new byte[100],out _,out _));
    }
    private static int PolygonCountOffset=>NavMesh.Magic.Length+9*LogicalScalar.SerializedSize+8;
    [Fact]
    public void AHugePolygonCount_IsRefusedWithoutAllocatingIt(){
        var Bytes=TwoRooms().ToBytes();
        BinaryPrimitives.WriteInt32LittleEndian(Bytes.AsSpan(PolygonCountOffset),int.MaxValue);
        Assert.False(NavMesh.TryRead(Bytes,out _,out _));
        BinaryPrimitives.WriteInt32LittleEndian(Bytes.AsSpan(PolygonCountOffset),-1);
        Assert.False(NavMesh.TryRead(Bytes,out _,out _));
    }
    [Fact]
    public void APolygonOutsideTheGrid_OrEmpty_IsRefused(){
        Assert.False(NavMesh.TryCreate(Settings,LogicalVector3.Zero,10,10,[new NavPolygon(0,0,11,5,0)],[],out _,out var Outside));
        Assert.Contains("outside the grid",Outside);
        Assert.False(NavMesh.TryCreate(Settings,LogicalVector3.Zero,10,10,[new NavPolygon(3,3,3,5,0)],[],out _,out _));
        Assert.False(NavMesh.TryCreate(Settings,LogicalVector3.Zero,10,10,[new NavPolygon(-1,0,5,5,0)],[],out _,out _));
    }
    [Fact]
    public void APortalToAPolygonThatDoesNotExist_OrNotOnTheSharedBorder_IsRefused(){
        NavPolygon[]Polygons=[new(0,0,10,10,5),new(10,2,20,8,6)];
        Assert.False(NavMesh.TryCreate(Settings,LogicalVector3.Zero,20,10,Polygons,[new NavPortal(0,2,10,2,10,8)],out _,out var Missing));
        Assert.Contains("do not exist",Missing);
        Assert.False(NavMesh.TryCreate(Settings,LogicalVector3.Zero,20,10,Polygons,[new NavPortal(0,0,10,2,10,8)],out _,out _));
        // 境の線(X=10)ではない。
        Assert.False(NavMesh.TryCreate(Settings,LogicalVector3.Zero,20,10,Polygons,[new NavPortal(0,1,9,2,9,8)],out _,out var Border));
        Assert.Contains("border",Border);
        // 2 つの辺の範囲(Z 2〜8)をはみ出す。
        Assert.False(NavMesh.TryCreate(Settings,LogicalVector3.Zero,20,10,Polygons,[new NavPortal(0,1,10,1,10,8)],out _,out _));
        // 軸に平行でない、長さが無い。
        Assert.False(NavMesh.TryCreate(Settings,LogicalVector3.Zero,20,10,Polygons,[new NavPortal(0,1,10,2,11,8)],out _,out _));
        Assert.False(NavMesh.TryCreate(Settings,LogicalVector3.Zero,20,10,Polygons,[new NavPortal(0,1,10,5,10,5)],out _,out _));
    }
    [Fact]
    public void InvalidSettings_AreRefused(){
        NavMeshSettings With(Func<NavMeshSettings,NavMeshSettings>Change)=>Change(Settings);
        Assert.False(NavMesh.TryCreate(With(static S=>S with { CellSize=LogicalScalar.Zero }),LogicalVector3.Zero,4,4,[],[],out _,out _));
        Assert.False(NavMesh.TryCreate(With(static S=>S with { CellHeight=LogicalScalar.FromInt64(-1) }),LogicalVector3.Zero,4,4,[],[],out _,out _));
        Assert.False(NavMesh.TryCreate(With(static S=>S with { AgentHeight=LogicalScalar.Zero }),LogicalVector3.Zero,4,4,[],[],out _,out _));
        Assert.False(NavMesh.TryCreate(With(static S=>S with { AgentRadius=LogicalScalar.FromInt64(-1) }),LogicalVector3.Zero,4,4,[],[],out _,out _));
        Assert.False(NavMesh.TryCreate(With(static S=>S with { AgentMaxSlopeDegrees=LogicalScalar.FromInt64(90) }),LogicalVector3.Zero,4,4,[],[],out _,out _));
        Assert.False(NavMesh.TryCreate(Settings,LogicalVector3.Zero,0,4,[],[],out _,out _));
        Assert.False(NavMesh.TryCreate(Settings,LogicalVector3.Zero,NavMesh.MaximumGridSize+1,4,[],[],out _,out _));
    }
    [Fact]
    public void ACorruptedValueInTheFile_IsRefusedByTheSameChecks(){
        var Bytes=TwoRooms().ToBytes();
        var FirstPolygon=PolygonCountOffset+4;
        // 最初の多角形の MaxX を格子の外へ。
        BinaryPrimitives.WriteInt32LittleEndian(Bytes.AsSpan(FirstPolygon+8),9999);
        Assert.False(NavMesh.TryRead(Bytes,out _,out var Error));
        Assert.Contains("outside the grid",Error);
    }
}
