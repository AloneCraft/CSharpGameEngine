namespace CSharpGameEngine.Core.Tests;

public sealed class DiagnosticSpec
{
    [Fact]
    public void Constructor_PreservesAllProperties()
    {
        SourceLocation location = new("Assets/Main.scene", 2, 8);
        Diagnostic diagnostic = new("CGE0001", DiagnosticSeverity.Warning, "Sample warning.", location);

        Assert.Equal("CGE0001", diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal("Sample warning.", diagnostic.Message);
        Assert.Equal(location, diagnostic.Location);
    }

    [Fact]
    public void ToString_ContainsRequiredInformation()
    {
        Diagnostic diagnostic = new("CGE0002", DiagnosticSeverity.Error, "Critical failure.", new SourceLocation("Assets/Main.scene", 5, 9));

        string text = diagnostic.ToString();

        Assert.Contains("CGE0002", text, StringComparison.Ordinal);
        Assert.Contains("Error", text, StringComparison.Ordinal);
        Assert.Contains("Critical failure.", text, StringComparison.Ordinal);
        Assert.Contains("Assets/Main.scene(5,9)", text, StringComparison.Ordinal);
    }
}
