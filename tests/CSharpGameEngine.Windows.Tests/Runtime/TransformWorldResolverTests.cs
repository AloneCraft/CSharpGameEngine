using System.Collections.Generic;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Windows;

namespace CSharpGameEngine.Windows.Tests.Runtime;

public sealed class TransformWorldResolverTests {
    [Fact]
    public void Resolve_ComposesLocalTransformWithAllParentsAndCachesTheResult(){
        Dictionary<Entity,Transform>Transforms=new(){
            [new Entity(1)]=new Transform { Position=new LogicalVector3(10f,0f,0f) },
            [new Entity(2)]=new Transform { Position=new LogicalVector3(0f,3f,0f) },
            [new Entity(3)]=new Transform { Position=new LogicalVector3(0f,0f,2f) },
        };
        Dictionary<Entity,Entity>Parents=new(){ [new Entity(2)]=new Entity(1),[new Entity(3)]=new Entity(2) };
        var TransformLookups=0;
        FixedTransformWorldResolver Resolver=new();

        var First=Resolver.Resolve(new Entity(3),
                                   (Entity Id,out Transform Value)=>{ TransformLookups++;return Transforms.TryGetValue(Id,out Value); },
                                   (Entity Id,out Entity Parent)=>Parents.TryGetValue(Id,out Parent),out var World);
        var Cached=Resolver.Resolve(new Entity(3),
                                    (Entity Id,out Transform Value)=>{ TransformLookups++;return Transforms.TryGetValue(Id,out Value); },
                                    (Entity Id,out Entity Parent)=>Parents.TryGetValue(Id,out Parent),out var CachedWorld);

        Assert.Equal(FixedTransformResolveStatus.Success,First);
        Assert.Equal(FixedTransformResolveStatus.Success,Cached);
        Assert.Equal(new LogicalVector3(10f,3f,2f),new LogicalVector3(World.M41,World.M42,World.M43));
        Assert.Equal(World,CachedWorld);
        Assert.Equal(3,TransformLookups);
    }

    [Fact]
    public void Resolve_ReturnsDeadParentWhenParentHasNoTransform(){
        Dictionary<Entity,Transform>Transforms=new(){ [new Entity(2)]=new Transform() };
        FixedTransformWorldResolver Resolver=new();

        var Status=Resolver.Resolve(new Entity(2),
                                    (Entity Id,out Transform Value)=>Transforms.TryGetValue(Id,out Value),
                                    (Entity Id,out Entity Parent)=>{ Parent=new Entity(1);return true; },out _);

        Assert.Equal(FixedTransformResolveStatus.DeadParent,Status);
    }

    [Fact]
    public void Resolve_DetectsHierarchyCycle(){
        Dictionary<Entity,Transform>Transforms=new(){ [new Entity(1)]=new Transform(),[new Entity(2)]=new Transform() };
        Dictionary<Entity,Entity>Parents=new(){ [new Entity(1)]=new Entity(2),[new Entity(2)]=new Entity(1) };
        FixedTransformWorldResolver Resolver=new();

        var Status=Resolver.Resolve(new Entity(1),
                                    (Entity Id,out Transform Value)=>Transforms.TryGetValue(Id,out Value),
                                    (Entity Id,out Entity Parent)=>Parents.TryGetValue(Id,out Parent),out _);

        Assert.Equal(FixedTransformResolveStatus.Cycle,Status);
    }
}
