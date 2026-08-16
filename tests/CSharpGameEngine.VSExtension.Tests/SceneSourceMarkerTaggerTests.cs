using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Tagging;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class SceneSourceMarkerTaggerTests {
    [Fact]
    public void MarkerFormatDefinition_HasMefConstructiblePublicConstructor(){
        Assert.NotNull(typeof(SceneSourceMarkerFormatDefinition).GetConstructor(Type.EmptyTypes));
    }

    [Fact]
    public void MarkerTag_UsesTheStandardMarkerFormatDefinitionName(){
        Assert.Equal(
            "MarkerFormatDefinition/CSharpGameEngine.SceneHierarchySource",
            SceneSourceMarkerFormatDefinition.MarkerTypeName);
        Assert.Equal(
            SceneSourceMarkerFormatDefinition.MarkerTypeName,
            SceneSourceMarkerFormatDefinition.MarkerTag.Type);
    }

    [Fact]
    public void MarkerFormatDefinition_OnlySetsTheFixedBluePurpleBackground(){
        SceneSourceMarkerFormatDefinition Definition=new();

        Assert.Equal(System.Windows.Media.Color.FromArgb(0x50,0x6A,0x3D,0xC9),Definition.BackgroundColor);
        Assert.Null(Definition.ForegroundColor);
    }

    [Fact]
    public void StandardTextMarkerProvider_ReturnsTheSharedSimpleTaggerContract(){
        var Method=typeof(ITextMarkerProviderFactory).GetMethod(
            nameof(ITextMarkerProviderFactory.GetTextMarkerTagger),[typeof(Microsoft.VisualStudio.Text.ITextBuffer)]);

        Assert.NotNull(Method);
        Assert.Equal(typeof(SimpleTagger<TextMarkerTag>),Method!.ReturnType);
    }
}