using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
namespace CSharpGameEngine.VSExtension;
/// <summary>
/// Data Asset Editor（.cgeasset）。型に従った値の編集は Details（Inspector と同じ経路）で行い、値は共有 JSON テキストバッファーに直接書く。
/// 保存・dirty・Undo は VS のテキストバッファーが持つので、このエディターは独自の履歴を持たない。
/// 「JSON で開く」は同じバッファーを VS 標準のテキストエディターで開く。型の解決に失敗する場合などは Details が理由を表示し、JSON 編集へ案内する。
/// </summary>
internal sealed class DataAssetEditorContent:AssetEditorContent {
    private readonly AssetDetailsView DetailsView;
    private readonly UIElement[]Toolbar;
    internal DataAssetEditorContent(string FilePath):base(FilePath){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.DetailsView=new AssetDetailsView(FilePath);
        this.Toolbar=[AssetEditorShell.CreateButton(VsixText.Get("AssetEditor_OpenAsJson"),this.OpenAsJson),AssetEditorShell.CreateButton(VsixText.Get("AssetEditor_Save"),this.Save)];
    }
    internal override UIElement Center=>this.DetailsView;
    internal override IReadOnlyList<UIElement>ToolbarItems=>this.Toolbar;
    private void OpenAsJson(){
        ThreadHelper.ThrowIfNotOnUIThread();
        try {
            VsShellUtilities.OpenDocumentWithSpecificEditor(ServiceProvider.GlobalProvider,this.FilePath,VSConstants.VsEditorFactoryGuid.TextEditor_guid,VSConstants.LOGVIEWID.TextView_guid);
        } catch(Exception Error) when(Error is System.Runtime.InteropServices.COMException or InvalidOperationException){
            this.SetStatus(Error.Message);
        }
    }
    private void Save(){
        ThreadHelper.ThrowIfNotOnUIThread();
        try {
            AssetDocumentWorkspace.Save(this.FilePath);
            this.SetStatus(VsixText.Get("AssetEditor_Saved"));
        } catch(Exception Error) when(Error is OperationCanceledException or System.Runtime.InteropServices.COMException or InvalidOperationException){
            this.SetStatus(Error.Message);
        }
    }
    internal override bool HandleNativeShortcut(Key Key){
        ThreadHelper.ThrowIfNotOnUIThread();
        return this.DetailsView.HandleNativeShortcut(Key);
    }
    protected override void OnReload(){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.DetailsView.Retarget(this.FilePath);
    }
    public override void Dispose()=>this.DetailsView.Dispose();
}
