using System;
using CSharpGameEngine.Entities;

namespace CSharpGameEngine.Entities.Tests;

public sealed class TransformPrecisionAttributeTests {
    [Fact]
    public void Constructor_StoresTheSourcePrecisionConstants(){
        TransformPrecisionAttribute Attribute=new(384,173);

        Assert.Equal(384,Attribute.TotalBits);
        Assert.Equal(173,Attribute.FractionalBits);
    }

    [Fact]
    public void Usage_IsRestrictedToOneAssemblyAttribute(){
        var Usage=Assert.IsType<AttributeUsageAttribute>(
            System.Attribute.GetCustomAttribute(typeof(TransformPrecisionAttribute),typeof(AttributeUsageAttribute)));

        Assert.Equal(AttributeTargets.Assembly,Usage.ValidOn);
        Assert.False(Usage.AllowMultiple);
        Assert.False(Usage.Inherited);
    }
}
