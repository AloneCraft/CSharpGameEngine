namespace CSharpGameEngine.Contracts;

/// <summary>
/// L8プレビュークライアントと分離されたRender Serverホスト間で使う、バージョン付きのプロバイダー非依存通信契約。
/// この境界を越えるのはプレゼンテーション相対の<see cref="float"/>値のみ。ネイティブのグラフィックスオブジェクトと、
/// エンジン所有の正本状態は越えない。
/// </summary>
public static class RenderPreviewHostProtocol {
    /// <summary>
    /// 13：スケルタルのレスト姿勢境界と統計をホスト応答に追加した。
    /// 12：ホストによる静的 glTF 読み込みを追加した。
    /// 11（ADR-0090）：ホストによるスケルタル読み込み（RenderPreviewAssetSource）、
    /// 要約応答（RenderPreviewAssetSummary）、時刻指定による姿勢評価（RenderPreviewAnimation）を追加した。
    /// 10（M0127）：アセットエディター用に、フレームへ照明・背景・環境（<see cref="RenderPreviewLighting"/>）を、
    /// マテリアルへ粗さ・金属度・ワールドグリッドを追加した。
    /// 9（M0119）：透視深度はネイティブの対数方式であり、セレクターを削除して投影メタデータを必須にした。
    /// 8（M0100）：遷移長と深度セレクターを導入した。7（M0097）：ランタイム診断スナップショットの
    /// コマンドとペイロードを削除した。
    /// </summary>
    public const int CurrentVersion=13;
    public const int MaximumDimension=16_384;
    public const int MaximumMeshCount=4_096;
    public const int MaximumMaterialCount=4_096;
    public const int MaximumObjectCount=100_000;
    public const int MaximumOverlayVertexCount=32_768;
    public const int MaximumCameraPreviewCount=3;
    /// <summary>float型の円周率（netstandard2.0にはMathFがない）。</summary>
    private const float Pi=3.14159274f;

    public static string?Validate(RenderPreviewHostRequest Request){
        if(Request is null){ return "Request is required."; }
        if(Request.ProtocolVersion!=CurrentVersion){
            return $"Unsupported render preview host protocol {Request.ProtocolVersion}; expected {CurrentVersion}.";
        }
        if(Request.RequestId==Guid.Empty){ return "RequestId is required."; }
        if(Request.SessionId==Guid.Empty){ return "SessionId is required."; }
        if(Request.Sequence==0){ return "Sequence must be greater than zero."; }
        if(!Enum.IsDefined(typeof(RenderPreviewHostCommand),Request.Command)){
            return "Render preview host command is unknown.";
        }
        if(Request.RuntimeView is not null&&Request.Command!=RenderPreviewHostCommand.Open){
            return "Only Open may request a runtime view.";
        }
        if(Request.SceneJson.Length>0&&Request.Command is not (RenderPreviewHostCommand.Open or RenderPreviewHostCommand.ReplaceScene)){
            return "Only Open and ReplaceScene carry a scene document.";
        }

        return Request.Command switch {
            RenderPreviewHostCommand.Open=>ValidateOpen(Request),
            RenderPreviewHostCommand.ReplaceScene=>ValidateReplaceScene(Request),
            RenderPreviewHostCommand.UpdateFrame=>ValidateUpdateFrame(Request),
            RenderPreviewHostCommand.Resize=>ValidateResize(Request),
            RenderPreviewHostCommand.Render=>ValidateRender(Request),
            RenderPreviewHostCommand.Close=>ValidateEmptyCommand(Request),
            _=>"Render preview host command is unknown.",
        };
    }

    private static string?ValidateOpen(RenderPreviewHostRequest Request){
        if(Request.SurfaceHandle==0){ return "Open requires a surface handle."; }
        if(!ValidPositiveDimensions(Request.Width,Request.Height)){
            return $"Open dimensions must both be between 1 and {MaximumDimension}.";
        }
        if(string.IsNullOrWhiteSpace(Request.ShaderArtifactPath)){ return "Open requires a shader artifact path."; }
        if(Request.Scene is null){ return "Open requires a scene."; }
        if(Request.RuntimeView is { }RuntimeView){
            if(string.IsNullOrWhiteSpace(RuntimeView.AssemblyPath)
                ||!RuntimeView.AssemblyPath.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)){
                return "A runtime view requires the game assembly path.";
            }
            if(string.IsNullOrWhiteSpace(RuntimeView.SceneName)){ return "A runtime view requires the scene name."; }
        }
        return ValidateScene(Request.Scene);
    }

    private static string?ValidateReplaceScene(RenderPreviewHostRequest Request){
        if(Request.SurfaceHandle!=0||Request.Width!=0||Request.Height!=0||!string.IsNullOrEmpty(Request.ShaderArtifactPath)
            ||Request.Frame is not null||Request.CaptureForVerification){
            return "ReplaceScene must not include surface, dimensions, or a shader artifact path.";
        }
        if(Request.Scene is null){ return "ReplaceScene requires a scene."; }
        return ValidateScene(Request.Scene);
    }

    private static string?ValidateResize(RenderPreviewHostRequest Request){
        if(Request.Scene is not null||Request.Frame is not null||Request.SurfaceHandle!=0
            ||!string.IsNullOrEmpty(Request.ShaderArtifactPath)||Request.CaptureForVerification){
            return "Resize must not include a scene, surface, or shader artifact path.";
        }
        bool Suspended=Request.Width==0&&Request.Height==0;
        if(!Suspended&&!ValidPositiveDimensions(Request.Width,Request.Height)){
            return $"Resize dimensions must both be zero or both be between 1 and {MaximumDimension}.";
        }
        return null;
    }

    private static string?ValidateEmptyCommand(RenderPreviewHostRequest Request){
        if(Request.Scene is not null||Request.Frame is not null||Request.SurfaceHandle!=0||Request.Width!=0||Request.Height!=0
            ||!string.IsNullOrEmpty(Request.ShaderArtifactPath)||Request.CaptureForVerification){
            return $"{Request.Command} must not include scene, surface, dimensions, or a shader artifact path.";
        }
        return null;
    }

    private static string?ValidateRender(RenderPreviewHostRequest Request){
        if(Request.Scene is not null||Request.Frame is not null||Request.SurfaceHandle!=0||Request.Width!=0||Request.Height!=0
            ||!string.IsNullOrEmpty(Request.ShaderArtifactPath)){
            return "Render must not include scene, surface, dimensions, or a shader artifact path.";
        }
        return null;
    }

    private static string?ValidateUpdateFrame(RenderPreviewHostRequest Request){
        if(Request.Scene is not null||Request.SurfaceHandle!=0||Request.Width!=0||Request.Height!=0
            ||!string.IsNullOrEmpty(Request.ShaderArtifactPath)||Request.CaptureForVerification){
            return "UpdateFrame must not include scene, surface, dimensions, shader artifact, or capture.";
        }
        if(Request.Frame?.Camera is null){ return "UpdateFrame requires a camera."; }
        string?Error=ValidateCamera(Request.Frame.Camera,"Frame camera");
        if(Error is not null){ return Error; }
        if(Request.Frame.Objects is null||Request.Frame.Objects.Length>MaximumObjectCount){
            return $"Frame object count must be between 0 and {MaximumObjectCount}.";
        }
        if(!Enum.IsDefined(
            typeof(RenderPreviewPresentationMode),Request.Frame.PresentationMode)){
            return "Frame presentation mode is unknown.";
        }
        string?LightingError=RenderPreviewLightingValidation.Validate(Request.Frame.Lighting);
        if(LightingError is not null){ return LightingError; }
        string?SkeletalError=RenderPreviewSkeletalValidation.Validate(Request.Frame);
        if(SkeletalError is not null){ return SkeletalError; }
        if(Request.Frame.OverlayTriangles is null||Request.Frame.OverlayLines is null){
            return "Frame overlay arrays are required.";
        }
        if(Request.Frame.TransformOverrides is null||Request.Frame.TransformOverrides.Length>MaximumObjectCount){
            return $"Frame transform override count must be between 0 and {MaximumObjectCount}.";
        }
        for(int Index=0;Index<Request.Frame.TransformOverrides.Length;Index++){
            RenderPreviewTransformOverride?Override=Request.Frame.TransformOverrides[Index];
            if(Override is null||string.IsNullOrWhiteSpace(Override.ObjectId)){
                return $"Frame transform override {Index} needs an object identity.";
            }
            if(Override.Position is not { Length:3 }||Override.Rotation is not { Length:4 }||Override.Scale is not { Length:3 }
                ||!Override.Position.All(IsFinite)||!Override.Rotation.All(IsFinite)||!Override.Scale.All(IsFinite)){
                return $"Frame transform override {Index} needs a finite position (3), rotation (4) and scale (3).";
            }
        }
        if(Request.Frame.CameraPreviews is null
            ||Request.Frame.CameraPreviews.Length>MaximumCameraPreviewCount){
            return $"Frame camera preview count must be between 0 and {MaximumCameraPreviewCount}.";
        }
        for(int Index=0;Index<Request.Frame.CameraPreviews.Length;Index++){
            RenderPreviewCameraPass?Preview=Request.Frame.CameraPreviews[Index];
            if(Preview is null){ return $"Frame camera preview {Index} is null."; }
            if(!ValidPositiveDimensions(Preview.Width,Preview.Height)){
                return $"Frame camera preview {Index} dimensions must both be between 1 and {MaximumDimension}.";
            }
            Error=ValidateCamera(Preview.Camera,$"Frame camera preview {Index} camera");
            if(Error is not null){ return Error; }
            if(Preview.ExcludedObjectId is null){
                return $"Frame camera preview {Index} excluded object identity is required.";
            }
            if(Preview.ExcludedObjectId.Length>0
                &&string.IsNullOrWhiteSpace(Preview.ExcludedObjectId)){
                return $"Frame camera preview {Index} excluded object identity is invalid.";
            }
        }
        int OverlayVertexCount;
        try {
            OverlayVertexCount=checked(
                Request.Frame.OverlayTriangles.Length
                +Request.Frame.OverlayLines.Length);
        } catch(OverflowException){
            return $"Frame overlay exceeds {MaximumOverlayVertexCount} vertices.";
        }
        if(OverlayVertexCount>MaximumOverlayVertexCount){
            return $"Frame overlay exceeds {MaximumOverlayVertexCount} vertices.";
        }
        if(Request.Frame.OverlayTriangles.Length%3!=0){
            return "Frame overlay triangle vertices must form complete triangles.";
        }
        if(Request.Frame.OverlayLines.Length%2!=0){
            return "Frame overlay line vertices must form complete lines.";
        }
        for(int Index=0;Index<Request.Frame.OverlayTriangles.Length;Index++){
            string?OverlayError=ValidateOverlayVertex(
                Request.Frame.OverlayTriangles[Index],Index,"triangle");
            if(OverlayError is not null){ return OverlayError; }
        }
        for(int Index=0;Index<Request.Frame.OverlayLines.Length;Index++){
            string?OverlayError=ValidateOverlayVertex(
                Request.Frame.OverlayLines[Index],Index,"line");
            if(OverlayError is not null){ return OverlayError; }
        }
        HashSet<string>Ids=new(StringComparer.Ordinal);
        for(int Index=0;Index<Request.Frame.Objects.Length;Index++){
            RenderPreviewObjectTransform?Object=Request.Frame.Objects[Index];
            if(Object is null){ return $"Frame object {Index} is null."; }
            if(string.IsNullOrWhiteSpace(Object.ObjectId)){ return $"Frame object {Index} identity is required."; }
            if(!Ids.Add(Object.ObjectId)){ return $"Frame object identity is duplicated: {Object.ObjectId}."; }
            Error=ValidateMatrix(Object.World,$"Frame object {Index} world matrix");
            if(Error is not null){ return Error; }
            if(!IsFinite(Object.SelectionWeight)
                ||Object.SelectionWeight<0f||Object.SelectionWeight>1f){
                return $"Frame object {Index} selection weight must be finite and between zero and one.";
            }
        }
        for(int Index=0;Index<Request.Frame.CameraPreviews.Length;Index++){
            string ExcludedObjectId=Request.Frame.CameraPreviews[Index].ExcludedObjectId;
            if(ExcludedObjectId.Length>0&&!Ids.Contains(ExcludedObjectId)){
                return $"Frame camera preview {Index} excluded object is not in the frame roster: {ExcludedObjectId}.";
            }
        }
        return null;
    }

    private static bool ValidPositiveDimensions(int Width,int Height)=>
        Width is>0 and<=MaximumDimension&&Height is>0 and<=MaximumDimension;

    private static string?ValidateScene(RenderPreviewScene Scene){
        if(Scene.Camera is null){ return "Scene camera is required."; }
        string?AssetError=RenderPreviewSkeletalValidation.Validate(Scene.Asset);
        if(AssetError is not null){ return AssetError; }
        if(!string.IsNullOrEmpty(Scene.StaticAssetPath)){
            if(Scene.Asset is not null){ return "A scene cannot contain both a static and skeletal asset."; }
            AssetError=RenderPreviewSkeletalValidation.ValidatePath(Scene.StaticAssetPath,"Static asset path",required:true);
            if(AssetError is not null){ return AssetError; }
        }
        if(Scene.Meshes is null||Scene.Meshes.Length>MaximumMeshCount){
            return $"Scene mesh count must be between 0 and {MaximumMeshCount}.";
        }
        if(Scene.Materials is null||Scene.Materials.Length>MaximumMaterialCount){
            return $"Scene material count must be between 0 and {MaximumMaterialCount}.";
        }
        if(Scene.Objects is null||Scene.Objects.Length>MaximumObjectCount){
            return $"Scene object count must be between 0 and {MaximumObjectCount}.";
        }
        if(Scene.Objects.Length==0){
            if(Scene.Meshes.Length!=0||Scene.Materials.Length!=0){
                return "An empty scene must not retain unused meshes or materials.";
            }
        } else if(Scene.Meshes.Length==0||Scene.Materials.Length==0){
            return "A non-empty scene requires at least one mesh and material.";
        }
        string?Error=ValidateCamera(Scene.Camera,"Camera");
        if(Error is not null){ return Error; }

        for(int MeshIndex=0;MeshIndex<Scene.Meshes.Length;MeshIndex++){
            Error=ValidateMesh(Scene.Meshes[MeshIndex],MeshIndex);
            if(Error is not null){ return Error; }
        }
        for(int MaterialIndex=0;MaterialIndex<Scene.Materials.Length;MaterialIndex++){
            RenderPreviewMaterial?Material=Scene.Materials[MaterialIndex];
            if(Material is null){ return $"Material {MaterialIndex} is null."; }
            if(Material.BaseColor is null||Material.BaseColor.Length!=4){
                return $"Material {MaterialIndex} base color must contain four values.";
            }
            for(int ValueIndex=0;ValueIndex<Material.BaseColor.Length;ValueIndex++){
                if(!IsFinite(Material.BaseColor[ValueIndex])){
                    return $"Material {MaterialIndex} base color must be finite.";
                }
            }
            if(Material.BaseColor[3]<0f||Material.BaseColor[3]>1f){
                return $"Material {MaterialIndex} alpha must be between zero and one.";
            }
            if(!IsFinite(Material.Roughness)||Material.Roughness<0f||Material.Roughness>1f
                ||!IsFinite(Material.Metallic)||Material.Metallic<0f||Material.Metallic>1f){
                return $"Material {MaterialIndex} roughness and metallic must be between zero and one.";
            }
            string?GridError=RenderPreviewLightingValidation.Validate(Material.WorldGrid,MaterialIndex);
            if(GridError is not null){ return GridError; }
        }

        HashSet<string>ObjectIds=new(StringComparer.Ordinal);
        for(int ObjectIndex=0;ObjectIndex<Scene.Objects.Length;ObjectIndex++){
            RenderPreviewObject?Object=Scene.Objects[ObjectIndex];
            if(Object is null){ return $"Object {ObjectIndex} is null."; }
            if(string.IsNullOrWhiteSpace(Object.ObjectId)){ return $"Object {ObjectIndex} identity is required."; }
            if(!ObjectIds.Add(Object.ObjectId)){ return $"Object identity is duplicated: {Object.ObjectId}."; }
            if((uint)Object.MeshIndex>=(uint)Scene.Meshes.Length){ return $"Object {ObjectIndex} mesh index is invalid."; }
            if((uint)Object.MaterialIndex>=(uint)Scene.Materials.Length){ return $"Object {ObjectIndex} material index is invalid."; }
            Error=ValidateMatrix(Object.World,$"Object {ObjectIndex} world matrix");
            if(Error is not null){ return Error; }
        }
        return null;
    }

    private static string?ValidateMesh(RenderPreviewMesh?Mesh,int MeshIndex){
        if(Mesh is null){ return $"Mesh {MeshIndex} is null."; }
        if(Mesh.Positions is null||Mesh.Positions.Length<9||Mesh.Positions.Length%3!=0){
            return $"Mesh {MeshIndex} positions must contain at least three xyz vertices.";
        }
        if(Mesh.Normals is null||Mesh.Normals.Length!=Mesh.Positions.Length){
            return $"Mesh {MeshIndex} normals must match its positions.";
        }
        if(Mesh.Indices is null||Mesh.Indices.Length<3||Mesh.Indices.Length%3!=0){
            return $"Mesh {MeshIndex} indices must contain complete triangles.";
        }
        foreach(float Value in Mesh.Positions){
            if(!IsFinite(Value)){ return $"Mesh {MeshIndex} positions must be finite."; }
        }
        foreach(float Value in Mesh.Normals){
            if(!IsFinite(Value)){ return $"Mesh {MeshIndex} normals must be finite."; }
        }
        uint VertexCount=(uint)(Mesh.Positions.Length/3);
        foreach(uint Index in Mesh.Indices){
            if(Index>=VertexCount){ return $"Mesh {MeshIndex} index {Index} is outside its vertex range."; }
        }
        return null;
    }

    private static string?ValidateMatrix(RenderPreviewMatrix4x4?Matrix,string Name){
        if(Matrix?.Values is null||Matrix.Values.Length!=16){ return $"{Name} must contain sixteen values."; }
        foreach(float Value in Matrix.Values){
            if(!IsFinite(Value)){ return $"{Name} must be finite."; }
        }
        return null;
    }

    private static string?ValidateCamera(RenderPreviewCamera?Camera,string Name){
        if(Camera is null){ return $"{Name} is required."; }
        string?Error=ValidateMatrix(Camera.ViewProjection,$"{Name} view-projection");
        if(Error is not null){ return Error; }
        if(Camera.Position is null||Camera.Position.Length!=3){
            return $"{Name} position must contain three values.";
        }
        foreach(float Value in Camera.Position){
            if(!IsFinite(Value)){ return $"{Name} position must be finite."; }
        }
        if(Camera.Rotation is null||Camera.Rotation.Length!=4){
            return $"{Name} rotation must contain four values.";
        }
        foreach(float Value in Camera.Rotation){
            if(!IsFinite(Value)){ return $"{Name} rotation must be finite."; }
        }
        if(!IsFinite(Camera.FovYRadians)||!IsFinite(Camera.NearZ)||!IsFinite(Camera.FarZ)
            ||Camera.FovYRadians<0f||Camera.NearZ<0f||Camera.FarZ<Camera.NearZ){
            return $"{Name} projection must be finite, non-negative and ordered.";
        }
        var Values=Camera.ViewProjection.Values;
        var Perspective=Values[3]!=0f||Values[7]!=0f||Values[11]!=0f;
        if(Perspective){
            if(!(Camera.FovYRadians>0f&&Camera.FovYRadians<Pi)
                ||!(Camera.NearZ>0f)||!(Camera.FarZ>Camera.NearZ)
                ||!IsFinite(Camera.RadialTransitionMeters)||!(Camera.RadialTransitionMeters>0f)){
                return $"{Name} logarithmic depth needs a field of view in (0, pi), 0 < NearZ < FarZ and a positive transition length.";
            }
        } else if(Values[15]==0f){
            return $"{Name} orthographic projection needs a nonzero homogeneous scale.";
        }
        return null;
    }

    private static bool IsFinite(float Value)=>!float.IsNaN(Value)&&!float.IsInfinity(Value);

    private static string?ValidateOverlayVertex(
        RenderPreviewOverlayVertex?Vertex,int Index,string Topology){
        if(Vertex is null){ return $"Frame overlay {Topology} vertex {Index} is null."; }
        if(Vertex.Position is null||Vertex.Position.Length!=3
            ||Vertex.Color is null||Vertex.Color.Length!=4){
            return $"Frame overlay {Topology} vertex {Index} has an invalid shape.";
        }
        foreach(float Value in Vertex.Position){
            if(!IsFinite(Value)){
                return $"Frame overlay {Topology} vertex {Index} position must be finite.";
            }
        }
        foreach(float Value in Vertex.Color){
            if(!IsFinite(Value)){
                return $"Frame overlay {Topology} vertex {Index} color must be finite.";
            }
        }
        if(Vertex.Color[3]<0f||Vertex.Color[3]>1f){
            return $"Frame overlay {Topology} vertex {Index} alpha must be between zero and one.";
        }
        return null;
    }
}

public enum RenderPreviewHostCommand:byte {
    Open,
    ReplaceScene,
    UpdateFrame,
    Resize,
    Render,
    Close,
}

public enum RenderPreviewHostResultState:byte {
    Succeeded,
    Failed,
    VersionMismatch,
    SessionNotFound,
    StaleSequence,
    HostCrashed,
}

public enum RenderPreviewPresentationMode:byte {
    Lit,
    BaseColor,
    Normal,
    Wireframe,
}

public sealed class RenderPreviewHostRequest {
    public int ProtocolVersion { get;set; }=RenderPreviewHostProtocol.CurrentVersion;
    public Guid RequestId { get;set; }
    public Guid SessionId { get;set; }
    public ulong Sequence { get;set; }
    public RenderPreviewHostCommand Command { get;set; }
    public long SurfaceHandle { get;set; }
    public int Width { get;set; }
    public int Height { get;set; }
    public string ShaderArtifactPath { get;set; }=string.Empty;
    public bool CaptureForVerification { get;set; }
    public RenderPreviewScene?Scene { get;set; }
    public RenderPreviewFrame?Frame { get;set; }
    /// <summary>
    /// ADR-0070 B1：Open時、<see cref="Scene"/>ではなく、実行時環境を使ってビルド済みゲーム自身のワールドを描画する
    ///（ゲームのスタートアップシーンと<see cref="RenderPreviewRuntimeView.SceneName"/>）。
    /// </summary>
    public RenderPreviewRuntimeView?RuntimeView { get;set; }
    /// <summary>
    /// ランタイムビューのOpenおよびReplaceScene時に使う、編集中のシーンの現在のシーンJSON文書。
    /// ビルドせず、その値を実行中のワールドへ適用する。
    /// </summary>
    public string SceneJson { get;set; }=string.Empty;
}

public sealed class RenderPreviewRuntimeView {
    /// <summary>ビルド済みゲームのアセンブリ。通常はコピーを使い、ビルド出力を書き込み可能な状態に保つ。</summary>
    public string AssemblyPath { get;set; }=string.Empty;
    /// <summary>ゲームビルドのディレクトリ。コンテンツのパスはこのディレクトリを基準とする。空の場合はアセンブリのディレクトリを使う。</summary>
    public string ContentDirectory { get;set; }=string.Empty;
    public string SceneName { get;set; }=string.Empty;
    public bool UseWarp { get;set; }
}

public sealed class RenderPreviewHostResponse {
    public int ProtocolVersion { get;set; }=RenderPreviewHostProtocol.CurrentVersion;
    public Guid RequestId { get;set; }
    public Guid SessionId { get;set; }
    public ulong Sequence { get;set; }
    public RenderPreviewHostResultState State { get;set; }
    public uint DeviceGeneration { get;set; }
    public ulong CompletedFence { get;set; }
    public string FrameHash { get;set; }=string.Empty;
    public string DiagnosticCode { get;set; }=string.Empty;
    public string Message { get;set; }=string.Empty;
    public RenderPreviewCameraDiagnostics[]CameraPreviews { get;set; }=[];
    /// <summary>
    /// 実行中のビルド後にシーン文書の構造（ノードまたはコンポーネント型）が変わったランタイムビュー。
    /// 次のビルドまでは、ビルド時の構造に文書の値を適用して表示する。
    /// </summary>
    public bool BuildRequired { get;set; }
    /// <summary>
    /// M0096（ADR-0047）：ランタイムビューのエディターワールドが、ホストプロセスのSpatialデバッグパイプ上で持つID。
    /// 存在しない場合は空。Spatial要求にワールドIDがなければ、このワールドが既に選択される。
    /// </summary>
    public Guid EditorSpatialWorldId { get;set; }
    /// <summary>Open／ReplaceScene でスケルタルアセットを読んだときの要約（ADR-0090）。それ以外は null。</summary>
    public RenderPreviewAssetSummary?AssetSummary { get;set; }
    /// <summary>静的 glTF の描画ノード全体の境界。対象がない場合は null。</summary>
    public RenderPreviewAssetBounds?StaticBounds { get;set; }
}

public sealed class RenderPreviewAssetBounds {
    public float[]Min { get;set; }=[];
    public float[]Max { get;set; }=[];
    /// <summary>参照される各プリミティブを一度ずつ数えた頂点数。</summary>
    public int VertexCount { get;set; }
    /// <summary>参照される各プリミティブを一度ずつ数えた三角形数。</summary>
    public int TriangleCount { get;set; }
}

public sealed class RenderPreviewScene {
    public RenderPreviewCamera?Camera { get;set; }
    public RenderPreviewMesh[]Meshes { get;set; }=[];
    public RenderPreviewMaterial[]Materials { get;set; }=[];
    public RenderPreviewObject[]Objects { get;set; }=[];
    /// <summary>ホストが自分で読み込むスケルタル glTF（ADR-0090）。明示分の後ろにメッシュ・マテリアル・オブジェクトとして足される。</summary>
    public RenderPreviewAssetSource?Asset { get;set; }
    /// <summary>ホストが読み込む静的 glTF の絶対パス。空なら使用しない。</summary>
    public string StaticAssetPath { get;set; }=string.Empty;
}

public sealed class RenderPreviewFrame {
    public RenderPreviewCamera?Camera { get;set; }
    public RenderPreviewPresentationMode PresentationMode { get;set; }
    /// <summary>プレビュー環境（照明・背景・環境）。null ならホストは直前の内容を変えない（最初のフレームではホストの従来の既定）。</summary>
    public RenderPreviewLighting?Lighting { get;set; }
    /// <summary>スケルタルアセットのアニメーション姿勢（ADR-0090）。null なら直前の姿勢を保つ（最初のフレームはバインド姿勢）。</summary>
    public RenderPreviewAnimation?Animation { get;set; }
    /// <summary>ホストがスケルトンの骨をオーバーレイの線として描く。</summary>
    public bool ShowSkeleton { get;set; }
    /// <summary>強調するジョイントの番号。-1 なら強調しない。</summary>
    public int SelectedJoint { get;set; }=-1;
    public RenderPreviewObjectTransform[]Objects { get;set; }=[];
    public RenderPreviewOverlayVertex[]OverlayTriangles { get;set; }=[];
    public RenderPreviewOverlayVertex[]OverlayLines { get;set; }=[];
    public RenderPreviewCameraPass[]CameraPreviews { get;set; }=[];
    /// <summary>
    /// M0096：ランタイムビューは<see cref="Objects"/>ではなくゲーム自身のワールドを描画する。そのためギズモのドラッグ時、
    /// ドラッグしたノードの親相対変換をここへ送り、文書が追いつくまで（コミットによるReplaceScene、またはキャンセル時）ビューへ適用する。
    /// </summary>
    public RenderPreviewTransformOverride[]TransformOverrides { get;set; }=[];
}

/// <summary>RUF座標系でのノードの親相対変換。Position（3値）、回転クォータニオンR、U、F、W（4値）、Scale（3値）。</summary>
public sealed class RenderPreviewTransformOverride {
    public string ObjectId { get;set; }=string.Empty;
    public float[]Position { get;set; }=[0f,0f,0f];
    public float[]Rotation { get;set; }=[0f,0f,0f,1f];
    public float[]Scale { get;set; }=[1f,1f,1f];
}

public sealed class RenderPreviewCameraPass {
    public int X { get;set; }
    public int Y { get;set; }
    public int Width { get;set; }
    public int Height { get;set; }
    public RenderPreviewCamera?Camera { get;set; }
    public string ExcludedObjectId { get;set; }=string.Empty;
}

public sealed class RenderPreviewCameraDiagnostics {
    public int SceneObjectCount { get;set; }
    public int FrustumCandidateCount { get;set; }
    public int DrawCallCount { get;set; }
    public uint ViewProjectionHash { get;set; }
}

public sealed class RenderPreviewOverlayVertex {
    public float[]Position { get;set; }=[];
    public float[]Color { get;set; }=[];
}

public sealed class RenderPreviewObjectTransform {
    public string ObjectId { get;set; }=string.Empty;
    public RenderPreviewMatrix4x4 World { get;set; }=RenderPreviewMatrix4x4.Identity;
    public float SelectionWeight { get;set; }
}

public sealed class RenderPreviewCamera {
    public RenderPreviewMatrix4x4 ViewProjection { get;set; }=RenderPreviewMatrix4x4.Identity;
    public float[]Position { get;set; }=[0f,0f,0f];
    /// <summary>RUFクォータニオン（R、U、F、W）で表すカメラのワールド回転。ランタイムビューでは下記の投影と組み合わせて使う。</summary>
    public float[]Rotation { get;set; }=[0f,0f,0f,1f];
    public float FovYRadians { get;set; }
    public float NearZ { get;set; }
    public float FarZ { get;set; }
    /// <summary>放射状深度カーブの遷移長T（メートル）。</summary>
    public float RadialTransitionMeters { get;set; }=1f;
}

public sealed class RenderPreviewMesh {
    public float[]Positions { get;set; }=[];
    public float[]Normals { get;set; }=[];
    public uint[]Indices { get;set; }=[];
}

public sealed class RenderPreviewMaterial {
    public float[]BaseColor { get;set; }=[1f,1f,1f,1f];
    /// <summary>粗さ（0..1）。従来のプレビューは常に 1。</summary>
    public float Roughness { get;set; }=1f;
    /// <summary>金属度（0..1）。従来のプレビューは常に 0。</summary>
    public float Metallic { get;set; }
    /// <summary>ワールドグリッド（床の格子など）。null なら無し。</summary>
    public RenderPreviewWorldGrid?WorldGrid { get;set; }
}

public sealed class RenderPreviewObject {
    public string ObjectId { get;set; }=string.Empty;
    public int MeshIndex { get;set; }
    public int MaterialIndex { get;set; }
    public RenderPreviewMatrix4x4 World { get;set; }=RenderPreviewMatrix4x4.Identity;
}

public sealed class RenderPreviewMatrix4x4 {
    public float[]Values { get;set; }=[];

    public static RenderPreviewMatrix4x4 Identity=>new(){
        Values=[
            1f,0f,0f,0f,
            0f,1f,0f,0f,
            0f,0f,1f,0f,
            0f,0f,0f,1f,
        ],
    };
}
