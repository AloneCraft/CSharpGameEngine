using System.Windows.Input;

namespace CSharpGameEngine.VSExtension;

internal static class InspectorKeyboardNavigationPolicy {
    internal static bool TryGetMoveDelta(Key Key,bool IsComboBoxDropDownOpen,out int Delta){
        if(IsComboBoxDropDownOpen||Key==Key.F4){
            Delta=0;
            return false;
        }

        Delta=Key switch {
            Key.Up=>-1,
            Key.Down=>1,
            _=>0,
        };
        return Delta!=0;
    }

    internal static bool ShouldOpenDropDown(Key Key,bool IsComboBox,bool IsDropDownOpen)=>
        Key==Key.Enter&&IsComboBox&&!IsDropDownOpen;
}