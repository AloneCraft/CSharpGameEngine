using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.SceneAnalysis.Json;

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
    private readonly MethodInfo NodeCountMethod;
    private readonly MethodInfo SetComponentMethod;
    private readonly MethodInfo OverlayMethod;
    private readonly MethodInfo SelectionMethod;
    private const string TransformTypeName="CSharpGameEngine.Entities.Transform";
    private Dictionary<string,int>NodeIndexByPath=new(StringComparer.Ordinal){ ["(root)"]=0 };
    /// <summary>The last applied document and its nodes in pre-order, the base of a drag's transform overrides.</summary>
    private string DocumentText=string.Empty;
    private SceneJsonValue[]DocumentNodes=[];
    private float[]OverlayTriangles=[];
    private float[]OverlayLines=[];
    private int[]Selection=[];
    private RenderPreviewCamera Camera;
    private bool Disposed;

    private RenderPreviewRuntimeSession(
        GameLoadContext Context,MethodInfo RenderMethod,MethodInfo ResizeMethod,MethodInfo CaptureMethod,
        MethodInfo ShutdownMethod,MethodInfo NodeCountMethod,MethodInfo SetComponentMethod,MethodInfo OverlayMethod,
        MethodInfo SelectionMethod,RenderPreviewCamera Camera){
        this.Context=Context;
        this.RenderMethod=RenderMethod;
        this.ResizeMethod=ResizeMethod;
        this.CaptureMethod=CaptureMethod;
        this.ShutdownMethod=ShutdownMethod;
        this.NodeCountMethod=NodeCountMethod;
        this.SetComponentMethod=SetComponentMethod;
        this.OverlayMethod=OverlayMethod;
        this.SelectionMethod=SelectionMethod;
        this.Camera=Camera;
    }

    public uint DeviceGeneration=>1u;
    public ulong CompletedFence{get;private set;}
    public string LastFrameHash{get;private set;}=string.Empty;
    public RenderPreviewCameraDiagnostics[]CameraPreviewDiagnostics=>[];
    public bool BuildRequired{get;private set;}

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
                Find("RuntimeViewNodeCount"),Find("TrySetRuntimeViewComponent"),Find("SetRuntimeViewOverlay"),
                Find("SetRuntimeViewSelection"),Request.Scene!.Camera!);
            // A false result has already released the game's host, so only the context remains to unload.
            if(Invoke(Initialize,[View.SceneName,ContentDirectory,(nint)Request.SurfaceHandle,Request.Width,Request.Height,View.UseWarp]) is not true){
                throw new InvalidOperationException($"The game build does not contain the scene '{View.SceneName}'; build the project to show it.");
            }
            if(Request.SceneJson.Length>0){ Session.ApplySceneDocument(Request.SceneJson); }
            return Session;
        } catch {
            Context.Unload();
            throw;
        }
    }

    /// <summary>
    /// Applies the edited document's values to the running world. The world keeps the build's
    /// structure: a node or component the build does not have sets <see cref="BuildRequired"/>, and
    /// its values wait for the next build. A document that cannot be read changes nothing.
    /// </summary>
    public void ReplaceScene(RenderPreviewScene Scene,string SceneJson){
        if(SceneJson.Length>0){ this.ApplySceneDocument(SceneJson); }
    }

    private void ApplySceneDocument(string SceneJson){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        SceneJsonValue[]Nodes;
        try {
            Nodes=[.. PreOrder(SceneJsonReader.Parse(SceneJson).Get("root")
                ??throw new SceneJsonException("The scene document has no root.",0))];
        } catch(SceneJsonException){
            return;
        }
        this.NodeIndexByPath=NodePaths(Nodes.Length>0?Nodes[0]:null);
        this.DocumentText=SceneJson;
        this.DocumentNodes=Nodes;
        var Stale=Nodes.Length!=(int)Invoke(this.NodeCountMethod,[])!;
        for(var Index=0;Index<Nodes.Length&&!Stale;Index++){
            foreach(var Component in Nodes[Index].Get("components")?.Items??[]){
                var TypeName=Component.Get("type")?.Text??string.Empty;
                if(this.FindType(TypeName) is not { }Type){
                    Stale=true;
                    break;
                }
                object Value;
                try {
                    Value=SceneJsonRuntimeValues.Component(Type,Component.Get("members")??SceneJsonReader.Parse("{}"),Type.Name);
                } catch(InvalidOperationException){
                    // The generator reports the document's errors; the view keeps the last good values.
                    continue;
                }
                if(Invoke(this.SetComponentMethod,[Index,Value]) is not true){
                    Stale=true;
                    break;
                }
            }
        }
        this.BuildRequired=Stale;
    }

    /// <summary>A component type as the game build sees it: from the assemblies of the game's own context.</summary>
    private Type?FindType(string FullName){
        foreach(var Assembly in this.Context.Assemblies){
            if(Assembly.GetType(FullName,throwOnError:false) is { }Type){ return Type; }
        }
        return null;
    }

    /// <summary>The designer's node paths ("(root)", "0", "0/1") mapped to their pre-order index.</summary>
    private static Dictionary<string,int>NodePaths(SceneJsonValue?Root){
        Dictionary<string,int>Paths=new(StringComparer.Ordinal);
        void Visit(SceneJsonValue Node,string Path){
            Paths[Path.Length==0?"(root)":Path]=Paths.Count;
            var Children=Node.Get("children")?.Items??[];
            for(var Index=0;Index<Children.Count;Index++){
                Visit(Children[Index],Path.Length==0?Index.ToString(System.Globalization.CultureInfo.InvariantCulture):Path+"/"+Index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }
        if(Root is not null){ Visit(Root,string.Empty); }
        return Paths;
    }

    private static IEnumerable<SceneJsonValue>PreOrder(SceneJsonValue Node){
        yield return Node;
        foreach(var Child in Node.Get("children")?.Items??[]){
            foreach(var Descendant in PreOrder(Child)){ yield return Descendant; }
        }
    }

    /// <summary>
    /// The designer's camera, gizmos and selection. The runtime renders relative to its camera, so the
    /// gizmos (in world space) are shifted by the camera position; selection maps node paths to the
    /// document's node order.
    /// </summary>
    public void UpdateFrame(RenderPreviewFrame Frame){
        ArgumentNullException.ThrowIfNull(Frame);
        this.Camera=Frame.Camera??throw new ArgumentException("A frame requires a camera.",nameof(Frame));
        this.OverlayTriangles=CameraRelative(Frame.OverlayTriangles,this.Camera.Position);
        this.OverlayLines=CameraRelative(Frame.OverlayLines,this.Camera.Position);
        this.Selection=[.. Frame.Objects
            .Where(static Object=>Object.SelectionWeight>0f)
            .Select(Object=>this.NodeIndexByPath.TryGetValue(Object.ObjectId,out var Index)?Index:-1)
            .Where(static Index=>Index>=0)
            .Distinct()
            .Order()];
        this.ApplyTransformOverrides(Frame.TransformOverrides??[]);
        this.LastFrameHash=string.Empty;
    }

    /// <summary>
    /// M0096: a gizmo drag's transforms, applied to the running world as the document's Transform with
    /// only Position, Rotation and Scale replaced (its frame and other members stay). The game's
    /// Presentation systems carry them to the renderer like any value edit.
    /// </summary>
    private void ApplyTransformOverrides(RenderPreviewTransformOverride[]Overrides){
        if(Overrides.Length==0||this.FindType(TransformTypeName) is not { }Type){ return; }
        foreach(var Override in Overrides){
            if(!this.NodeIndexByPath.TryGetValue(Override.ObjectId,out var Index)||Index>=this.DocumentNodes.Length){ continue; }
            var Component=(this.DocumentNodes[Index].Get("components")?.Items??[])
                .FirstOrDefault(static Item=>string.Equals(Item.Get("type")?.Text,TransformTypeName,StringComparison.Ordinal));
            if(Component is null){ continue; }
            object Value;
            try {
                Value=SceneJsonRuntimeValues.Component(Type,this.OverriddenMembers(Component.Get("members"),Override),Type.Name);
            } catch(Exception Error)when(Error is InvalidOperationException or SceneJsonException){
                continue;
            }
            Invoke(this.SetComponentMethod,[Index,Value]);
        }
    }

    private SceneJsonValue OverriddenMembers(SceneJsonValue?Members,RenderPreviewTransformOverride Override){
        static string Number(float Value)=>Value.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
        var Text=new System.Text.StringBuilder("{");
        foreach(var Member in Members?.Properties??[]){
            if(Member.Name is "Position" or "Rotation" or "Scale"){ continue; }
            Text.Append('"').Append(Member.Name).Append("\":").Append(this.DocumentText,Member.Value.Start,Member.Value.End-Member.Value.Start).Append(',');
        }
        Text.Append("\"Position\":{\"R\":").Append(Number(Override.Position[0])).Append(",\"U\":").Append(Number(Override.Position[1])).Append(",\"F\":").Append(Number(Override.Position[2])).Append("},");
        Text.Append("\"Rotation\":{\"R\":").Append(Number(Override.Rotation[0])).Append(",\"U\":").Append(Number(Override.Rotation[1])).Append(",\"F\":").Append(Number(Override.Rotation[2])).Append(",\"W\":").Append(Number(Override.Rotation[3])).Append("},");
        Text.Append("\"Scale\":{\"R\":").Append(Number(Override.Scale[0])).Append(",\"U\":").Append(Number(Override.Scale[1])).Append(",\"F\":").Append(Number(Override.Scale[2])).Append("}}");
        return SceneJsonReader.Parse(Text.ToString());
    }

    private static float[]CameraRelative(RenderPreviewOverlayVertex[]Vertices,float[]Camera){
        var Values=new float[Vertices.Length*7];
        for(var Index=0;Index<Vertices.Length;Index++){
            var At=Index*7;
            Values[At]=Vertices[Index].Position[0]-Camera[0];
            Values[At+1]=Vertices[Index].Position[1]-Camera[1];
            Values[At+2]=Vertices[Index].Position[2]-Camera[2];
            Vertices[Index].Color.AsSpan(0,4).CopyTo(Values.AsSpan(At+3,4));
        }
        return Values;
    }

    public void Resize(int Width,int Height){
        if(Width==0&&Height==0){ return; }
        Invoke(this.ResizeMethod,[Width,Height]);
        this.LastFrameHash=string.Empty;
    }

    public void Render(bool CaptureForVerification){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        RenderPreviewCamera Camera=this.Camera;
        // Before the level has loaded (the first frame) there is no renderer yet; the next frame shows them.
        Invoke(this.OverlayMethod,[this.OverlayTriangles,this.OverlayLines]);
        Invoke(this.SelectionMethod,[this.Selection]);
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
