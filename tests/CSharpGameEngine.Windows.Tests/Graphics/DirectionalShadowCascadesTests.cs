using System.Numerics;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
public sealed class DirectionalShadowCascadesTests {
    private static Matrix4x4 CameraViewProjection(Vector3 Eye,Vector3 Target,float FarZ)=>
        Matrix4x4.CreateLookAtLeftHanded(Eye,Target,Vector3.UnitY)*Matrix4x4.CreatePerspectiveFieldOfViewLeftHanded(1.0268f,16f/9f,0.1f,FarZ);
    [Fact]
    public void SplitDistances_FollowUnrealExponentThreeDistribution(){
        var Splits=Enumerable.Range(0,DirectionalShadowCascades.Count).Select(Cascade=>DirectionalShadowCascades.SplitDistance(Cascade,0f,200f)).ToArray();
        Assert.Equal([5f,20f,65f,200f],Splits.Select(static Value=>MathF.Round(Value,3)).ToArray());
    }
    [Fact]
    public void Build_SpheresContainTheirSlicesAndTilesAreDistinctQuadrants(){
        var Eye=new Vector3(-4f,3.1f,0f);
        var Cascades=DirectionalShadowCascades.Build(CameraViewProjection(Eye,Eye+Vector3.UnitX,1000f),Eye,Vector3.Normalize(new Vector3(-0.47f,0.87f,-0.13f)));
        Assert.Equal(DirectionalShadowCascades.Count,Cascades.Length);
        for(var Cascade=0;Cascade<Cascades.Length;Cascade++){
            var Value=Cascades[Cascade];
            var SliceMid=Eye+Vector3.UnitX*((Value.NearDistance+Value.FarDistance)*0.5f);
            Assert.True(Vector3.Distance(SliceMid,Value.Center)<Value.Radius,$"Cascade {Cascade} sphere must contain its slice centre.");
            Assert.True(Value.DepthRangeMeters>2f*Value.Radius);
            Assert.Equal(DirectionalShadowCascades.ResolutionPixels,Value.Tile.Width);
        }
        Assert.Equal(200f,Cascades[^1].FarDistance,3);
        Assert.Equal(4,Cascades.Select(static Value=>(Value.Tile.X,Value.Tile.Y)).Distinct().Count());
        Assert.True(Cascades[0].Radius<Cascades[1].Radius&&Cascades[1].Radius<Cascades[3].Radius);
    }
    [Fact]
    public void Build_ProjectsTheSliceCentreInsideTheShadowMap(){
        var Eye=new Vector3(0f,2f,-6f);
        var Toward=Vector3.Normalize(new Vector3(0.3f,0.8f,0.2f));
        foreach(var Value in DirectionalShadowCascades.Build(CameraViewProjection(Eye,Eye+Vector3.UnitZ,300f),Eye,Toward)){
            var Clip=Vector4.Transform(new Vector4(Value.Center,1f),Value.ViewProjection);
            Assert.InRange(Clip.X/Clip.W,-0.01f,0.01f);
            Assert.InRange(Clip.Y/Clip.W,-0.01f,0.01f);
            Assert.InRange(Clip.Z/Clip.W,0f,1f);
        }
    }
}
