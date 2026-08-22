using System.Numerics;
using CSharpGameEngine.Runtime.Windows;
using Logical=CSharpGameEngine.Numerics.Logical256;

namespace CSharpGameEngine.Windows.Tests.Runtime;

public sealed class SceneRootIdentityTransformTests {
    [Fact]
    public void Resolve_ComposesWithIdentitySceneRootThatHasNoAuthoredTransform(){
        TransformWorldResolver Resolver=new();

        var Status=Resolver.Resolve(2,
            (int Id,out Transform Value)=>{
                if(Id==2){
                    Value=new Transform { Position=new Logical.Vector3(0f,3f,0f) };
                    return true;
                }
                if(Id==1){
                    // Logical Scene roots may own environment data without an authored Transform.
                    Value=new Transform();
                    return true;
                }
                Value=default;
                return false;
            },
            (int Id,out int Parent)=>{
                Parent=1;
                return Id==2;
            },out Matrix4x4 World);

        Assert.Equal(TransformResolveStatus.Success,Status);
        Assert.Equal(new Vector3(0f,3f,0f),World.Translation);
    }
}
