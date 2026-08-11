namespace CSharpGameEngine.Core.Tests;

public sealed class DiagnosticBagSpec
{
    [Fact]
    public void Add_PreservesInsertionOrder()
    {
        DiagnosticBag Bag = new();
        Diagnostic First = new("CGE1001", DiagnosticSeverity.Info, "A", SourceLocation.Unknown);
        Diagnostic Second = new("CGE1002", DiagnosticSeverity.Warning, "B", SourceLocation.Unknown);

        Bag.Add(First);
        Bag.Add(Second);

        Assert.Equal([First, Second], Bag.ToArray());
    }

    [Fact]
    public void HasErrors_IsTrueOnlyWhenErrorExists()
    {
        DiagnosticBag Bag = new();

        Bag.Add(new Diagnostic("CGE1003", DiagnosticSeverity.Info, "info", SourceLocation.Unknown));
        Assert.False(Bag.HasErrors);

        Bag.Add(new Diagnostic("CGE1004", DiagnosticSeverity.Error, "error", SourceLocation.Unknown));
        Assert.True(Bag.HasErrors);
    }

    [Fact]
    public void EmptyBag_IsLightweightAndUsable()
    {
        DiagnosticBag Bag = new();

        Assert.True(Bag.Count == 0);
        Assert.False(Bag.HasErrors);
        Assert.Empty(Bag);
    }
}
