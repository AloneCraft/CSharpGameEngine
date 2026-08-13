using System.Collections.Generic;
using System.Numerics;
using CSharpGameEngine.Runtime.Windows;

namespace CSharpGameEngine.Windows.Tests.Runtime;

public sealed class TransformWorldResolverTests {
    [Fact]
    public void Resolve_ComposesLocalTransformWithAllParentsAndCachesTheResult(){
        Dictionary<int,Transform>Transforms=new(){
            [1]=new Transform { PositionX=10f },
            [2]=new Transform { PositionY=3f },
            [3]=new Transform { PositionZ=2f },
        };
        Dictionary<int,int>Parents=new(){ [2]=1,[3]=2 };
        int TransformLookups=0;
        TransformWorldResolver Resolver=new();

        TransformResolveStatus First=Resolver.Resolve(3,
            (int Id,out Transform Value)=>{ TransformLookups++;return Transforms.TryGetValue(Id,out Value); },
            (int Id,out int Parent)=>Parents.TryGetValue(Id,out Parent),out Matrix4x4 World);
        TransformResolveStatus Cached=Resolver.Resolve(3,
            (int Id,out Transform Value)=>{ TransformLookups++;return Transforms.TryGetValue(Id,out Value); },
            (int Id,out int Parent)=>Parents.TryGetValue(Id,out Parent),out Matrix4x4 CachedWorld);

        Assert.Equal(TransformResolveStatus.Success,First);
        Assert.Equal(TransformResolveStatus.Success,Cached);
        Assert.Equal(new Vector3(10f,3f,2f),World.Translation);
        Assert.Equal(World,CachedWorld);
        Assert.Equal(3,TransformLookups);
    }

    [Fact]
    public void Resolve_ReturnsDeadParentWhenParentHasNoTransform(){
        Dictionary<int,Transform>Transforms=new(){ [2]=new Transform() };
        TransformWorldResolver Resolver=new();

        TransformResolveStatus Status=Resolver.Resolve(2,
            (int Id,out Transform Value)=>Transforms.TryGetValue(Id,out Value),
            (int Id,out int Parent)=>{ Parent=1;return true; },out _);

        Assert.Equal(TransformResolveStatus.DeadParent,Status);
    }

    [Fact]
    public void Resolve_DetectsHierarchyCycle(){
        Dictionary<int,Transform>Transforms=new(){ [1]=new Transform(),[2]=new Transform() };
        Dictionary<int,int>Parents=new(){ [1]=2,[2]=1 };
        TransformWorldResolver Resolver=new();

        TransformResolveStatus Status=Resolver.Resolve(1,
            (int Id,out Transform Value)=>Transforms.TryGetValue(Id,out Value),
            (int Id,out int Parent)=>Parents.TryGetValue(Id,out Parent),out _);

        Assert.Equal(TransformResolveStatus.Cycle,Status);
    }
}
