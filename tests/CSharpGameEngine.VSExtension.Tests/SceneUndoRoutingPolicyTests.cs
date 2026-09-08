namespace CSharpGameEngine.VSExtension.Tests;

public sealed class SceneUndoRoutingPolicyTests {
    [Fact]
    public void UndoRedoIsRoutedFromViewport(){
        Assert.True(SceneDesignerInputRoutingPolicy.ShouldRouteUndoRedoToDesigner(
            ViewportFocused:true,AuxiliarySurfaceFocused:false,TextEntryFocused:false));
    }

    [Fact]
    public void UndoRedoIsRoutedFromAuxiliarySurface(){
        Assert.True(SceneDesignerInputRoutingPolicy.ShouldRouteUndoRedoToDesigner(
            ViewportFocused:false,AuxiliarySurfaceFocused:true,TextEntryFocused:false));
    }

    [Fact]
    public void UndoRedoIsNotRoutedOutsideDesigner(){
        Assert.False(SceneDesignerInputRoutingPolicy.ShouldRouteUndoRedoToDesigner(
            ViewportFocused:false,AuxiliarySurfaceFocused:false,TextEntryFocused:false));
    }

    [Theory]
    [InlineData(true,false)]
    [InlineData(false,true)]
    [InlineData(true,true)]
    public void TextEntryKeepsItsOwnUndoHistory(bool Viewport,bool Auxiliary){
        Assert.False(SceneDesignerInputRoutingPolicy.ShouldRouteUndoRedoToDesigner(
            Viewport,Auxiliary,TextEntryFocused:true));
    }

    [Fact]
    public void SaveIsRoutedFromAuxiliarySurface(){
        Assert.True(SceneDesignerInputRoutingPolicy.ShouldRouteSaveToDesigner(
            ViewportFocused:false,AuxiliarySurfaceFocused:true));
    }

    [Fact]
    public void SaveIsNotRoutedOutsideDesigner(){
        Assert.False(SceneDesignerInputRoutingPolicy.ShouldRouteSaveToDesigner(
            ViewportFocused:false,AuxiliarySurfaceFocused:false));
    }

    [Fact]
    public void TransactionNameCarriesTheGestureLabel(){
        string Formatted=SceneUndoTransactionNaming.Format(
            System.Globalization.CultureInfo.GetCultureInfo("en-US"),"Remove node");
        Assert.Equal("Scene Designer: Remove node",Formatted);
    }
}
