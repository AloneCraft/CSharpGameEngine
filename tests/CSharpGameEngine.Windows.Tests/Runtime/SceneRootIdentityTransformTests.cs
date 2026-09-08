using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Windows;

namespace CSharpGameEngine.Windows.Tests.Runtime;

public sealed class SceneRootIdentityTransformTests {
    [Fact]
    public void Resolve_ComposesWithIdentitySceneRootThatHasNoAuthoredTransform(){
        FixedTransformWorldResolver Resolver=new();

        var Status=Resolver.Resolve(2,
            (int Id,out Transform Value)=>{
                if(Id==2){
                    Value=new Transform { Position=new LogicalVector3(0f,3f,0f) };
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
            },out FixedAffineTransform World);

        Assert.Equal(FixedTransformResolveStatus.Success,Status);
        Assert.Equal(new LogicalVector3(0f,3f,0f),new LogicalVector3(World.M41,World.M42,World.M43));
    }
}
