using System.Numerics;
namespace CSharpGameEngine.Rendering.Tests;
public sealed class VirtualDirectionalShadowTests {
    [Fact]
    public void SoftFilterRequestsItsHaloWithoutInvalidatingExistingDepth(){
        var Cache=new VirtualDirectionalShadowCache();var Receiver=new VirtualShadowBounds(0,new(1.95f,0f,1.95f),new(2.05f,0f,2.05f),true,false);
        Cache.Plan(Vector3.Zero,Vector3.UnitY,[Receiver]);var Before=Cache.Statistics.Requested;
        Cache.Plan(Vector3.Zero,Vector3.UnitY,[Receiver],FilterRadiusMeters:4f);
        Assert.True(Cache.Statistics.Requested>Before);Assert.True(Cache.Statistics.Reused>0);Assert.Equal(0,Cache.Statistics.Missing);
        Cache.Plan(Vector3.Zero,Vector3.UnitY,[Receiver],FilterRadiusMeters:4f);Assert.Equal(0,Cache.Statistics.Rendered);
        Assert.Throws<ArgumentOutOfRangeException>(()=>Cache.Plan(Vector3.Zero,Vector3.UnitY,[Receiver],FilterRadiusMeters:float.NaN));
    }
    [Theory]
    [InlineData(-1f,2f)]
    [InlineData(float.NaN,2f)]
    [InlineData(11f,2f)]
    [InlineData(1f,0f)]
    [InlineData(1f,float.PositiveInfinity)]
    public void SoftSettingsRejectInvalidPhysicalQuantities(float Diameter,float Radius){
        Assert.False(new DirectionalShadowSettings(DirectionalShadowMode.Virtual,SoftShadows:true,SourceAngularDiameterDegrees:Diameter,MaximumSoftShadowRadiusMeters:Radius).IsValid);
        Assert.True(new DirectionalShadowSettings(DirectionalShadowMode.Virtual,SoftShadows:true,SourceAngularDiameterDegrees:0f).IsValid);
    }
    private static readonly Vector3 Sun=Vector3.UnitY;
    private static VirtualShadowBounds Ground=>new(0,new(-4f,0f,-4f),new(4f,0f,4f),true,false);
    [Fact]
    public void StationaryReceiversReuseDepthAndTable(){
        var Cache=new VirtualDirectionalShadowCache();Cache.Plan(Vector3.Zero,Sun,[Ground]);
        var Table=Cache.PageTable.ToArray();Assert.True(Cache.Statistics.Rendered>0);Assert.Equal(0,Cache.Statistics.Missing);
        Cache.Plan(Vector3.Zero,Sun,[Ground]);Assert.Equal(0,Cache.Statistics.Rendered);Assert.True(Cache.Statistics.Reused>0);Assert.Equal(Table,Cache.PageTable.ToArray());
    }
    [Fact]
    public void PageKeysSurviveCameraPanAcrossNegativeOrigin(){
        var Cache=new VirtualDirectionalShadowCache();Cache.Plan(new(-0.1f,0f,0f),Sun,[Ground]);
        Cache.Plan(new(2.1f,0f,0f),Sun,[Ground]);Assert.Equal(0,Cache.Statistics.Rendered);Assert.Equal(0,Cache.Statistics.Missing);
    }
    [Fact]
    public void MovementRemovalAndDeformationInvalidateCachedCasters(){
        var Cache=new VirtualDirectionalShadowCache();var Caster=new VirtualShadowBounds(1,new(-1f,1f,-1f),new(1f,2f,1f),false,true);
        Cache.Plan(Vector3.Zero,Sun,[Ground,Caster]);Cache.Plan(Vector3.Zero,Sun,[Ground,Caster]);Assert.Equal(0,Cache.Statistics.Rendered);
        var Moved=Caster with { Minimum=Caster.Minimum+Vector3.UnitX,Maximum=Caster.Maximum+Vector3.UnitX };
        Cache.Plan(Vector3.Zero,Sun,[Ground,Moved]);Assert.True(Cache.Statistics.Rendered>0);
        Cache.Plan(Vector3.Zero,Sun,[Ground]);Assert.True(Cache.Statistics.Rendered>0);
        Cache.Plan(Vector3.Zero,Sun,[Ground,Caster with { Deforming=true }]);Cache.Plan(Vector3.Zero,Sun,[Ground,Caster with { Deforming=true }]);Assert.True(Cache.Statistics.Rendered>0);
    }
    [Fact]
    public void OverflowIsObservableAndNeverAliasesRequestedPages(){
        var Cache=new VirtualDirectionalShadowCache();Cache.Plan(Vector3.Zero,Sun,[Ground],2);
        Assert.Equal(2,Cache.Statistics.Resident);Assert.True(Cache.Statistics.Missing>0);
        Assert.Equal(Cache.Statistics.Requested,Cache.Statistics.Rendered+Cache.Statistics.Missing);
        var Values=Cache.PageTable.ToArray().Where(Value=>Value!=0).ToArray();Assert.Equal(Values.Length,Values.Distinct().Count());
    }
    [Fact]
    public void LightAndDepthOriginChangesRebuildDepth(){
        var Cache=new VirtualDirectionalShadowCache();Cache.Plan(Vector3.Zero,Sun,[Ground]);Cache.Plan(Vector3.Zero,Vector3.Normalize(new(1f,2f,0f)),[Ground]);
        Assert.True(Cache.Statistics.Rendered>0);Assert.Equal(0,Cache.Statistics.Reused);
        Cache.Plan(new(0f,600f,0f),Vector3.Normalize(new(1f,2f,0f)),[Ground]);Assert.Equal(0,Cache.Statistics.Reused);
    }
    [Fact]
    public void BudgetAndDirectionAreValidated(){
        var Cache=new VirtualDirectionalShadowCache();Assert.Throws<ArgumentOutOfRangeException>(()=>Cache.Plan(Vector3.Zero,Sun,[Ground],0));
        Assert.Throws<ArgumentException>(()=>Cache.Plan(Vector3.Zero,Vector3.Zero,[Ground]));
        Assert.False(new DirectionalShadowSettings((DirectionalShadowMode)99).IsValid);
    }
    [Fact]
    public void RotationWithUnchangedBoundsAndSlotReuseInvalidateDepth(){
        var Cache=new VirtualDirectionalShadowCache();var Caster=new VirtualShadowBounds(1,new(-1f),new(1f),true,true,Transform:Matrix4x4.Identity);
        Cache.Plan(Vector3.Zero,Sun,[Ground,Caster]);Cache.Plan(Vector3.Zero,Sun,[Ground,Caster]);Assert.Equal(0,Cache.Statistics.Rendered);
        Caster=Caster with { Transform=Matrix4x4.CreateRotationY(MathF.PI/2) };
        Cache.Plan(Vector3.Zero,Sun,[Ground,Caster]);Assert.True(Cache.Statistics.Invalidated>0);
        Caster=Caster with { GeometryId=2 };Cache.Plan(Vector3.Zero,Sun,[Ground,Caster]);Assert.True(Cache.Statistics.Invalidated>0);
    }
    [Fact]
    public void UnrequestedPagesCanBeEvictedAndRevisitedWithoutAliasing(){
        var Cache=new VirtualDirectionalShadowCache();var First=new VirtualShadowBounds(0,Vector3.Zero,Vector3.Zero,true,false);
        Cache.Plan(Vector3.Zero,Sun,[First],16);Assert.Equal(0,Cache.Statistics.Missing);
        var Second=First with { Minimum=new(20f,0f,20f),Maximum=new(20f,0f,20f) };
        Cache.Plan(Vector3.Zero,Sun,[Second],16);Assert.Equal(0,Cache.Statistics.Missing);Assert.True(Cache.Statistics.Rendered>0);
        Cache.Plan(Vector3.Zero,Sun,[First],16);Assert.Equal(0,Cache.Statistics.Missing);Assert.True(Cache.Statistics.Rendered>0);
    }
    [Fact]
    public void DepthBinBoundaryRetainsReceiverAndOffscreenCasterMargin(){
        var Cache=new VirtualDirectionalShadowCache();Cache.Plan(new(0f,255f,0f),Sun,[new(0,new(0f,455f,0f),new(0f,455f,0f),true,false)]);
        foreach(var Height in new[]{455f,655f}){
            var Clip=Vector4.Transform(new Vector4(0f,Height,0f,1f),Cache.Projections[0]);Assert.InRange(Clip.Z,0f,1f);
        }
        Cache.SuspendFrame();Assert.Equal(0,Cache.Statistics.Requested);Assert.Equal(0,Cache.Statistics.Rendered);Assert.True(Cache.Statistics.Resident>0);
    }
}
