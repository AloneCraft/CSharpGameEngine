using System.Windows.Input;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class InspectorKeyboardNavigationPolicyTests {
    [Theory]
    [InlineData(Key.Up,-1)]
    [InlineData(Key.Down,1)]
    public void ArrowKeys_MoveSelectionWhenAComboBoxIsClosed(Key Key,int ExpectedDelta){
        bool Moves=InspectorKeyboardNavigationPolicy.TryGetMoveDelta(Key,false,out int Delta);

        Assert.True(Moves);
        Assert.Equal(ExpectedDelta,Delta);
    }

    [Theory]
    [InlineData(Key.Up)]
    [InlineData(Key.Down)]
    [InlineData(Key.F4)]
    public void ComboBoxNativeKeys_DoNotMoveTheSelection(Key Key){
        bool Moves=InspectorKeyboardNavigationPolicy.TryGetMoveDelta(Key,true,out int Delta);

        Assert.False(Moves);
        Assert.Equal(0,Delta);
    }

    [Fact]
    public void F4_DoesNotMoveSelectionWhenAComboBoxIsClosed(){
        bool Moves=InspectorKeyboardNavigationPolicy.TryGetMoveDelta(Key.F4,false,out int Delta);

        Assert.False(Moves);
        Assert.Equal(0,Delta);
    }

    [Fact]
    public void Enter_OpensClosedComboBox(){
        Assert.True(InspectorKeyboardNavigationPolicy.ShouldOpenDropDown(Key.Enter,true,false));
        Assert.False(InspectorKeyboardNavigationPolicy.ShouldOpenDropDown(Key.F4,true,false));
    }

    [Theory]
    [InlineData(Key.Enter,false,false)]
    [InlineData(Key.Enter,true,true)]
    [InlineData(Key.F4,true,false)]
    public void DropDownOpenPolicy_LeavesNonTargetStatesAlone(Key Key,bool IsComboBox,bool IsOpen){
        Assert.False(InspectorKeyboardNavigationPolicy.ShouldOpenDropDown(Key,IsComboBox,IsOpen));
    }
}