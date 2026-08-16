using System;
using CSharpGameEngine.Entities;

namespace CSharpGameEngine.Entities.Tests;

public sealed class SceneComponentIconAttributeTests {
    [Fact]
    public void Constructor_StoresTheProjectRelativePath(){
        SceneComponentIconAttribute IconAttribute=new("Assets/Icons/component.svg");

        Assert.Equal("Assets/Icons/component.svg",IconAttribute.ProjectRelativePath);
    }

    [Fact]
    public void Usage_IsRestrictedToOneStructAttribute(){
        AttributeUsageAttribute Usage=Assert.IsType<AttributeUsageAttribute>(
            System.Attribute.GetCustomAttribute(typeof(SceneComponentIconAttribute),typeof(AttributeUsageAttribute)));

        Assert.Equal(AttributeTargets.Struct,Usage.ValidOn);
        Assert.False(Usage.AllowMultiple);
        Assert.False(Usage.Inherited);
    }
}
