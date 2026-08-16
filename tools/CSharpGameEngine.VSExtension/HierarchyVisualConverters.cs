using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CSharpGameEngine.VSExtension;

public sealed class HierarchyIconSizeConverter : IValueConverter {
    public object Convert(object Value,Type TargetType,object Parameter,CultureInfo Culture){
        double FontSize=Value is double Size&&Size>0d?Size:12d;
        return Math.Max(12d,Math.Round(FontSize*4d/3d));
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
        double FontSize=Value is double Size&&Size>0d?Size:12d;
        return new GridLength(Math.Max(18d,Math.Round(FontSize*1.5d)));
    }

    public object ConvertBack(object Value,Type TargetType,object Parameter,CultureInfo Culture)=>
        DependencyProperty.UnsetValue;
}

public sealed class HierarchyIndentThicknessConverter : IValueConverter {
    public object Convert(object Value,Type TargetType,object Parameter,CultureInfo Culture){
        double FontSize=Value is double Size&&Size>0d?Size:12d;
        return new Thickness(Math.Max(18d,Math.Round(FontSize*1.5d)),0d,0d,0d);
    }

    public object ConvertBack(object Value,Type TargetType,object Parameter,CultureInfo Culture)=>
        DependencyProperty.UnsetValue;
}