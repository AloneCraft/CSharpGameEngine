using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Modal picker shown by <see cref="OpenSceneDesignerCommand"/> when the target <c>.cs</c> file
/// declares more than one <c>IScene</c> class (ADR-0040 Phase 9) - e.g.
/// <c>PrimitiveShowcaseScene.cs</c>'s two classes. A plain WPF <see cref="Window"/>, not a VS-specific
/// dialog shell, matching how <c>System.Windows.MessageBox.Show</c> is already used elsewhere in this
/// extension for simple modal prompts.
/// </summary>
public partial class ScenePickerDialog:Window {
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
