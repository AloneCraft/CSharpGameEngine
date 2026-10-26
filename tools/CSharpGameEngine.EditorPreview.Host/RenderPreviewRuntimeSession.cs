using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.SceneAnalysis.Json;

namespace CSharpGameEngine.EditorPreview.Host;

/// <summary>
/// ADR-0070 B1：プレビューセッションは、ゲーム自身のランタイムを通してビルド済みゲームのワールドを表示する。ゲームアセンブリは独自のアンロード可能コンテキストに読み込むため、その静的データ（コンテンツルート、ランタイムビュー）はこのセッションに属し、次回のビルド時には新しく読み込まれる。ゲームが生成した <c>GeneratedEditorPreviewBridge</c> がワールドをインスタンス化し、Designer のカメラから Scene ビューのサーフェスへ Presentation システムを実行する。
/// <para>
/// M0096 (ADR-0047)：同じブリッジは、編集済みシーンの第 2 のワールドであるエディターワールドも実行する。<c>[ExecuteInEditor(Realtime=true)]</c> システムは Spatial と Physics のデバッグオーバーレイ用に 15 Hz で tick する。tick と各要求は 1 つのロック内で実行されるため、ブリッジで 2 つの呼び出しが同時に実行されることはない。
/// </para>
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
    /// <summary>最後に適用したドキュメントとそのノードの先行順リスト。ドラッグ時の Transform 上書きの基準となる。</summary>
    private string DocumentText=string.Empty;
    private SceneJsonValue[]DocumentNodes=[];
    /// <summary>ドキュメントの角度が回転数であるかどうか（スキーマ バージョン 2、ADR-0074）。上書き後も他メンバーのテキストを維持する。</summary>
    private bool DocumentTurns;
    private float[]OverlayTriangles=[];
    private float[]OverlayLines=[];
    private int[]Selection=[];
    private RenderPreviewCamera Camera;
    private bool Disposed;
    private readonly object Gate=new();
    private MethodInfo?EditorShutdownMethod;
    private MethodInfo?EditorTickMethod;
    private MethodInfo?EditorSpatialWorldIdMethod;
    private Timer?EditorTimer;
    private long EditorTicks;
    /// <summary>ADR-0047：エディターワールドのリアルタイムシステムは 15 Hz で tick する。</summary>
    private static readonly TimeSpan EditorTickPeriod=TimeSpan.FromTicks(TimeSpan.TicksPerSecond/15);

    /// <summary>ビューとともにエディターワールドを実行するか（ビルドにシーンまたはそのブリッジがない場合を除き実行する）。</summary>
    public bool EditorWorldRunning=>this.EditorTimer is not null;

    /// <summary>エディターワールドが実行した 15 Hz の tick 数。</summary>
    public long EditorWorldTicks=>Interlocked.Read(ref this.EditorTicks);

    public Guid EditorSpatialWorldId{
        get {
            lock(this.Gate){
                return !this.Disposed&&this.EditorSpatialWorldIdMethod is { }Method&&Invoke(Method,[]) is Guid Id?Id:Guid.Empty;
            }
        }
    }

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

    /// <summary><paramref name="Request"/> のランタイムビューを開く。要求にはそのビューを含める必要がある（<see cref="RenderPreviewHostRequest.RuntimeView"/> を参照）。</summary>
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
            // false の結果はゲームのホストをすでに解放済みであり、アンロードする対象はコンテキストだけである。
            if(Invoke(Initialize,[View.SceneName,ContentDirectory,(nint)Request.SurfaceHandle,Request.Width,Request.Height,View.UseWarp]) is not true){
                throw new InvalidOperationException($"The game build does not contain the scene '{View.SceneName}'; build the project to show it.");
            }
            if(Request.SceneJson.Length>0){ Session.ApplySceneDocument(Request.SceneJson); }
            Session.StartEditorWorld(Bridge,View.SceneName);
            return Session;
        } catch {
            Context.Unload();
            throw;
        }
    }

    /// <summary>
    /// <paramref name="SceneName"/> のエディターワールドを開始する。失敗した場合はワールドなしでビューを動かし続ける。オーバーレイはデータ元を失うが、画像表示には影響しない。
    /// </summary>
    private void StartEditorWorld(Type Bridge,string?SceneName){
        const BindingFlags Static=BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public;
        if(Bridge.GetMethod("Initialize",Static) is not { }Initialize
            ||Bridge.GetMethod("TickRealtime",Static) is not { }Tick
            ||Bridge.GetMethod("Shutdown",Static) is not { }Shutdown){
            return;
        }
        try {
            if(Invoke(Initialize,[SceneName]) is not true){ return; }
        } catch(Exception Error){
            Console.Error.WriteLine($"The editor world did not start: {Error.Message}");
            return;
        }
        this.EditorTickMethod=Tick;
        this.EditorShutdownMethod=Shutdown;
        this.EditorSpatialWorldIdMethod=Bridge.GetMethod("EditorSpatialWorldId",Static);
        this.EditorTimer=new Timer(this.TickEditorWorld,null,EditorTickPeriod,EditorTickPeriod);
    }

    private void TickEditorWorld(object?_){
        lock(this.Gate){
            if(this.Disposed||this.EditorTickMethod is not { }Tick){ return; }
            try {
                Invoke(Tick,[]);
                Interlocked.Increment(ref this.EditorTicks);
            } catch(Exception Error){
                // 障害状態になったエディターワールドは tick を停止するが、ビューは動作を続ける。
                Console.Error.WriteLine($"The editor world stopped: {Error.Message}");
                this.EditorTickMethod=null;
            }
        }
    }

    /// <summary>
    /// 実行中のワールドへ編集済みドキュメントの値を適用する。ワールドの構造はビルド時のまま維持する。ビルドに存在しないノードやコンポーネントがあれば <see cref="BuildRequired"/> を設定し、その値の適用は次回のビルドまで待つ。読み取り不能なドキュメントの場合は何も変更しない。
    /// </summary>
    public void ReplaceScene(RenderPreviewScene Scene,string SceneJson){
        lock(this.Gate){
            if(SceneJson.Length>0){ this.ApplySceneDocument(SceneJson); }
        }
    }

    private void ApplySceneDocument(string SceneJson){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        SceneJsonValue[]Nodes;
        bool Turns;
        try {
            var Document=SceneJsonReader.Parse(SceneJson);
            Turns=SceneJsonRuntimeValues.UsesTurns(Document);
            Nodes=[.. PreOrder(Document.Get("root")
                ??throw new SceneJsonException("The scene document has no root.",0))];
        } catch(SceneJsonException){
            return;
        }
        this.NodeIndexByPath=NodePaths(Nodes.Length>0?Nodes[0]:null);
        this.DocumentText=SceneJson;
        this.DocumentTurns=Turns;
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
                    Value=SceneJsonRuntimeValues.Component(Type,Component.Get("members")??SceneJsonReader.Parse("{}"),Type.Name,Turns);
                } catch(InvalidOperationException){
                    // ジェネレーターはドキュメントのエラーを報告する。ビューは最後に正常だった値を維持する。
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

    /// <summary>ゲームビルドから見たコンポーネント型。ゲーム独自のコンテキストに読み込まれたアセンブリから取得する。</summary>
    private Type?FindType(string FullName)=>SceneJsonRuntimeValues.FindType(this.Context.Assemblies,FullName);

    /// <summary>デザイナーのノードパス（"(root)"、"0"、"0/1"）を先行順インデックスへ対応付けたもの。</summary>
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
    /// Designer のカメラ、ギズモ、選択状態。ランタイムはカメラ基準でレンダリングするため、ワールド空間のギズモはカメラ位置分だけずれる。選択状態はノードパスをドキュメントのノード順へ対応付ける。
    /// </summary>
    public void UpdateFrame(RenderPreviewFrame Frame){
        ArgumentNullException.ThrowIfNull(Frame);
        lock(this.Gate){ this.UpdateFrameLocked(Frame); }
    }

    private void UpdateFrameLocked(RenderPreviewFrame Frame){
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
    /// M0096：ギズモのドラッグで得た Transform を、実行中ワールドのドキュメント上の Transform に適用する。Position、Rotation、Scale だけを置き換え、座標系やその他のメンバーは維持する。ゲームの Presentation システムが、他の値の編集と同様にレンダラーへ伝える。
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
                Value=SceneJsonRuntimeValues.Component(Type,this.OverriddenMembers(Component.Get("members"),Override),Type.Name,this.DocumentTurns);
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
        lock(this.Gate){
            Invoke(this.ResizeMethod,[Width,Height]);
            this.LastFrameHash=string.Empty;
        }
    }

    public void Render(bool CaptureForVerification){
        lock(this.Gate){ this.RenderLocked(CaptureForVerification); }
    }

    private void RenderLocked(bool CaptureForVerification){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        RenderPreviewCamera Camera=this.Camera;
        // レベルの読み込み前（最初のフレーム）にはレンダラーがまだない。次のフレームで表示される。
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

    public void Dispose(){
        lock(this.Gate){
            if(this.Disposed){ return; }
            this.Disposed=true;
        }
        // 実行中の tick が終わるのを待つ。Disposed フラグが次の tick を停止する。
        using(var Stopped=new ManualResetEvent(false)){
            if(this.EditorTimer?.Dispose(Stopped)==true){ Stopped.WaitOne(TimeSpan.FromSeconds(5)); }
        }
        lock(this.Gate){
            try {
                if(this.EditorShutdownMethod is { }EditorShutdown){
                    try { Invoke(EditorShutdown,[]); } catch(Exception Error){ Console.Error.WriteLine($"The editor world did not shut down: {Error.Message}"); }
                }
                Invoke(this.ShutdownMethod,[]);
            }
            finally { this.Context.Unload(); }
        }
    }

    /// <summary>Designer にメッセージを表示できるよう、ゲームコード自身の例外をラップ解除する。</summary>
    private static object?Invoke(MethodInfo Method,object?[]Arguments){
        try {
            return Method.Invoke(null,Arguments);
        } catch(TargetInvocationException Error)when(Error.InnerException is not null){
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(Error.InnerException).Throw();
            throw;
        }
    }

    /// <summary>ゲームの依存関係はゲーム自身のディレクトリから解決し、フレームワークは既定のコンテキストから取得する。</summary>
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
