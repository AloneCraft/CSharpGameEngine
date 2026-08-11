namespace CSharpGameEngine.Core.Tests;

public sealed class SourceLocationSpec
{
    [Fact]
    public void Unknown_CanBeRepresented()
    {
        SourceLocation Unknown = SourceLocation.Unknown;

        Assert.True(Unknown.IsUnknown);
        Assert.Equal("<unknown>", Unknown.ToString());
        Assert.Equal(0, Unknown.Line);
        Assert.Equal(0, Unknown.Column);
    }

    [Fact]
    public void ToString_UsesSpecifiedReadableFormat()
    {
        SourceLocation Location = new("Assets/Player.scene", 12, 3);

        Assert.Equal("Assets/Player.scene(12,3)", Location.ToString());
    }
}
