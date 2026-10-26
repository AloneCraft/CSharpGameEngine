using Camera=CSharpGameEngine.Rendering.View.RenderCamera;
using System.Numerics;
using CSharpGameEngine.Rendering;
using static CSharpGameEngine.Graphics.D3D12.Tests.LumenGpuIntegrationTests;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
[Collection(D3D12GpuCollection.Name)]
public sealed class LumenSurfaceDepthTests {
    [WindowsOnlyTheory]
    [Trait("Category","GPU")]
    [InlineData(10f,300f,false)]
    [InlineData(2e7f,1e18f,false)]
    [InlineData(2e7f,1e18f,true)]
    public void SurfacePrepass_PreservesTheNearestPhysicalSurfaceWithAnInterstellarFarPlane(float Distance,float Far,bool Reverse){
        using var Scene=LumenScene.Room(WithField:false);
        var View=Camera.CreateFirstPerson(Vector3.Zero);
        View.NearZ=0.1f;
        View.FarZ=Far;
        var Near=Matrix4x4.CreateScale(Distance)*Matrix4x4.CreateTranslation(0f,0f,Distance);
        var Behind=Matrix4x4.CreateScale(Distance)*Matrix4x4.CreateTranslation(0f,0f,Distance+Distance*0.001f);
        Scene.Render(LumenSettings.Unreal,Sun:false,Sky:false,View:View,Box:Reverse?Behind:Near,Prepare:Renderer=>{
            for(var Index=0;Index<4;Index++){
                Renderer.SetObjectVisibility(Index,VisibleInView:Index==1,CastsShadow:false);
            }
            Renderer.SetObjectTransform(1,Reverse?Near:Behind);
        });
        Assert.True(Scene.Renderer.LumenActiveLastFrame);
        var (Distances,Width,Height)=Scene.Renderer.ReadLumenSurfaceDistancesForTesting();
        var MainDepth=Scene.Renderer.ReadDepthForTesting();
        var TanHalfFov=Math.Tan(View.FovYRadians*0.5d);
        for(var Y=Height/2-2;Y<=Height/2+2;Y++){
            for(var X=Width/2-2;X<=Width/2+2;X++){
                var Index=Y*Width+X;
                Assert.True(MainDepth[Index]<1f,"The main view must actually draw the box.");
                var NdcX=(X+0.5d)/Width*2d-1d;
                var NdcY=1d-(Y+0.5d)/Height*2d;
                var RayX=NdcX*TanHalfFov*Width/Height;
                var RayY=NdcY*TanHalfFov;
                var Expected=Distance*0.5d*Math.Sqrt(1d+RayX*RayX+RayY*RayY);
                Assert.InRange((double)Distances[Index],Expected*0.99998d,Expected*1.00002d);
            }
        }
        Scene.AssertNoDebugLayerErrors();
    }
}
