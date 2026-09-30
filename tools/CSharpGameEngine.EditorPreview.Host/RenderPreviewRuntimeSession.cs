using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.EditorPreview.Host;

/// <summary>
/// ADR-0070 B1: a preview session that shows the built game's world through the game's own runtime.
/// The game assembly is loaded into a collectible context of its own, so its statics (content root,
/// runtime view) belong to this session and a later build loads fresh. The game's generated
/// <c>GeneratedEditorPreviewBridge</c> instantiates the world and runs its Presentation systems into
/// the Scene view's surface from the designer's camera.
/// </summary>
public sealed class RenderPreviewRuntimeSession:IRenderPreviewSession {
    private const string BridgeTypeName="CSharpGameEngine.Generated.GeneratedEditorPreviewBridge";
    private readonly GameLoadContext Context;
    private readonly MethodInfo RenderMethod;
    private readonly MethodInfo ResizeMethod;
    private readonly MethodInfo CaptureMethod;
    private readonly MethodInfo ShutdownMethod;
    private RenderPreviewCamera Camera;
    private bool Disposed;

    private RenderPreviewRuntimeSession(
        GameLoadContext Context,MethodInfo RenderMethod,MethodInfo ResizeMethod,MethodInfo CaptureMethod,
        MethodInfo ShutdownMethod,RenderPreviewCamera Camera){
        this.Context=Context;
        this.RenderMethod=RenderMethod;
        this.ResizeMethod=ResizeMethod;
        this.CaptureMethod=CaptureMethod;
        this.ShutdownMethod=ShutdownMethod;
        this.Camera=Camera;
    }

    public uint DeviceGeneration=>1u;
    public ulong CompletedFence{get;private set;}
    public string LastFrameHash{get;private set;}=string.Empty;
    public RenderPreviewCameraDiagnostics[]CameraPreviewDiagnostics=>[];

    /// <summary>Opens <paramref name="Request"/>'s runtime view; it must carry one (see <see cref="RenderPreviewHostRequest.RuntimeView"/>).</summary>
    public static RenderPreviewRuntimeSession Open(RenderPreviewHostRequest Request){
        ArgumentNullException.ThrowIfNull(Request);
        string?Error=RenderPreviewHostProtocol.Validate(Request);
        if(Error is not null){ throw new ArgumentException(Error,nameof(Request)); }
        RenderPreviewRuntimeView View=Request.RuntimeView
            ??throw new ArgumentException("The request does not ask for a runtime view.",nameof(Request));
        string AssemblyPath=Path.GetFullPath(View.AssemblyPath);
        string ContentDirectory=Path.GetFullPath(string.IsNullOrWhiteSpace(View.ContentDirectory)
            ?Path.GetDirectoryName(AssemblyPath)!
            :View.ContentDirectory);
        GameLoadContext Context=new(Path.GetDirectoryName(AssemblyPath)!);
        try {
            Type Bridge=Context.LoadFromAssemblyPath(AssemblyPath).GetType(BridgeTypeName,throwOnError:false)
                ??throw new InvalidOperationException("The game build has no Scene Designer runtime view; build it with this engine version.");
            MethodInfo Find(string Name)=>Bridge.GetMethod(Name,BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)
                ??throw new InvalidOperationException($"The game build's runtime view has no {Name}; build it with this engine version.");
            MethodInfo Initialize=Find("InitializeRuntimeView");
            RenderPreviewRuntimeSession Session=new(
                Context,Find("RenderRuntimeView"),Find("ResizeRuntimeView"),Find("CaptureRuntimeView"),Find("ShutdownRuntimeView"),
                Request.Scene!.Camera!);
            // A false result has already released the game's host, so only the context remains to unload.
            if(Invoke(Initialize,[View.SceneName,ContentDirectory,(nint)Request.SurfaceHandle,Request.Width,Request.Height,View.UseWarp]) is not true){
                throw new InvalidOperationException($"The game build does not contain the scene '{View.SceneName}'; build the project to show it.");
            }
            return Session;
        } catch {
            Context.Unload();
            throw;
        }
    }

    public void ReplaceScene(RenderPreviewScene Scene){
        // The runtime view shows the build's world; a new structure appears after the next build.
    }

    public void UpdateFrame(RenderPreviewFrame Frame){
        ArgumentNullException.ThrowIfNull(Frame);
        this.Camera=Frame.Camera??throw new ArgumentException("A frame requires a camera.",nameof(Frame));
        this.LastFrameHash=string.Empty;
    }

    public void Resize(int Width,int Height){
        if(Width==0&&Height==0){ return; }
        Invoke(this.ResizeMethod,[Width,Height]);
        this.LastFrameHash=string.Empty;
    }

    public void Render(bool CaptureForVerification){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        RenderPreviewCamera Camera=this.Camera;
        if(Invoke(this.RenderMethod,[
            Camera.Position[0],Camera.Position[1],Camera.Position[2],
            Camera.Rotation[0],Camera.Rotation[1],Camera.Rotation[2],Camera.Rotation[3],
            Camera.FovYRadians,Camera.NearZ,Camera.FarZ]) is not true){
            throw new InvalidOperationException("The game's runtime view stopped.");
        }
        this.CompletedFence++;
        if(CaptureForVerification){
            object Frame=Invoke(this.CaptureMethod,[])
                ??throw new InvalidOperationException("The game's runtime view has no level to capture.");
            byte[]Pixels=(byte[])Frame.GetType().GetProperty("Pixels")!.GetValue(Frame)!;
            this.LastFrameHash=Convert.ToHexString(SHA256.HashData(Pixels));
        }
    }

    public RenderPreviewDebugSnapshot CaptureDebugSnapshot()=>
        throw new InvalidOperationException("The runtime view does not capture debug snapshots yet.");

    public void Dispose(){
        if(this.Disposed){ return; }
        this.Disposed=true;
        try { Invoke(this.ShutdownMethod,[]); }
        finally { this.Context.Unload(); }
    }

    /// <summary>Unwraps the game code's own exception so the designer sees its message.</summary>
    private static object?Invoke(MethodInfo Method,object?[]Arguments){
        try {
            return Method.Invoke(null,Arguments);
        } catch(TargetInvocationException Error)when(Error.InnerException is not null){
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(Error.InnerException).Throw();
            throw;
        }
    }

    /// <summary>Resolves the game's dependencies from its own directory; the framework comes from the default context.</summary>
    private sealed class GameLoadContext(string Directory):AssemblyLoadContext("CSharpGameEngine.RuntimeView",isCollectible:true) {
        protected override Assembly?Load(AssemblyName Name){
            string Candidate=Path.Combine(Directory,Name.Name+".dll");
            return File.Exists(Candidate)?this.LoadFromAssemblyPath(Candidate):null;
        }
        protected override nint LoadUnmanagedDll(string UnmanagedDllName){
            string Candidate=Path.Combine(Directory,UnmanagedDllName.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)?UnmanagedDllName:UnmanagedDllName+".dll");
            return File.Exists(Candidate)?this.LoadUnmanagedDllFromPath(Candidate):0;
        }
    }
}
