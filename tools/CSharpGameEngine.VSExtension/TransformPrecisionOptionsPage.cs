using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Tools&gt;Options &gt; C# Game Engine &gt; Transform Precision.
/// Unlike normal Options pages this page has no user-scoped settings: it resolves the currently
/// opened Scene's project and modifies its checked-in <c>GameEnginePrecision.cs</c> through Roslyn.
/// </summary>
[Guid("d03f7dfb-04a3-42b3-8e25-6e74e5e91e54")]
public sealed class TransformPrecisionOptionsPage:UIElementDialogPage {
    private TransformPrecisionOptionsControl?Control;
    private TransformPrecisionOptionsController?Controller;
    private JoinableTask?RunningOperation;
    private TransformPrecisionOptionsSnapshot?CurrentSnapshot;

    protected override UIElement Child=>this.EnsureControl();

    protected override void OnActivate(CancelEventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread();
        base.OnActivate(Args);
        this.QueueRefresh();
    }

    /// <summary>
    /// Visual Studio's standard Apply button is wired to the same source edit as the page-local
    /// button.  When no Scene-owned project is resolved, the control's button is disabled and this
    /// method intentionally does nothing; no value is ever copied to the VS profile.
    /// </summary>
    protected override void OnApply(PageApplyEventArgs Args){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(Args.ApplyBehavior==ApplyKind.Apply){
            ThreadHelper.JoinableTaskFactory.Run(()=>this.ApplyCoreAsync(CancellationToken.None));
        }

        base.OnApply(Args);
    }

    private TransformPrecisionOptionsControl EnsureControl(){
        if(this.Control is not { } Existing){
            Existing=new TransformPrecisionOptionsControl();
            Existing.RefreshRequested+=this.QueueRefresh;
            Existing.ApplyRequested+=this.QueueApply;
            this.Control=Existing;
        }

        return Existing;
    }

    private void QueueRefresh(){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.RunningOperation=ThreadHelper.JoinableTaskFactory.RunAsync(async ()=>{
            await this.RefreshCoreAsync(CancellationToken.None).ConfigureAwait(false);
        });
    }

    private void QueueApply(){
        ThreadHelper.ThrowIfNotOnUIThread();
        this.RunningOperation=ThreadHelper.JoinableTaskFactory.RunAsync(async ()=>{
            await this.ApplyCoreAsync(CancellationToken.None).ConfigureAwait(false);
        });
    }

    private async Task RefreshCoreAsync(CancellationToken CancellationToken){
        this.Controller??=TransformPrecisionOptionsController.TryCreate();
        TransformPrecisionOptionsSnapshot Snapshot=this.Controller is null
            ?TransformPrecisionOptionsSnapshot.Unavailable(
                "Visual StudioのRoslyn Workspaceを取得できないため、Transform Precisionは変更できません。")
            :await this.Controller.RefreshAsync(CancellationToken).ConfigureAwait(false);
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(CancellationToken);
        this.CurrentSnapshot=Snapshot;
        this.EnsureControl().Load(Snapshot);
    }

    private async Task ApplyCoreAsync(CancellationToken CancellationToken){
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(CancellationToken);
        TransformPrecisionOptionsControl View=this.EnsureControl();
        if(this.Controller is null){
            View.ShowApplyResult(TransformPrecisionApplyResult.Failure(
                "Transform Precisionの対象Sceneプロジェクトを解決できません。"));
            return;
        }

        TransformPrecisionOptionsSnapshot? Snapshot=this.CurrentSnapshot;
        if(Snapshot is null||!Snapshot.CanApply||Snapshot.Target is null){
            View.ShowApplyResult(TransformPrecisionApplyResult.Failure(
                "Transform Precisionの対象Sceneプロジェクトを解決できません。"));
            return;
        }

        if(!View.TryGetRequestedConfiguration(out TransformPrecisionConfiguration Configuration,out string?ErrorMessage)){
            View.ShowApplyResult(TransformPrecisionApplyResult.Failure(
                ErrorMessage??"Transform Precisionの入力が無効です。"));
            return;
        }

        TransformPrecisionApplyResult Result=await this.Controller.ApplyAsync(
            Snapshot.Target,Configuration.TotalBits,Configuration.FractionalBits,CancellationToken).ConfigureAwait(false);
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(CancellationToken);
        View.ShowApplyResult(Result);
    }
}
