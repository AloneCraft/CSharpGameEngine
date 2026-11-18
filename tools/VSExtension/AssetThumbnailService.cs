using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Json;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.Shell;
namespace CSharpGameEngine.VSExtension;
/// <summary>Visible tiles request disposable geometry/base-color or real image previews; source decoders and GPU execution stay in isolated hosts.</summary>
internal static class AssetThumbnailService {
    private const int MaximumPending=32;
    private const int MaximumEntries=128;
    private const int MaximumBytes=8*1024*1024;
    private static readonly object Gate=new();
    // 順番待ちの空き。いっぱいのときは新しい依頼が空きを待つ(見えているタイルの依頼を取り消すと、タイルは頼み直さず空のまま残る)。
    private static readonly SemaphoreSlim QueueSlots=new(MaximumPending,MaximumPending);
    private static readonly LinkedList<Work>Queue=new();
    private static readonly Dictionary<string,Work>Pending=new(StringComparer.Ordinal);
    private static readonly Dictionary<string,CacheEntry>Cache=new(StringComparer.Ordinal);
    private static readonly LinkedList<string>Recency=new();
    private static readonly Dictionary<string,SimpleMesh>Primitives=new(StringComparer.Ordinal);
    private static bool Running;
    private static int Bytes;
    private static long Generation;
    private static readonly string PrimitiveCacheName="primitives-"+Guid.NewGuid().ToString("N");
    private sealed class CacheEntry(BitmapSource Image,LinkedListNode<string>Node){
        internal readonly BitmapSource Image=Image;
        internal readonly LinkedListNode<string>Node=Node;
        internal int Bytes=>this.Image.PixelWidth*this.Image.PixelHeight*4;
    }
    private sealed class Work(string Key,AssetWorkspaceItem Item,Project?Project,int Edge){
        internal readonly string Key=Key;
        internal readonly AssetWorkspaceItem Item=Item;
        internal readonly Project?Project=Project;
        internal readonly int Edge=Edge;
        internal readonly CancellationTokenSource Stop=new();
        internal CancellationToken Cancellation=>this.Stop.Token;
        internal int Subscribers;
        internal readonly TaskCompletionSource<BitmapSource?>Completion=new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    /// <summary>Inventory refresh invalidates transitive source dependencies that are not themselves a tile.</summary>
    internal static void Invalidate(){ lock(Gate){ Generation++;Cache.Clear();Recency.Clear();Bytes=0; } }
    internal static bool SupportsThumbnail(AssetWorkspaceItem Item,AssetKey?ThumbnailKey=null)=>
        Item.Kind is AssetWorkspaceKind.Scene or AssetWorkspaceKind.Model or AssetWorkspaceKind.Texture or AssetWorkspaceKind.Image or AssetWorkspaceKind.Font
        &&ThumbnailKey?.SubAssetId is null;
    internal static async Task<BitmapSource?>RequestAsync(AssetWorkspaceItem Item,CancellationToken Cancellation,AssetKey?ThumbnailKey=null,int Edge=AssetThumbnailRequest.DefaultEdge){
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(Cancellation);
        var Operation=RequestCoreAsync(Item,Cancellation,ThumbnailKey,Edge);
        return await Operation.ConfigureAwait(false);
    }
    private static Task<BitmapSource?>RequestCoreAsync(AssetWorkspaceItem Item,CancellationToken Cancellation,AssetKey?ThumbnailKey,int Edge){
        // A child must supply its real artifact/selector before it can be previewed; never reuse its parent's picture.
        if(Cancellation.IsCancellationRequested||!SupportsThumbnail(Item,ThumbnailKey)){ return Task.FromResult<BitmapSource?>(null); }
        Project?Project=null;
        if(Item.Kind==AssetWorkspaceKind.Scene){
            Project=AssetDocumentWorkspace.Workspace.CurrentSolution.Projects.FirstOrDefault(P=>string.Equals(P.FilePath,Item.Source.ProjectPath,StringComparison.OrdinalIgnoreCase));
            if(Project is null){ return Task.FromResult<BitmapSource?>(null); }
        }
        var File=new FileInfo(Item.Source.SourcePath);
        var Key=(ThumbnailKey??Item.GetKey())?.ToString()+"|"+Item.Source.SourcePath+"|"+File.Length+"|"+File.LastWriteTimeUtc.Ticks+"|"+Item.MetadataSha256+"|"+Project?.Version.ToString()+"|"+Generation+"|"+Edge;
        lock(Gate){
            if(Cache.TryGetValue(Key,out var Existing)){ Recency.Remove(Existing.Node);Recency.AddFirst(Existing.Node);return Task.FromResult<BitmapSource?>(Existing.Image); }
            if(Pending.TryGetValue(Key,out var ExistingWork)&&!ExistingWork.Cancellation.IsCancellationRequested){ return SubscribeAsync(ExistingWork,Cancellation); }
        }
        return AdmitAsync(Key,Item,Project,Edge,Cancellation);
    }
    /// <summary>
    /// 空きを待ってから順番待ちに入れる。待っている間に呼び手が取り消せば、その依頼だけが抜ける。
    /// 画面から外れたタイルは自分の取り消しで抜けるので、見えている依頼を押し出す必要はない。1 つの依頼が二重に走ることもない。
    /// </summary>
    private static async Task<BitmapSource?>AdmitAsync(string Key,AssetWorkspaceItem Item,Project?Project,int Edge,CancellationToken Cancellation){
        await QueueSlots.WaitAsync(Cancellation).ConfigureAwait(false);
        var OwnsSlot=true;
        Task<BitmapSource?>Operation;
        try {
            lock(Gate){
                Cancellation.ThrowIfCancellationRequested();
                if(Cache.TryGetValue(Key,out var Existing)){ Recency.Remove(Existing.Node);Recency.AddFirst(Existing.Node);return Existing.Image; }
                if(Pending.TryGetValue(Key,out var ExistingWork)&&!ExistingWork.Cancellation.IsCancellationRequested){ Operation=SubscribeAsync(ExistingWork,Cancellation); }
                else{
                    var Next=new Work(Key,Item,Project,Edge);Queue.AddFirst(Next);Pending[Key]=Next;OwnsSlot=false;
                    if(!Running){ Running=true;_=Task.Run(RunAsync); }
                    Operation=SubscribeAsync(Next,Cancellation);
                }
            }
        }finally { if(OwnsSlot){ QueueSlots.Release(); } }
        return await Operation.ConfigureAwait(false);
    }
    private static async Task<BitmapSource?>SubscribeAsync(Work Work,CancellationToken Cancellation){
        lock(Gate){ Work.Subscribers++; }
        try {
            var Cancelled=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using(Cancellation.Register(()=>Cancelled.TrySetResult(true))){
                // The Task.Run worker uses immutable Roslyn snapshots, CLI calls and frozen bitmaps only.
                // Its ConfigureAwait(false) chain never needs the UI thread or a joinable-task context.
#pragma warning disable VSTHRD003
                await Task.WhenAny(Work.Completion.Task,Cancelled.Task).ConfigureAwait(false);
                Cancellation.ThrowIfCancellationRequested();
                return await Work.Completion.Task.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            }
        }finally { lock(Gate){ Work.Subscribers--;if(Work.Subscribers==0&&!Work.Completion.Task.IsCompleted){ Work.Stop.Cancel(); } } }
    }
    private static async Task RunAsync(){
        while(true){
            Work Next;
            lock(Gate){
                if(Queue.First is null){ Running=false;return; }
                Next=Queue.First.Value;Queue.RemoveFirst();QueueSlots.Release();
            }
            try {
                Next.Cancellation.ThrowIfCancellationRequested();
                var Image=await GenerateAsync(Next).ConfigureAwait(false);
                Next.Cancellation.ThrowIfCancellationRequested();
                lock(Gate){
                    if(Cache.TryGetValue(Next.Key,out var Prior)){ Recency.Remove(Prior.Node);Bytes-=Prior.Bytes; }
                    var Node=Recency.AddFirst(Next.Key);var Entry=new CacheEntry(Image,Node);Cache[Next.Key]=Entry;Bytes+=Entry.Bytes;
                    while(Cache.Count>MaximumEntries||Bytes>MaximumBytes){ var Last=Recency.Last!;var Removed=Cache[Last.Value];Bytes-=Removed.Bytes;Cache.Remove(Last.Value);Recency.RemoveLast(); }
                }
                Next.Completion.TrySetResult(Image);
            }catch(OperationCanceledException){ Next.Completion.TrySetCanceled(); }
            catch(Exception Error){
                System.Diagnostics.Trace.TraceWarning(nameof(AssetThumbnailService)+": "+Error.Message);
                Next.Completion.TrySetResult(null);
            }finally { lock(Gate){ if(Pending.TryGetValue(Next.Key,out var Current)&&ReferenceEquals(Current,Next)){ Pending.Remove(Next.Key); }Next.Stop.Dispose(); } }
        }
    }
    private static async Task<BitmapSource>GenerateAsync(Work Work){
        if(Work.Item.Kind==AssetWorkspaceKind.Font){ return await FontTileAsync(Work.Item,Work.Cancellation).ConfigureAwait(false); }
        var Item=Work.Item;var Context=CgeProjectContext.Resolve(Item.Source.ProjectPath)??throw new InvalidOperationException("Game project is unavailable.");
        var Request=new AssetThumbnailRequest{MaxEdge=Work.Edge};
        if(Item.Kind is AssetWorkspaceKind.Texture or AssetWorkspaceKind.Image){ Request.ImagePath=Item.Source.SourcePath; }
        else{
            if(Item.Kind==AssetWorkspaceKind.Model){
                // モデルは Studio（床・壁・天井のある部屋）で描く。シーンは自分の世界を描くので従来どおり。
                Request.Scene=StudioScene(LoadMesh(Item.Source.SourcePath,Context),out var Lighting);
                Request.Lighting=Lighting;
            } else { Request.Scene=await SceneAsync(Work,Context).ConfigureAwait(false); }
            Request.ShaderArtifactPath=RenderPreviewSceneBuilder.ResolveShaderArtifactPath(Context.WorkspaceRoot);
        }
        Work.Cancellation.ThrowIfCancellationRequested();
        var Error=AssetThumbnailRequest.Validate(Request);if(Error is not null){ throw new InvalidDataException(Error); }
        var Temp=Path.Combine(Path.GetTempPath(),"CgeAssetThumbnails",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Temp);
        try {
            var Input=Path.Combine(Temp,"request.json");var Output=Path.Combine(Temp,"thumbnail.png");
            var Json=JsonSerializer.Serialize(Request);
            if(Json.Length>32*1024*1024){ throw new InvalidDataException("Thumbnail scene exceeds its transfer bound."); }
            File.WriteAllText(Input,Json);
            var ImageRequest=Request.Scene is null;
            var Host=ImageRequest
                ?BridgeExecutableLocator.FindExecutable(Context.WorkspaceRoot,@"tools\Cook.Cli")??throw new FileNotFoundException("The packaged Cook thumbnail worker is unavailable.")
                :RenderPreviewSceneBuilder.ResolveHostPath(Context.WorkspaceRoot);
            if(!Host.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)){ throw new FileNotFoundException("The packaged thumbnail worker executable is unavailable."); }
            ExternalBridgeProcess.RunExecutableCommandProducingFile(Host,ImageRequest?"Cook.Cli":"EditorPreview.Host",[ImageRequest?"thumbnail-image":"--thumbnail",Input,Output],Output,Context.WorkspaceRoot,TimeSpan.FromSeconds(30),false,Work.Cancellation);
            Work.Cancellation.ThrowIfCancellationRequested();
            using(var Stream=File.OpenRead(Output)){
                if(Stream.Length>Work.Edge*Work.Edge*8){ throw new InvalidDataException("Thumbnail PNG exceeds its bound."); }
                var Bitmap=new BitmapImage();Bitmap.BeginInit();Bitmap.CacheOption=BitmapCacheOption.OnLoad;Bitmap.StreamSource=Stream;Bitmap.EndInit();Bitmap.Freeze();
                if(Bitmap.PixelWidth<1||Bitmap.PixelWidth>Work.Edge||Bitmap.PixelHeight<1||Bitmap.PixelHeight>Work.Edge){ throw new InvalidDataException("Thumbnail dimensions are invalid."); }
                return Bitmap;
            }
        }finally { try { Directory.Delete(Temp,true); }catch(IOException){ }catch(UnauthorizedAccessException){ } }
    }
    /// <summary>モデルを Studio の部屋に置いた 128 px 用のシーンと照明（エディターのプレビューと同じ構成・カメラ既定）。</summary>
    internal static RenderPreviewScene StudioScene(SimpleMesh Mesh,out RenderPreviewLighting Lighting){
        var Template=PreviewLevelBuiltIns.Studio();
        var Min=new Vector3(float.PositiveInfinity);var Max=new Vector3(float.NegativeInfinity);
        foreach(var Position in Mesh.Positions){ Min=Vector3.Min(Min,Position);Max=Vector3.Max(Max,Position); }
        if(Mesh.Positions.Length==0){ Min=Max=Mesh.BoundsCenter; }
        var Bounds=new PreviewBounds(Min,Max,Mesh.BoundsCenter,Mesh.BoundsRadius);
        var Composition=PreviewSceneComposer.Compose(Mesh,Bounds,Template,128,128);
        var Camera=new AssetViewportCamera();
        Camera.SetFarReach(PreviewSceneComposer.FarReachRadii(Template));
        Camera.Frame(Bounds.Center,Bounds.Radius,1f,Template.Camera);
        Composition.Scene.Camera=RenderPreviewSceneBuilder.CreateCamera(Camera.View,Camera.Projection(1f),Camera.Eye,Camera.FovYRadians);
        Lighting=PreviewLevelWire.CreateLighting(Template,Bounds,1);
        return Composition.Scene;
    }
    /// <summary>フォントのタイル: そのフォント自身で描いた "Aa"（暗い背景）。読めない・描ける字形がなければ例外で、タイルは汎用アイコンのまま。</summary>
    private static async Task<BitmapSource>FontTileAsync(AssetWorkspaceItem Item,CancellationToken Cancellation){
        var Data=await AssetFontData.LoadAsync(Item.Source.SourcePath,Cancellation).ConfigureAwait(false)??throw new InvalidDataException("The font cannot be read.");
        var Tile=Data.CreateTile(128);
        if(Tile is null){
            // .ttf/.otf は WPF の描画なので UI スレッドで作る。
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(Cancellation);
            Tile=Data.CreateTrueTypeTile(128);
        }
        return Tile??throw new InvalidDataException("The font has no drawable glyphs.");
    }
    private static SimpleMesh LoadMesh(string PathValue,CgeProjectContext Context){
        var Mesh=MeshLoader.Load(PathValue,Context.WorkspaceRoot);
        if(Mesh.Positions.Length>250_000||Mesh.Indices.Length>750_000){ throw new InvalidDataException("Model exceeds the thumbnail geometry budget."); }
        return Mesh;
    }
    private static async Task<RenderPreviewScene>SceneAsync(Work Work,CgeProjectContext Context){
        var Project=Work.Project??throw new InvalidOperationException("Scene workspace is unavailable.");
        var Document=Project.AdditionalDocuments.FirstOrDefault(D=>string.Equals(D.FilePath,Work.Item.Source.SourcePath,StringComparison.OrdinalIgnoreCase))??throw new InvalidOperationException("Scene document is unavailable.");
        var Compilation=await Project.GetCompilationAsync(Work.Cancellation).ConfigureAwait(false)??throw new InvalidOperationException("Scene compilation is unavailable.");
        var Model=await AnalyzeSceneAsync(Project,Compilation,Document,Work.Cancellation).ConfigureAwait(false);
        var Objects=SceneModelRenderObjectBuilder.IsGameMode(Model)
            ?await BuildStartupGeometryAsync(Project,Model,Context,Work.Cancellation).ConfigureAwait(false)
            :BuildGeometry(Model,Context,Work.Cancellation);
        if(Objects.Count==0){ throw new InvalidDataException("Scene has no supported geometry."); }
        Work.Cancellation.ThrowIfCancellationRequested();
        return Frame(Objects);
    }
    internal static async Task<IReadOnlyList<SceneRenderObject>>BuildStartupGeometryAsync(Project Project,SceneModel GameMode,CgeProjectContext Context,CancellationToken Cancellation){
        var Compilation=await Project.GetCompilationAsync(Cancellation).ConfigureAwait(false)??throw new InvalidOperationException("Scene compilation is unavailable.");
        var Objects=new List<SceneRenderObject>();
        Append(GameMode);
        foreach(var Document in SceneJsonWorkspaceSource.JsonScenes(Project)){
            if(string.Equals(Document.FilePath,GameMode.FilePath,StringComparison.OrdinalIgnoreCase)){ continue; }
            var Text=await Document.GetTextAsync(Cancellation).ConfigureAwait(false);
            if(SceneJsonWorkspaceSource.TryReadClassFullyQualifiedName(Text.ToString()) is null||SceneJsonReader.Parse(Text.ToString()).Get("startupOrder") is null){ continue; }
            var Model=await AnalyzeSceneAsync(Project,Compilation,Document,Cancellation).ConfigureAwait(false);
            if(Model.IsStartupScene){ Append(Model); }
        }
        return Objects;
        void Append(SceneModel Model){
            foreach(var Object in BuildGeometry(Model,Context,Cancellation)){
                Objects.Add(new(){Mesh=Object.Mesh,World=Object.World,NodeId=Object.NodeId,
                    PartId=Model.ClassFullyQualifiedName+"/"+Object.RenderId,IncludeInBounds=Object.IncludeInBounds,IsPickable=false});
            }
        }
    }
    private static async Task<SceneModel>AnalyzeSceneAsync(Project Project,Compilation Compilation,TextDocument Document,CancellationToken Cancellation){
        var Text=await Document.GetTextAsync(Cancellation).ConfigureAwait(false);
        var ClassName=SceneJsonWorkspaceSource.TryReadClassFullyQualifiedName(Text.ToString())??throw new InvalidDataException("Scene declaration is invalid.");
        var Added=await SceneJsonWorkspaceSource.AddDesignerTreeAsync(Project,Compilation,Document,Cancellation).ConfigureAwait(false)??throw new InvalidDataException("Scene analysis failed.");
        var Type=Added.Compilation.GetTypeByMetadataName(ClassName)??throw new InvalidDataException("Scene type is unavailable.");
        var Model=SceneDeclarationAnalyzer.Analyze(Type,Added.Compilation,Cancellation);
        if(Model.HasErrors){ throw new InvalidDataException("Scene analysis has errors."); }
        return Model;
    }
    private static IReadOnlyList<SceneRenderObject>BuildGeometry(SceneModel Model,CgeProjectContext Context,CancellationToken Cancellation){
        SceneModelRenderObjectBuilder.Prepare(Model,Context);
        return SceneModelRenderObjectBuilder.Build(Model,Name=>Primitive(Name,Context,Cancellation),PathValue=>LoadMesh(Context.ResolveProjectPath(PathValue),Context),IncludePlaceholders:false);
    }
    private static RenderPreviewScene Frame(IReadOnlyList<SceneRenderObject>Objects){
        if(Objects.Sum(O=>(long)O.Mesh.Positions.Length)>250_000||Objects.Sum(O=>(long)O.Mesh.Indices.Length)>750_000){ throw new InvalidDataException("Scene exceeds the thumbnail geometry budget."); }
        var Bounds=SceneCameraFraming.ComputeBounds(Objects);var Fov=SceneCameraFraming.DefaultFovYRadians;
        var Frame=SceneCameraFraming.FrameBounds(Bounds.Center,Bounds.Radius,Fov,1f);
        var Eye=Bounds.Center+SceneCameraFraming.ViewDirection*SceneCameraFraming.ComputeDistance(Bounds.Radius,Fov,1f);
        return RenderPreviewSceneBuilder.FromScene(Objects,Frame.View,Frame.Projection,Eye);
    }
    private static SimpleMesh?Primitive(string Name,CgeProjectContext Context,CancellationToken Cancellation){
        var Kind=Name switch {"Plane"=>"Rectangle","Box"=>"Box","Sphere"=>"Sphere","Cylinder"=>"Cylinder","Cone"=>"Cone","Torus"=>"Torus","Disk"=>"Disk","Capsule"=>"Capsule",_=>null};
        if(Kind is null){ return null; }
        var Key=Context.WorkspaceRoot+"|"+Kind;
        lock(Primitives){
            if(Primitives.TryGetValue(Key,out var Existing)){ return Existing; }
            // The Cook reader confines dependencies to this workspace.
            var DirectoryPath=Path.Combine(Context.WorkspaceRoot,"obj","cge-content","asset-thumbnails",PrimitiveCacheName);
            Directory.CreateDirectory(DirectoryPath);
            var Output=Path.Combine(DirectoryPath,Kind+".glb");
            if(!File.Exists(Output)){
                var Tool=BridgeExecutableLocator.FindExecutable(Context.WorkspaceRoot,BridgeTool.Primitive.ProjectRelativePath)??throw new FileNotFoundException("The packaged primitive producer is unavailable.");
                ExternalBridgeProcess.RunExecutableCommandProducingFile(Tool,BridgeTool.Primitive,[Kind,Output],Output,Context.WorkspaceRoot,TimeSpan.FromSeconds(30),true,Cancellation);
            }
            var Mesh=ExternalGltfLoader.Load(Output,Context.WorkspaceRoot);
            Primitives.Add(Key,Mesh);
            return Mesh;
        }
    }
}
