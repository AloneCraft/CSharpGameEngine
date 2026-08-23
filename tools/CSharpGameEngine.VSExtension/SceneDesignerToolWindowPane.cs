using System;
using System.Runtime.InteropServices;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.VisualStudio.Shell;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Dockable VS tool window showing the 3D preview of one <c>IScene</c>-implementing class.
/// The Package-owned session provider does not hold designer state; it only lets a pane restored by
/// Visual Studio attach to the one already-created editor session and populate its project ComboBox.
///
/// <para>
/// M0045: このペインは<c>VSStd97CmdID.Undo</c>等の標準コマンドを<see cref="OleMenuCommandService"/>へ
/// 登録しない。M0037でその方式を試したが、ペインの表示・フォーカス状態に関わらずVS全体のUndo管理
/// (ツールバーの履歴ドロップダウン・コードエディタ自身のCtrl+Z)を壊す実機回帰を引き起こした。
/// シーンビューのCtrl+Z/Ctrl+Yは<see cref="SceneHwndHost"/>の<c>GetKeyState</c>ポーリング
/// (<see cref="SceneDesignerToolWindowControl"/>の<c>Host.UndoRequested</c>/<c>RedoRequested</c>)
/// で検知しており、標準コマンドテーブルには一切触れない。
/// </para>
/// </summary>
[Guid(PaneGuidString)]
public sealed class SceneDesignerToolWindowPane:ToolWindowPane {
    public const string PaneGuidString="6f2c6a0e-6e8a-4a7a-9a3a-3b6f2b8d7c4a";

    private static Func<SceneEditorSessionService?>?SessionProvider;
    private readonly SceneDesignerToolWindowControl Control;

    public SceneDesignerToolWindowPane():base(null){
        this.Caption="シーン デザイナー";
        this.Control=new SceneDesignerToolWindowControl();
        this.Content= this.Control;
        ThreadHelper.ThrowIfNotOnUIThread();
        if(SessionProvider?.Invoke() is { } Session){
            this.Control.SetSession(Session);
        }
    }

    internal static void ConfigureSessionProvider(Func<SceneEditorSessionService?>?Provider)=>SessionProvider=Provider;

    internal void SetTarget(SceneEditorSessionService Session,SceneDocumentSnapshot Snapshot){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.Control.SetTarget(Session,Snapshot);
    }
}
