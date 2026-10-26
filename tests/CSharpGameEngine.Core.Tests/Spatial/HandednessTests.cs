using CSharpGameEngine.Spatial.Abstractions;
using CSharpGameEngine.Spatial.Blender;
using CSharpGameEngine.Spatial.Fbx;
using CSharpGameEngine.Spatial.Gltf;
using CSharpGameEngine.Spatial.Unity;
using CSharpGameEngine.Spatial.Unreal;

namespace CSharpGameEngine.Spatial.Tests;

/// <summary>
/// Docs/RUF-Semantic-Coordinate-Specification-Prompt.md の「Handedness Tests」と Docs/Specifications/Spatial-RUF.md の「Handedness の定義」：左手系の規約では <c>Cross(Right, Up) == Forward</c>、右手系の規約では <c>Cross(Right, Up) == Backward</c>。<c>RightFromUpForward</c> は、どちらの左右系でも <c>Right</c> を返さなければならない。
/// </summary>
public class HandednessTests {
    [Fact]
    public void Unity_LeftHanded_CrossRightUp_IsForward(){
        Assert.Equal(CoordinateHandedness.LeftHanded,UnityCoordinateConvention.Handedness);
        Assert.Equal(RufVector.Forward,Spatial<UnityCoordinateConvention>.Cross(RufVector.Right,RufVector.Up));
    }

    [Fact]
    public void Unreal_LeftHanded_CrossRightUp_IsForward(){
        Assert.Equal(CoordinateHandedness.LeftHanded,UnrealCoordinateConvention.Handedness);
        Assert.Equal(RufVector.Forward,Spatial<UnrealCoordinateConvention>.Cross(RufVector.Right,RufVector.Up));
    }

    [Fact]
    public void Blender_RightHanded_CrossRightUp_IsBackward(){
        Assert.Equal(CoordinateHandedness.RightHanded,BlenderWorldConvention.Handedness);
        Assert.Equal(RufVector.Backward,Spatial<BlenderWorldConvention>.Cross(RufVector.Right,RufVector.Up));
    }

    [Fact]
    public void Gltf_RightHanded_CrossRightUp_IsBackward(){
        Assert.Equal(CoordinateHandedness.RightHanded,GltfCoordinateConvention.Handedness);
        Assert.Equal(RufVector.Backward,Spatial<GltfCoordinateConvention>.Cross(RufVector.Right,RufVector.Up));
    }

    [Fact]
    public void Fbx_RightHanded_CrossRightUp_IsBackward(){
        Assert.Equal(CoordinateHandedness.RightHanded,FbxCoordinateConvention.Handedness);
        Assert.Equal(RufVector.Backward,Spatial<FbxCoordinateConvention>.Cross(RufVector.Right,RufVector.Up));
    }

    [Fact]
    public void Unity_RightFromUpForward_IsAlwaysRight()=>AssertRightFromUpForward<UnityCoordinateConvention>();

    [Fact]
    public void Unreal_RightFromUpForward_IsAlwaysRight()=>AssertRightFromUpForward<UnrealCoordinateConvention>();

    [Fact]
    public void Blender_RightFromUpForward_IsAlwaysRight()=>AssertRightFromUpForward<BlenderWorldConvention>();

    [Fact]
    public void Gltf_RightFromUpForward_IsAlwaysRight()=>AssertRightFromUpForward<GltfCoordinateConvention>();

    [Fact]
    public void Fbx_RightFromUpForward_IsAlwaysRight()=>AssertRightFromUpForward<FbxCoordinateConvention>();

    private static void AssertRightFromUpForward<TConvention>()
        where TConvention:struct,ICoordinateConvention {
        var Right=Spatial<TConvention>.RightFromUpForward(RufVector.Up,RufVector.Forward);

        Assert.Equal(RufVector.Right.R,Right.R,precision:4);
        Assert.Equal(RufVector.Right.U,Right.U,precision:4);
        Assert.Equal(RufVector.Right.F,Right.F,precision:4);
    }
}
