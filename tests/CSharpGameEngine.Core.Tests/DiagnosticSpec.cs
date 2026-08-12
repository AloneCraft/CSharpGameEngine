namespace CSharpGameEngine.Core.Tests;

public sealed class DiagnosticSpec {
    [Fact]
    public void Constructor_PreservesAllProperties(){
        SourceLocation Location=new("Assets/Main.scene",2,8);
        Diagnostic Diagnostic=new("CGE0001",DiagnosticSeverity.Warning,"Sample warning.",Location);

        Assert.Equal("CGE0001",Diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Warning,Diagnostic.Severity);
        Assert.Equal("Sample warning.",Diagnostic.Message);
        Assert.Equal(Location,Diagnostic.Location);
    }

    [Fact]
    public void ToString_ContainsRequiredInformation(){
        Diagnostic Diagnostic=new("CGE0002",DiagnosticSeverity.Error,"Critical failure.",new SourceLocation("Assets/Main.scene",5,9));

        var Text=Diagnostic.ToString();

        Assert.Contains("CGE0002",Text,StringComparison.Ordinal);
        Assert.Contains("Error",Text,StringComparison.Ordinal);
        Assert.Contains("Critical failure.",Text,StringComparison.Ordinal);
        Assert.Contains("Assets/Main.scene(5,9)",Text,StringComparison.Ordinal);
    }
}
