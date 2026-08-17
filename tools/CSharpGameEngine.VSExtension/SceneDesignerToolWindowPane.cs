using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Dockable VS tool window showing the 3D preview of one <c>IScene</c>-implementing class.
/// The Package-owned session provider does not hold designer state; it only lets a pane restored by
/// Visual Studio attach to the one already-created editor session and populate its project ComboBox.
/// </summary>
[Guid(PaneGuidString)]
public sealed class SceneDesignerToolWindowPane:ToolWindowPane {
    public const string PaneGuidString="6f2c6a0e-6e8a-4a7a-9a3a-3b6f2b8d7c4a";

    private static Func<SceneEditorSessionService?>?SessionProvider;
    private readonly SceneDesignerToolWindowControl Control;

    public SceneDesignerToolWindowPane():base(null){
        Caption="シーン デザイナー";
        Control=new SceneDesignerToolWindowControl();
        Content=Control;
        if(SessionProvider?.Invoke() is { } Session){
            Control.SetSession(Session);
        }
    }

    internal static void ConfigureSessionProvider(Func<SceneEditorSessionService?>?Provider)=>SessionProvider=Provider;

    internal void SetTarget(SceneEditorSessionService Session,SceneDocumentSnapshot Snapshot)=>Control.SetTarget(Session,Snapshot);

    /// <summary>
    /// M0037: Edit.Undo/Redoをここで<see cref="OleMenuCommandService"/>へ登録する。
    /// <see cref="WindowPane"/>(<see cref="ToolWindowPane"/>の基底)は<c>IOleCommandTarget</c>を
    /// explicit/sealedで実装しており派生クラスからoverrideできないが、その実装はペイン自身の
    /// <see cref="IMenuCommandService"/>へ委譲するため、コマンドを登録するだけでシェルのコマンド
    /// ルーティングに参加できる。コンストラクタ時点ではペインのサービスサイトがまだ設定されて
    /// いないため、ここ(<see cref="OnToolWindowCreated"/>)で行う。
    ///
    /// <para>
    /// 背景: M0036はCtrl+ZをWPFの<c>PreviewKeyDown</c>で横取りしようとしたが、Ctrl+ZはGlobal
    /// スコープでEdit.Undoに割り当て済みのためVSシェルがコマンドとして先に消費してしまい、WPFの
    /// 入力系には一切届かず実機で無反応だった。正しい経路はペイン自身がシェルコマンドとして
    /// Undo/Redoを引き受けることであり、`BeforeQueryStatus`でビューポートのWPFフォーカスが無い
    /// 場合は<c>Supported=false</c>を返してシェルの通常ルーティング(コードエディタ等)へ戻すため、
    /// 他の場所のCtrl+Zには影響しない。
    /// </para>
    /// </summary>
    public override void OnToolWindowCreated(){
        base.OnToolWindowCreated();
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.GetService(typeof(IMenuCommandService))is not OleMenuCommandService Commands){
            return;
        }

        RegisterUndoRedoCommand(Commands,VSConstants.VSStd97CmdID.Undo,IsRedo:false);
        RegisterUndoRedoCommand(Commands,VSConstants.VSStd97CmdID.MultiLevelUndo,IsRedo:false);
        RegisterUndoRedoCommand(Commands,VSConstants.VSStd97CmdID.Redo,IsRedo:true);
        RegisterUndoRedoCommand(Commands,VSConstants.VSStd97CmdID.MultiLevelRedo,IsRedo:true);
    }

    private void RegisterUndoRedoCommand(OleMenuCommandService Commands,VSConstants.VSStd97CmdID CmdId,bool IsRedo){
        CommandID Id=new(VSConstants.GUID_VSStandardCommandSet97,(int)CmdId);
        OleMenuCommand Command=new(
            (Sender,Args)=>{
                ThreadHelper.ThrowIfNotOnUIThread();
                if(IsRedo){ Control.TryRedoFromCommand(); } else { Control.TryUndoFromCommand(); }
            },
            Id);
        Command.BeforeQueryStatus+=(Sender,Args)=>{
            bool Handle=SceneDesignerUndoCommandPolicy.ShouldHandle(Control.IsViewportFocused,Control.HasPinnedSnapshot);
            OleMenuCommand Self=(OleMenuCommand)Sender!;
            Self.Supported=Handle;
            Self.Enabled=Handle;
        };
        Commands.AddCommand(Command);
    }
}