namespace CSharpGameEngine.Core.Tests;

public sealed class DiagnosticBagSpec
{
    [Fact]
    public void Add_PreservesInsertionOrder()
    {
        DiagnosticBag bag = new();
        Diagnostic first = new("CGE1001", DiagnosticSeverity.Info, "A", SourceLocation.Unknown);
        Diagnostic second = new("CGE1002", DiagnosticSeverity.Warning, "B", SourceLocation.Unknown);

        bag.Add(first);
        bag.Add(second);

        Assert.Equal([first, second], bag.ToArray());
    }

    [Fact]
    public void HasErrors_IsTrueOnlyWhenErrorExists()
    {
        DiagnosticBag bag = new();

        bag.Add(new Diagnostic("CGE1003", DiagnosticSeverity.Info, "info", SourceLocation.Unknown));
        Assert.False(bag.HasErrors);

        bag.Add(new Diagnostic("CGE1004", DiagnosticSeverity.Error, "error", SourceLocation.Unknown));
        Assert.True(bag.HasErrors);
    }

    [Fact]
    public void EmptyBag_IsLightweightAndUsable()
    {
        DiagnosticBag bag = new();

        Assert.True(bag.Count == 0);
        Assert.False(bag.HasErrors);
        Assert.Empty(bag);
    }
}
