namespace CSharpGameEngine.Core.Tests;

public sealed class DiagnosticSpec {
    [Fact]
    public void Constructor_PreservesAllProperties(){
        SourceLocation Location=new("Assets/Main.scene",2,8);
        Diagnostic diagnostic=new("CGE0001",DiagnosticSeverity.Warning,"Sample warning.",Location);

        Assert.Equal("CGE0001",diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Warning,diagnostic.Severity);
        Assert.Equal("Sample warning.",diagnostic.Message);
        Assert.Equal(Location,diagnostic.Location);
    }

    [Fact]
    public void ToString_ContainsRequiredInformation(){
        Diagnostic diagnostic=new("CGE0002",DiagnosticSeverity.Error,"Critical failure.",new SourceLocation("Assets/Main.scene",5,9));

        var Text=diagnostic.ToString();

        Assert.Contains("CGE0002",Text,StringComparison.Ordinal);
        Assert.Contains("Error",Text,StringComparison.Ordinal);
        Assert.Contains("Critical failure.",Text,StringComparison.Ordinal);
        Assert.Contains("Assets/Main.scene(5,9)",Text,StringComparison.Ordinal);
    }
}
