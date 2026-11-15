using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// <see cref="InspectorEditorKind"/>を、<c>ConverterParameter</c>に列挙したカンマ区切りの許可リストと
/// 突き合わせてVisibilityへ変換する。1行に4つの編集コントロール(TextBox/CheckBox/ComboBox/read-only
/// TextBlock)を重ねて置き、対応するEditorKindのものだけ見せる。
/// </summary>
public sealed class InspectorEditorKindVisibilityConverter:IValueConverter {
    public object Convert(object Value,Type TargetType,object Parameter,CultureInfo Culture){
        if(Value is not InspectorEditorKind Kind||Parameter is not string Allowed){
            return Visibility.Collapsed;
        }

        return Allowed.Split(',').Any(Name=>string.Equals(Name,Kind.ToString(),StringComparison.Ordinal))
            ?Visibility.Visible
            :Visibility.Collapsed;
    }

    public object ConvertBack(object Value,Type TargetType,object Parameter,CultureInfo Culture)=>
        DependencyProperty.UnsetValue;
}
