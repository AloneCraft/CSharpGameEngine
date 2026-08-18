using System;
using System.Linq;
using CSharpGameEngine.SceneAnalysis;
using EnvDTE;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// M0050 Phase 4: Scene Designerが実際にPinしている(開いている)Sceneの解析診断
/// (SCENE1xx/SCENE2xx)をVisual Studioのエラー一覧ウィンドウへ反映する。
///
/// <para>
/// 対象を「Pin中のScene」だけに絞るのは、Compilationの通常のライブ診断(C#エディタの赤線)とは
/// 別経路であり、ソリューション内の全<c>*.scene.json</c>を常時解析してエラー一覧を維持する
/// コストが見合わないため。実機不具合(2026-08、`ActivityLog.xml`が`/Log`起動でないと書かれず
/// 原因調査ができなかった)を受け、Scene解析の失敗を`ActivityLog.xml`に頼らずVisual Studio上で
/// 直接確認できるようにする目的も兼ねる。
/// </para>
/// </summary>
internal sealed class SceneDiagnosticsErrorListSync:IDisposable {
    private readonly ErrorListProvider Provider;
    private readonly SceneEditorSessionService Session;
    private bool Disposed;

    private SceneDiagnosticsErrorListSync(IServiceProvider ServiceProvider,SceneEditorSessionService Session){
        this.Provider=new ErrorListProvider(ServiceProvider);
        this.Session=Session;
    }

    internal static SceneDiagnosticsErrorListSync Start(IServiceProvider ServiceProvider,SceneEditorSessionService Session){
        ThreadHelper.ThrowIfNotOnUIThread();
        SceneDiagnosticsErrorListSync Sync=new(ServiceProvider,Session);
        Session.PinnedSnapshotChanged+=Sync.OnPinnedSnapshotChanged;
        return Sync;
    }

    private void OnPinnedSnapshotChanged(SceneDocumentSnapshot?Snapshot){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(this.Disposed){
            return;
        }

        this.Provider.Tasks.Clear();

        if(Snapshot?.CurrentModel is not { }Model||Model.Diagnostics.IsEmpty){
            return;
        }

        foreach(Diagnostic Diagnostic in Model.Diagnostics){
            if(Diagnostic.Severity!=DiagnosticSeverity.Error){
                continue;
            }

            FileLinePositionSpan Span=Diagnostic.Location.GetLineSpan();
            ErrorTask Task=new(){
                Document=Model.FilePath,
                ErrorCategory=TaskErrorCategory.Error,
                Category=TaskCategory.BuildCompile,
                Text=$"{Diagnostic.Id}: {Diagnostic.GetMessage()}",
                Line=Span.StartLinePosition.Line,
                Column=Span.StartLinePosition.Character,
            };
            Task.Navigate+=this.OnTaskNavigate;
            this.Provider.Tasks.Add(Task);
        }

        if(this.Provider.Tasks.Count>0){
            this.Provider.Show();
        }
    }

    private void OnTaskNavigate(object Sender,EventArgs E){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(Sender is not ErrorTask{ Document:{ Length:>0 }FilePath}
            ||Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(SDTE))is not DTE Dte){
            return;
        }

        Dte.ItemOperations.OpenFile(FilePath);
    }

    public void Dispose(){
        if(this.Disposed){
            return;
        }

        this.Disposed=true;
        this.Session.PinnedSnapshotChanged-=this.OnPinnedSnapshotChanged;
        foreach(ErrorTask Task in this.Provider.Tasks.OfType<ErrorTask>()){
            Task.Navigate-=this.OnTaskNavigate;
        }

        this.Provider.Tasks.Clear();
        this.Provider.Dispose();
    }
}
