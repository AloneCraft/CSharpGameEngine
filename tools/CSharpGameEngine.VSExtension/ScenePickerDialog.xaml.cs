using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using Microsoft.VisualStudio.PlatformUI;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Modal picker shown by <see cref="OpenSceneDesignerCommand"/> when the target <c>.cs</c> file
/// declares more than one <c>IScene</c> class (ADR-0040 Phase 9) - e.g.
/// <c>PrimitiveShowcaseScene.cs</c>'s two classes. Uses Visual Studio's
/// <see cref="DialogWindow.ShowModal"/> so the IDE owns, centers, and manages the modal state.
/// </summary>
public partial class ScenePickerDialog:DialogWindow {
    public ScenePickerDialog(IReadOnlyList<string>ClassNames){
        InitializeComponent();
        ClassList.ItemsSource=ClassNames;
        ClassList.SelectedIndex=0;
    }

    /// <summary>Index into the list passed to the constructor, or null if the dialog was cancelled.</summary>
    public int?SelectedIndex { get;private set;}

    private void OkButton_Click(object Sender,RoutedEventArgs E)=>Accept();

    private void ClassList_MouseDoubleClick(object Sender,MouseButtonEventArgs E)=>Accept();

    private void Accept(){
        if(ClassList.SelectedIndex<0){
            return;
        }

        SelectedIndex=ClassList.SelectedIndex;
        DialogResult=true;
    }
}
