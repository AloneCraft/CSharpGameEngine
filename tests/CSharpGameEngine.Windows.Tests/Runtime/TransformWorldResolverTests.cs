using System.Collections.Generic;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Windows;

namespace CSharpGameEngine.Windows.Tests.Runtime;

public sealed class TransformWorldResolverTests {
    [Fact]
    public void Resolve_ComposesLocalTransformWithAllParentsAndCachesTheResult(){
        Dictionary<int,Transform>Transforms=new(){
            [1]=new Transform { Position=new LogicalVector3(10f,0f,0f) },
            [2]=new Transform { Position=new LogicalVector3(0f,3f,0f) },
            [3]=new Transform { Position=new LogicalVector3(0f,0f,2f) },
        };
        Dictionary<int,int>Parents=new(){ [2]=1,[3]=2 };
        var TransformLookups=0;
        FixedTransformWorldResolver Resolver=new();

        var First=Resolver.Resolve(3,
                                   (int Id,out Transform Value)=>{ TransformLookups++;return Transforms.TryGetValue(Id,out Value); },
                                   (int Id,out int Parent)=>Parents.TryGetValue(Id,out Parent),out var World);
        var Cached=Resolver.Resolve(3,
                                    (int Id,out Transform Value)=>{ TransformLookups++;return Transforms.TryGetValue(Id,out Value); },
                                    (int Id,out int Parent)=>Parents.TryGetValue(Id,out Parent),out var CachedWorld);

        Assert.Equal(FixedTransformResolveStatus.Success,First);
        Assert.Equal(FixedTransformResolveStatus.Success,Cached);
        Assert.Equal(new LogicalVector3(10f,3f,2f),new LogicalVector3(World.M41,World.M42,World.M43));
        Assert.Equal(World,CachedWorld);
        Assert.Equal(3,TransformLookups);
    }

    [Fact]
    public void Resolve_ReturnsDeadParentWhenParentHasNoTransform(){
        Dictionary<int,Transform>Transforms=new(){ [2]=new Transform() };
        FixedTransformWorldResolver Resolver=new();

        var Status=Resolver.Resolve(2,
                                    (int Id,out Transform Value)=>Transforms.TryGetValue(Id,out Value),
                                    (int Id,out int Parent)=>{ Parent=1;return true; },out _);

        Assert.Equal(FixedTransformResolveStatus.DeadParent,Status);
    }

    [Fact]
    public void Resolve_DetectsHierarchyCycle(){
        Dictionary<int,Transform>Transforms=new(){ [1]=new Transform(),[2]=new Transform() };
        Dictionary<int,int>Parents=new(){ [1]=2,[2]=1 };
        FixedTransformWorldResolver Resolver=new();

        var Status=Resolver.Resolve(1,
                                    (int Id,out Transform Value)=>Transforms.TryGetValue(Id,out Value),
                                    (int Id,out int Parent)=>Parents.TryGetValue(Id,out Parent),out _);

        Assert.Equal(FixedTransformResolveStatus.Cycle,Status);
    }
}
