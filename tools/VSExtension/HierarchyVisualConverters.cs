using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CSharpGameEngine.VSExtension;

public sealed class HierarchyIconSizeConverter : IValueConverter {
    public object Convert(object Value,Type TargetType,object Parameter,CultureInfo Culture){
        float FontSize=Value is double Size&&FloatMath.TryFromExternal(Size,out var Projected)&&Projected>0f?Projected:12f;
        return Math.Max(12f,FloatMath.Round(FontSize*4f/3f));
    }

    public object ConvertBack(object Value,Type TargetType,object Parameter,CultureInfo Culture)=>
        DependencyProperty.UnsetValue;
}

public sealed class InverseBooleanToVisibilityConverter : IValueConverter {
    public object Convert(object Value,Type TargetType,object Parameter,CultureInfo Culture)=>
        Value is bool IsVisible&&IsVisible?Visibility.Collapsed:Visibility.Visible;

    public object ConvertBack(object Value,Type TargetType,object Parameter,CultureInfo Culture)=>
        DependencyProperty.UnsetValue;
}
public sealed class HierarchyIndentSizeConverter : IValueConverter {
    public object Convert(object Value,Type TargetType,object Parameter,CultureInfo Culture){
        float FontSize=Value is double Size&&FloatMath.TryFromExternal(Size,out var Projected)&&Projected>0f?Projected:12f;
        return new GridLength(Math.Max(18f,FloatMath.Round(FontSize*1.5f)));
    }

    public object ConvertBack(object Value,Type TargetType,object Parameter,CultureInfo Culture)=>
        DependencyProperty.UnsetValue;
}

public sealed class HierarchyIndentThicknessConverter : IValueConverter {
    public object Convert(object Value,Type TargetType,object Parameter,CultureInfo Culture){
        float FontSize=Value is double Size&&FloatMath.TryFromExternal(Size,out var Projected)&&Projected>0f?Projected:12f;
        return new Thickness(Math.Max(18f,FloatMath.Round(FontSize*1.5f)),0f,0f,0f);
    }

    public object ConvertBack(object Value,Type TargetType,object Parameter,CultureInfo Culture)=>
        DependencyProperty.UnsetValue;
}
