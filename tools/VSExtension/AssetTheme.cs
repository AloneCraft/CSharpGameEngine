using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.Shell;
namespace CSharpGameEngine.VSExtension;
internal static class AssetTheme {
    internal static void Apply(FrameworkElement Element){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(Element is Window Window){
            if(Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(Microsoft.VisualStudio.Shell.Interop.SVsUIShell)) is Microsoft.VisualStudio.Shell.Interop.IVsUIShell Shell){
                Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(Shell.GetDialogOwnerHwnd(out var Owner));
                new System.Windows.Interop.WindowInteropHelper(Window).Owner=Owner;
            }
            Window.WindowStartupLocation=WindowStartupLocation.CenterOwner;
        }
        if(Element is Control Control){
            Control.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty,VsBrushes.WindowKey);
            Control.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty,VsBrushes.WindowTextKey);
        }
        foreach(var Entry in new (Type Type,object Key)[]{
            (typeof(Button),VsResourceKeys.ButtonStyleKey),(typeof(CheckBox),VsResourceKeys.CheckBoxStyleKey),
            (typeof(TextBox),VsResourceKeys.TextBoxStyleKey),(typeof(ComboBox),VsResourceKeys.ComboBoxStyleKey),
            (typeof(ComboBoxItem),VsResourceKeys.ComboBoxItemStyleKey),(typeof(ContextMenu),VsResourceKeys.ContextMenuStyleKey),
            (typeof(ScrollViewer),VsResourceKeys.ScrollViewerStyleKey)}){
            if(Application.Current?.TryFindResource(Entry.Key) is Style BaseStyle){ Element.Resources[Entry.Type]=new Style(Entry.Type,BaseStyle); }
        }
        foreach(var Type in new[]{typeof(ListBox),typeof(ListView),typeof(GridViewColumnHeader)}){
            var Style=new Style(Type);
            Style.Setters.Add(new Setter(Control.BackgroundProperty,new DynamicResourceExtension(VsBrushes.WindowKey)));
            Style.Setters.Add(new Setter(Control.ForegroundProperty,new DynamicResourceExtension(VsBrushes.WindowTextKey)));
            Style.Setters.Add(new Setter(Control.BorderBrushProperty,new DynamicResourceExtension(VsBrushes.InactiveBorderKey)));
            Element.Resources[Type]=Style;
        }
        foreach(var Type in new[]{typeof(ListBoxItem),typeof(ListViewItem)}){
            var Style=new Style(Type);
            Style.Setters.Add(new Setter(Control.ForegroundProperty,new DynamicResourceExtension(VsBrushes.WindowTextKey)));
            var Selected=new Trigger{Property=ListBoxItem.IsSelectedProperty,Value=true};
            Selected.Setters.Add(new Setter(Control.BackgroundProperty,new DynamicResourceExtension(VsBrushes.HighlightKey)));
            Selected.Setters.Add(new Setter(Control.ForegroundProperty,new DynamicResourceExtension(VsBrushes.HighlightTextKey)));
            Style.Triggers.Add(Selected);
            var Inactive=new MultiTrigger();
            Inactive.Conditions.Add(new Condition(ListBoxItem.IsSelectedProperty,true));
            Inactive.Conditions.Add(new Condition(System.Windows.Controls.Primitives.Selector.IsSelectionActiveProperty,false));
            Inactive.Setters.Add(new Setter(Control.ForegroundProperty,new DynamicResourceExtension(VsBrushes.WindowTextKey)));
            Inactive.Setters.Add(new Setter(Control.BackgroundProperty,new DynamicResourceExtension(VsBrushes.WindowKey)));
            Style.Triggers.Add(Inactive);Element.Resources[Type]=Style;
        }
    }
}
