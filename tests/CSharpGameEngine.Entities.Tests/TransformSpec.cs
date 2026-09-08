using CSharpGameEngine.Contracts;
using CSharpGameEngine.Numerics;

namespace CSharpGameEngine.Entities.Tests;

public sealed class TransformSpec {
    [Fact]
    public void NewTransform_IsAFrameLocalLogicalIdentity(){
        Transform Transform=new();

        Assert.Equal(ReferenceFrameId.Root,Transform.Frame);
        Assert.Equal(LogicalVector3.Zero,Transform.Position);
        Assert.Equal(LogicalQuaternion.Identity,Transform.Rotation);
        Assert.Equal(LogicalVector3.One,Transform.Scale);
    }

    [Fact]
    public void AuthoritativeFields_HaveOnlyCanonicalLogicalTypes(){
        var Fields=typeof(Transform).GetFields().ToDictionary(static Field=>Field.Name,static Field=>Field.FieldType);

        Assert.Equal(typeof(ReferenceFrameId),Fields[nameof(Transform.Frame)]);
        Assert.Equal(typeof(LogicalVector3),Fields[nameof(Transform.Position)]);
        Assert.Equal(typeof(LogicalQuaternion),Fields[nameof(Transform.Rotation)]);
        Assert.Equal(typeof(LogicalVector3),Fields[nameof(Transform.Scale)]);
        Assert.DoesNotContain(Fields.Values,static Type=>Type==typeof(float)||Type==typeof(double));
    }
}
