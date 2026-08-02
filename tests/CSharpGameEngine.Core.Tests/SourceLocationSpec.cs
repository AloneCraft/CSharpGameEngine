namespace CSharpGameEngine.Core.Tests;

public sealed class SourceLocationSpec
{
    [Fact]
    public void Unknown_CanBeRepresented()
    {
        SourceLocation unknown = SourceLocation.Unknown;

        Assert.True(unknown.IsUnknown);
        Assert.Equal("<unknown>", unknown.ToString());
        Assert.Equal(0, unknown.Line);
        Assert.Equal(0, unknown.Column);
    }

    [Fact]
    public void ToString_UsesSpecifiedReadableFormat()
    {
        SourceLocation location = new("Assets/Player.scene", 12, 3);

        Assert.Equal("Assets/Player.scene(12,3)", location.ToString());
    }
}
