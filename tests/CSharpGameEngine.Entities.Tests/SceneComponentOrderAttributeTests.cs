using System;
using CSharpGameEngine.Entities;

namespace CSharpGameEngine.Entities.Tests;

public sealed class SceneComponentOrderAttributeTests {
    [Fact]
    public void Categories_FollowTheStableFunctionalNormalizationOrder(){
        Assert.Equal(0,(int)SceneComponentCategory.Spatial);
        Assert.Equal(1,(int)SceneComponentCategory.Camera);
        Assert.Equal(2,(int)SceneComponentCategory.LightingEnvironment);
        Assert.Equal(3,(int)SceneComponentCategory.Rendering);
        Assert.Equal(4,(int)SceneComponentCategory.Physics);
        Assert.Equal(5,(int)SceneComponentCategory.Audio);
        Assert.Equal(6,(int)SceneComponentCategory.Gameplay);
        Assert.Equal(7,(int)SceneComponentCategory.Runtime);
    }

    [Fact]
    public void Constructor_StoresCategoryAndExplicitOrder(){
        SceneComponentOrderAttribute Attribute=new(SceneComponentCategory.LightingEnvironment,42);

        Assert.Equal(SceneComponentCategory.LightingEnvironment,Attribute.Category);
        Assert.Equal(42,Attribute.Order);
    }

    [Fact]
    public void Usage_IsRestrictedToOneStructAttribute(){
        var Usage=Assert.IsType<AttributeUsageAttribute>(
            System.Attribute.GetCustomAttribute(typeof(SceneComponentOrderAttribute),typeof(AttributeUsageAttribute)));

        Assert.Equal(AttributeTargets.Struct,Usage.ValidOn);
        Assert.False(Usage.AllowMultiple);
        Assert.False(Usage.Inherited);
    }
}
