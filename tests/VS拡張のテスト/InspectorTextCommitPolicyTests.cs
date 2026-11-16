namespace CSharpGameEngine.VSExtension.Tests;

public sealed class InspectorTextCommitPolicyTests {
    [Theory]
    [InlineData("-19f", "-19f")]
    [InlineData("Hello", "Hello")]
    public void UnchangedTextDoesNotCommitWhenFocusMoves(string Original,string Current){
        Assert.False(InspectorTextCommitPolicy.HasChanged(Original,Current));
    }

    [Theory]
    [InlineData("-19f", "0f")]
    [InlineData("Hello", "World")]
    public void ChangedTextCommitsWhenFocusMoves(string Original,string Current){
        Assert.True(InspectorTextCommitPolicy.HasChanged(Original,Current));
    }
}