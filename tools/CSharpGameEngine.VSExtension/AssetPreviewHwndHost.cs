using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Isolated Render Server preview for the Solution Explorer asset editor. WPF owns one stable
/// container HWND while a host crash replaces the DXGI child surface before starting a new host
/// generation and replaying the same source-derived scene.
/// </summary>
internal sealed class AssetPreviewHwndHost(string ModelPath) : HwndHost {
    private string?RepoRoot;
    private string ModelPath = ModelPath;
    private RenderSurfaceHostWindow?SurfaceHost;
    private RenderPreviewHostClient?PreviewClient;
    private EventHandler?RenderingHandler;
    private Action<int,int>?ResizeHandler;
    private bool HostCrashRecoveryUsed;
    private AuthoringOperationSnapshot?CurrentOperation;

    internal event Action<AuthoringOperationSnapshot>?OperationChanged;

    private RenderSurfaceWindow?Surface=>this.SurfaceHost?.Surface;

    protected override HandleRef BuildWindowCore(HandleRef HwndParent){
        this.SurfaceHost=new RenderSurfaceHostWindow(HwndParent.Handle,800,600);
        this.LoadIntoSurface(IsRecovery:false);
        return new HandleRef(this, this.SurfaceHost.Handle);
    }

    protected override void DestroyWindowCore(HandleRef Hwnd){
        this.TearDownRenderer();
        this.SurfaceHost?.Dispose();
        this.SurfaceHost=null;
    }

    /// <summary>Reloads the preview with a different file - used if VS reuses this pane's doc data for a renamed/reloaded document.</summary>
    public void SetModelPath(string ModelPath){
        this.ModelPath=ModelPath;
        if(this.Surface is not null){
            this.TearDownRenderer();
            this.LoadIntoSurface(IsRecovery:false);
        }
    }

    private void LoadIntoSurface(bool IsRecovery){
        RenderSurfaceWindow?Surface=this.Surface;
        if(Surface is null){
            return;
        }
        if(!IsRecovery){
            this.HostCrashRecoveryUsed=false;
        }

        Guid RequestId=Guid.NewGuid();
        this.PublishOperation(AuthoringOperationSnapshot.Start(
            RequestId,AuthoringHostOperation.Import,"Import",
            VsixText.Get("AssetPreview_Loading")));

        try {
            this.RepoRoot??=RepoLocator.ResolveRepoRoot();
            if(this.RepoRoot is null){
                throw new InvalidOperationException(VsixText.Format("Common_RepoRootNotFound",RepoLocator.RepoMarkerFileName));
            }

            var Mesh=MeshLoader.Load(this.ModelPath, this.RepoRoot);
            this.PublishOperation(this.CurrentOperation!.ApplyProgress(
                new AuthoringHostProgress {
                    RequestId=RequestId,Sequence=1,Fraction=0.6f,Phase="Preview",
                },VsixText.Get("AssetPreview_PreparingPreview")));
            string HostPath=RenderPreviewSceneBuilder.ResolveHostPath(this.RepoRoot);
            string ShaderArtifactPath=RenderPreviewSceneBuilder.ResolveShaderArtifactPath(this.RepoRoot);
            int Width=this.SurfaceHost?.Width??800;
            int Height=this.SurfaceHost?.Height??600;
            this.PreviewClient=RenderPreviewHostClient.Start(HostPath);
            this.PreviewClient.Open(
                Surface.Handle.ToInt64(),Width,Height,ShaderArtifactPath,
                RenderPreviewSceneBuilder.FromMesh(Mesh,Width,Height));
            this.PublishOperation(this.CurrentOperation!.Complete(new AuthoringHostResult {
                RequestId=RequestId,State=AuthoringHostTerminalState.Succeeded,ExitCode=0,
                Output=VsixText.Get("AssetPreview_Ready"),
                Progress=new AuthoringHostProgress {
                    RequestId=RequestId,Sequence=2,Fraction=1f,Phase="Completed",
                },
            }));

            this.ResizeHandler=(W,H)=>this.PreviewClient?.Resize(
                W==0&&H==0?0:Math.Max(1,W),
                W==0&&H==0?0:Math.Max(1,H));
            Surface.Resized+=this.ResizeHandler;

            this.RenderingHandler=(_,_)=>{
                try { this.PreviewClient?.Render(captureForVerification:false); }
                catch(RenderPreviewHostException Ex){
                    this.HandleHostFailure(Ex);
                }
            };
            CompositionTarget.Rendering+= this.RenderingHandler;
        } catch(Exception Ex){
            Microsoft.VisualStudio.Shell.ActivityLog.LogError(nameof(AssetPreviewHwndHost),Ex.ToString());
            string Message=VsixText.Format("AssetPreview_LoadFailed",Ex.Message);
            AuthoringOperationSnapshot Operation=this.CurrentOperation
                ??AuthoringOperationSnapshot.Start(
                    RequestId,AuthoringHostOperation.Import,"Import",Message);
            this.PublishOperation(Ex is AuthoringHostOperationException HostFailure
                ?Operation.Fail(Message,HostFailure.Result.Diagnostics)
                :Operation.Fail("ASSET_IMPORT_FAILED",Message));
            System.Windows.MessageBox.Show(
                Message,
                VsixText.Get("AssetPreview_Title"),
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    private void HandleHostFailure(RenderPreviewHostException Error){
        Microsoft.VisualStudio.Shell.ActivityLog.LogError(
            nameof(AssetPreviewHwndHost),Error.ToString());
        this.TearDownRenderer();
        if(Error.RequiresFreshSurfaceGeneration
            &&!this.HostCrashRecoveryUsed
            &&this.SurfaceHost is { }Host){
            this.HostCrashRecoveryUsed=true;
            try {
                _=Host.RecreateSurface(InitiallyVisible:true);
                this.LoadIntoSurface(IsRecovery:true);
                return;
            } catch(Exception RecoveryError){
                Microsoft.VisualStudio.Shell.ActivityLog.LogError(
                    nameof(AssetPreviewHwndHost),RecoveryError.ToString());
            }
        }
        if(this.CurrentOperation is { }Operation){
            this.PublishOperation(Operation.Fail(
                "ASSET_PREVIEW_HOST_FAILED",Error.Message));
        }
        this.Surface?.SetVisible(false);
    }

    private void PublishOperation(AuthoringOperationSnapshot Operation){
        this.CurrentOperation=Operation;
        this.OperationChanged?.Invoke(Operation);
    }

    private void TearDownRenderer(){
        if(this.RenderingHandler is not null){
            CompositionTarget.Rendering-= this.RenderingHandler;
            this.RenderingHandler=null;
        }

        if(this.Surface is not null&&this.ResizeHandler is not null){
            this.Surface.Resized-=this.ResizeHandler;
            this.ResizeHandler=null;
        }
        this.PreviewClient?.Dispose();
        this.PreviewClient=null;
    }
}
