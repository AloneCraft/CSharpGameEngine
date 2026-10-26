using CSharpGameEngine.Rendering.Backend;
using System.Numerics;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Core;
using CSharpGameEngine.Rendering;

namespace CSharpGameEngine.Rendering.Server;

/// <summary>送信済みフレーム状態、名簿アーティファクト、デバイス世代の回復処理を所有する。</summary>
public sealed class RenderServer:IDisposable {
    private static readonly StableId RecoveryArtifactNamespace=
        StableId.Parse("ef11e972-e4bd-5127-82a4-7f52175f9d48");
    private readonly IStageRenderBackendFactory Factory;
    // デバイス消失後にレンダラーを再構築するための名簿。Create に渡された初期値から始まり、AddMesh、
    // AddMaterial、AddObject で増える。削除済みオブジェクトもスロットを保持し、Removed として印を付けるため、呼び出し側が保持するすべてのインデックスは変わらない（M0105）。
    private readonly List<StageMeshCreateInfo>Meshes;
    private readonly List<StageMaterialCreateInfo>Materials;
    private readonly List<StageObjectCreateInfo>Objects;
    private bool ObjectRosterArtifactStale;
    private readonly RenderDeviceLifecycle Lifecycle;
    private RenderFrameState PendingFrame;
    private RenderFrameState CompletedFrame;
    private RenderQualityPlan DeclaredQualityPlanValue;
    private IStageShaderArtifact AppliedShaderPackage;
    private IStageRenderBackend?Renderer;
    private ulong FenceBase;
    private ulong LastCompletedFence;
    private int Width;
    private int Height;

    private RenderServer(
        IStageRenderBackendFactory Factory,int Width,int Height,StageMeshCreateInfo[]Meshes,
        StageMaterialCreateInfo[]Materials,StageObjectCreateInfo[]Objects,
        IStageRenderBackend Renderer,AssetArtifactReference[]ArtifactClosure){
        this.Factory=Factory;
        this.Width=Width;
        this.Height=Height;
        this.Meshes=[.. Meshes];
        this.Materials=[.. Materials];
        this.Objects=[.. Objects];
        this.Renderer=Renderer;
        this.AppliedShaderPackage=Renderer.LastKnownGoodShaderArtifact;
        this.Lifecycle=new RenderDeviceLifecycle(ArtifactClosure);
        this.PendingFrame=new RenderFrameState(Objects.Length+StageRenderLimits.DynamicObjectHeadroom);
        this.CompletedFrame=new RenderFrameState(Objects.Length+StageRenderLimits.DynamicObjectHeadroom);
        this.DeclaredQualityPlanValue=RenderQualityPlan.Build(
            checked((uint)Width),checked((uint)Height),
            RenderQualityProfile.Compatibility);
    }

    public RenderDeviceSnapshot DeviceSnapshot=>this.Lifecycle.Snapshot;
    public static PixelColor ClearColor=>StageRenderDefaults.ClearColor;
    public static bool ShaderHotReloadEnabled=>StageRuntimeCapabilities.ShaderHotReloadEnabled;
    public string AdapterDescription=>this.Backend.AdapterDescription;
    public bool IsWarp=>this.Backend.IsWarp;
    public bool DebugLayerEnabled=>this.Backend.DebugLayerEnabled;
    public bool GpuBasedValidationEnabled=>this.Backend.GpuBasedValidationEnabled;
    public IReadOnlyList<RenderDeviceMessage>DebugMessages=>this.Backend.DebugMessages;
    public int ErrorOrWorseMessageCount=>this.Backend.ErrorOrWorseMessageCount;
    public bool ShaderArtifactLoaded=>this.Backend.ShaderArtifactLoaded;
    public string ShaderSemanticHash=>this.Backend.ShaderSemanticHash;
    public IReadOnlyList<string>ShaderArtifactDiagnostics=>this.Backend.ShaderArtifactDiagnostics;
    public uint FramesRendered=>this.Backend.FramesRendered;
    public ulong CompletedFence=>this.LastCompletedFence;
    public int ResidentMaterialTextureCount=>this.Backend.ResidentMaterialTextureCount;
    public int ResidentScreenTextureCount=>this.Backend.ResidentScreenTextureCount;
    public PixelReadbackResult?LastCapturedFrame=>this.Backend.LastCapturedFrame;
    /// <summary>設定済み互換パイプラインに対する、正準形式の L4 宣言。実行結果の正解基準は引き続きバックエンドが持つため、ツールはパスごとの実行証拠ではなく、宣言された計画として表示する。</summary>
    public RenderQualityPlan DeclaredQualityPlan=>this.DeclaredQualityPlanValue;
    public int SurfaceWidth=>this.Width;
    public int SurfaceHeight=>this.Height;
    /// <summary>作成時または追加時にレンダラーが保持するすべてのメッシュ。メッシュのインデックスはサーバーの存続中有効。</summary>
    public int MeshCount=>this.Meshes.Count;
    /// <summary>作成時または追加時にレンダラーが保持するすべてのマテリアル。</summary>
    public int MaterialCount=>this.Materials.Count;
    /// <summary><see cref="AddMaterial"/> がさらに受け付けられるマテリアル数。</summary>
    public int FreeMaterialSlots=>this.Backend.FreeMaterialSlots;
    /// <summary>現在描画中のオブジェクト数（削除済みのものは含めない）。</summary>
    public int ActiveObjectCount=>this.Backend.ActiveObjectCount;
    /// <summary>距離場を持つメッシュと、前フレームのグローバル距離場に含まれていたオブジェクト（ADR-0078/M0110）。</summary>
    public int DistanceFieldMeshCount=>this.Backend.DistanceFieldMeshCount;
    public int DistanceFieldObjectCount=>this.Backend.DistanceFieldObjectCount;
    /// <summary>
    /// 動的ライティングのアダプター側品質（ADR-0078/M0111）。Lumen GI と反射は High または Epic で動作する。既定値はアダプターの値（
    /// <see cref="RenderScalability.DefaultFor"/>）。ここで設定した値はデバイス回復後も保持する。
    /// </summary>
    public RenderScalability Scalability{
        get=>this.ScalabilityValue??this.Backend.Scalability;
        set{
            this.ScalabilityValue=value;
            this.Backend.Scalability=value;
        }
    }
    private RenderScalability?ScalabilityValue;
    /// <summary>デバッグ表示（Unreal の r.Lumen.Visualize、M0111）。Lumen 実行中に、ライティング済みシーンの代わりに表示する。デバイス回復後も保持する。</summary>
    public LumenVisualization LumenVisualization{
        get=>this.LumenVisualizationValue;
        set{
            this.LumenVisualizationValue=value;
            this.Backend.LumenVisualization=value;
        }
    }
    private LumenVisualization LumenVisualizationValue;
    /// <summary>前フレームが、プローブボリュームとスカイライトの代わりに Lumen 相当の GI でシーンを照らしたか（M0111）。</summary>
    public bool LumenActive=>this.Backend.LumenActiveLastFrame;


    private IStageRenderBackend Backend{
        get{
            this.Lifecycle.EnsureCanSubmit();
            return this.Renderer
                ??throw new InvalidOperationException("Ready Render Server has no device generation.");
        }
    }

    public static RenderServer Create(IStageRenderBackendFactory Factory,int Width,int Height,
        IReadOnlyList<StageMeshCreateInfo>Meshes,IReadOnlyList<StageMaterialCreateInfo>Materials,IReadOnlyList<StageObjectCreateInfo>Objects){
        ArgumentNullException.ThrowIfNull(Factory);
        ArgumentNullException.ThrowIfNull(Materials);
        ArgumentNullException.ThrowIfNull(Objects);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Height);
        var OwnedMeshes=CloneMeshes(Meshes);
        StageMaterialCreateInfo[]OwnedMaterials=[.. Materials];
        StageObjectCreateInfo[]OwnedObjects=[.. Objects];
        var Renderer=Factory.Create(Width,Height,OwnedMeshes,OwnedMaterials,OwnedObjects);
        try {
            var Closure=BuildArtifactClosure(Renderer.LastKnownGoodShaderArtifact,OwnedMeshes,OwnedMaterials,OwnedObjects);
            return new RenderServer(Factory,Width,Height,OwnedMeshes,OwnedMaterials,OwnedObjects,Renderer,Closure);
        } catch {
            Renderer.Dispose();
            throw;
        }
    }

    /// <summary>レンダラーにメッシュを1つ追加し（M0105）、そのインデックスを返す。デバイス消失後に再構築する名簿にも追加する。スキニングメッシュはスキニングパスに追加する（M0107）。容量を増やすため、GPU を一度待機させる。</summary>
    public int AddMesh(StageMeshCreateInfo Mesh){
        var Owned=CloneMeshes([Mesh])[0];
        var Index=this.Backend.AddMesh(Owned);
        if(Index!=this.Meshes.Count){
            throw new InvalidOperationException("The renderer and the Render Server disagree on the mesh count.");
        }
        var Closure=new List<AssetArtifactReference>(this.Lifecycle.RequiredArtifacts);
        Closure.Insert(1+Index,CreateArtifact($"mesh/{Index}",Writer=>WriteMesh(Writer,Owned)));
        this.Meshes.Add(Owned);
        this.Lifecycle.ReplaceRequiredArtifacts(Closure);
        return Index;
    }

    /// <summary>マテリアルを1つ追加し（M0105）、そのインデックスを返す。<see cref="FreeMaterialSlots"/> が0の場合は -1 を返す。テクスチャのアップロード時に GPU を一度待機させる。</summary>
    public int AddMaterial(StageMaterialCreateInfo Material){
        var Index=this.Backend.AddMaterial(Material);
        if(Index<0){ return -1; }
        if(Index!=this.Materials.Count){
            throw new InvalidOperationException("The renderer and the Render Server disagree on the material count.");
        }
        var Closure=new List<AssetArtifactReference>(this.Lifecycle.RequiredArtifacts);
        Closure.Insert(1+this.Meshes.Count+Index,CreateArtifact($"material/{Index}",Writer=>WriteMaterial(Writer,Material)));
        this.Materials.Add(Material);
        this.Lifecycle.ReplaceRequiredArtifacts(Closure);
        return Index;
    }

    /// <summary>
    /// 描画するオブジェクトを1つ追加し（M0105）、そのインデックスを返す。レンダラーの余剰容量（<see cref="StageRenderLimits.DynamicObjectHeadroom"/>
    /// ）を使い切った場合は -1 を返す。削除済みオブジェクトのインデックスは再利用する。<see cref="SetObjectTransform"/> で配置されるまでは原点に置く。
    /// </summary>
    public int AddObject(int MeshIndex,int MaterialIndex){
        var Index=this.Backend.AddObject(MeshIndex,MaterialIndex);
        if(Index<0){ return -1; }
        var Info=new StageObjectCreateInfo { MeshIndex=MeshIndex,MaterialIndex=MaterialIndex };
        if(Index==this.Objects.Count){ this.Objects.Add(Info); } else { this.Objects[Index]=Info; }
        this.ObjectRosterArtifactStale=true;
        return Index;
    }

    /// <summary>オブジェクト1つの描画を停止する（M0105）。そのインデックスは <see cref="AddObject"/> で再利用する。</summary>
    public void RemoveObject(int ObjectIndex){
        this.Backend.RemoveObject(ObjectIndex);
        var Info=this.Objects[ObjectIndex];
        if(!Info.Removed){
            this.Objects[ObjectIndex]=new StageObjectCreateInfo { MeshIndex=Info.MeshIndex,MaterialIndex=Info.MaterialIndex,Removed=true };
            this.ObjectRosterArtifactStale=true;
        }
    }

    /// <summary>このフレームの検証済みメインビューを送信し、デバイス回復用に値を保持する。</summary>
    public void SetView(in RenderView View)=>this.SetViewSource(RenderViewSource.Fixed(View));
    /// <summary>物理カメラ入力を解決して送信し、サイズ変更や回復に備えて値を保持する。</summary>
    public void SetViewSource(in RenderViewSource Source){
        var View=Source.Resolve(this.Backend.DirectionalShadows.Compare?Math.Max(1,this.Width/2):this.Width,this.Height);
        this.Backend.SetView(View);
        this.PendingFrame.RecordViewSource(Source);
    }

    public void SetDirectionalLight(in DirectionalLightSubmission Value){
        Value.Validate();
        this.Backend.SetLight(Value);
        this.PendingFrame.RecordSetDirectional(Value);
    }
    public void AddDirectionalLight(in DirectionalLightSubmission Value){
        Value.Validate();
        this.Backend.Submit(Value);
        this.PendingFrame.RecordAddDirectional(Value);
    }
    public void SetAmbientLight(Vector3 Color){
        this.Backend.SetAmbientLight(Color);
        this.PendingFrame.RecordAmbient(Color);
    }
    public void SetFog(FogParams Fog){
        this.Backend.SetFog(Fog);
        this.PendingFrame.RecordFog(Fog);
    }
    /// <summary>シーンの環境設定（ADR-0069）。物理ベースの空とスカイライト、物理フォグ、露出、トーンマッパーを含む。</summary>
    public void SetEnvironmentLighting(in EnvironmentLightingSettings Settings){
        this.Backend.SetEnvironmentLighting(Settings);
        this.PendingFrame.RecordEnvironment(Settings);
        if(!this.PendingFrame.TryReplayView(this.Backend,this.Width,this.Height)){ this.CompletedFrame.TryReplayView(this.Backend,this.Width,this.Height); }
    }
    public VirtualShadowStatistics VirtualShadowStatistics=>this.Backend.VirtualShadowStatistics;
    public DirectionalShadowSettings DirectionalShadows=>this.Backend.DirectionalShadows;
    public float CascadedShadowGpuMilliseconds=>this.Backend.CascadedShadowGpuMilliseconds;
    public float VirtualShadowGpuMilliseconds=>this.Backend.VirtualShadowGpuMilliseconds;
    /// <summary>大気と高さフォグで使う、ワールド原点からのカメラの絶対高度（メートル）。</summary>
    public void SetCameraAltitude(float Meters){
        this.Backend.SetCameraAltitude(Meters);
        this.PendingFrame.RecordCameraAltitude(Meters);
    }
    /// <summary>簡易高さフォグで使う、ワールド原点からのレンダリング座標原点の高度。ワールド座標では0。</summary>
    public void SetRenderOriginAltitude(float Meters){
        this.Backend.SetRenderOriginAltitude(Meters);
        this.PendingFrame.RecordRenderOriginAltitude(Meters);
    }
    /// <summary>レンダリング座標原点をグリッドマテリアルの区画サイズで剰余に変換した値。ワールド空間のグリッドをワールド格子に一致させる。</summary>
    public void SetWorldGridAnchor(int MaterialIndex,Vector3 Anchor){
        this.Backend.SetWorldGridAnchor(MaterialIndex,Anchor);
        this.PendingFrame.RecordWorldGridAnchor(MaterialIndex,Anchor);
    }

    public void AddPointLight(in PointLightSubmission Value){
        Value.Validate();
        this.Backend.Submit(Value);
        this.PendingFrame.RecordPoint(Value);
    }
    public void AddSpotLight(in SpotLightSubmission Value){
        Value.Validate();
        this.Backend.Submit(Value);
        this.PendingFrame.RecordSpot(Value);
    }
    public void AddRectAreaLight(in RectAreaLightSubmission Value){
        Value.Validate();
        this.Backend.Submit(Value);
        this.PendingFrame.RecordArea(Value);
    }
    public void SubmitDirectLighting(
        IReadOnlyList<EmissivePatch>Patches,float RangeMeters,
        bool CastsShadows,int ShadowPriority,float ShadowBiasMeters){
        this.Backend.SubmitDirectLighting(
            Patches,RangeMeters,CastsShadows,ShadowPriority,ShadowBiasMeters);
        this.PendingFrame.RecordEmissive(
            Patches,RangeMeters,CastsShadows,ShadowPriority,ShadowBiasMeters);
    }

    public void SetObjectTransform(int ObjectIndex,Matrix4x4 World){
        this.Backend.SetObjectTransform(ObjectIndex,World);
        this.PendingFrame.RecordObjectTransform(ObjectIndex,World);
    }

    /// <summary>スキニングメッシュのパレットを設定する（ADR-0066）。デバイス回復時に現在の姿勢を再適用できるよう、値を記録する。</summary>
    public void SetSkinPalette(int MeshIndex,ReadOnlySpan<Matrix4x4>Palette){
        this.Backend.SetSkinPalette(MeshIndex,Palette);
        this.PendingFrame.RecordSkinPalette(MeshIndex,Palette);
    }

    public void SetPresentationMode(RenderPresentationMode Mode){
        if(!Enum.IsDefined(Mode)){ throw new ArgumentOutOfRangeException(nameof(Mode)); }
        this.Backend.SetPresentationMode(Mode);
        this.PendingFrame.RecordPresentationMode(Mode);
    }

    /// <summary>
    /// オブジェクトをどこに描画するかを選択する（M0107）。カメラ視点では描画しない（UE の OwnerNoSee）、シャドウマップには描画しない（CastShadow オフ）、またはその両方を設定でき
    /// る。設定するゲームがデバイス回復後も保持できるよう、フレームとともに記録する。
    /// </summary>
    public void SetObjectVisibility(int ObjectIndex,bool VisibleInView,bool CastsShadow){
        this.Backend.SetObjectVisibility(ObjectIndex,VisibleInView,CastsShadow);
        this.PendingFrame.RecordObjectVisibility(ObjectIndex,VisibleInView,CastsShadow);
    }

    /// <summary>
    /// オブジェクトのマテリアル発光に <paramref name="Scale"/> を乗算する（ADR-0078/M0109。車両のブレーキランプなど、ゲーム実行中に変更するマテリアルスカラー）。1なら
    /// マテリアル本来の発光量。デバイス回復後も維持できるよう、フレームとともに記録する。
    /// </summary>
    public void SetObjectEmissiveScale(int ObjectIndex,float Scale){
        this.Backend.SetObjectEmissiveScale(ObjectIndex,Scale);
        this.PendingFrame.RecordObjectEmissiveScale(ObjectIndex,Scale);
    }

    public void SetObjectSelectionWeight(int ObjectIndex,float Weight){
        if(!float.IsFinite(Weight)||Weight<0f||Weight>1f){
            throw new ArgumentOutOfRangeException(
                nameof(Weight),"Selection weight must be finite and between zero and one.");
        }
        this.Backend.SetObjectSelectionWeight(ObjectIndex,Weight);
        this.PendingFrame.RecordObjectSelectionWeight(ObjectIndex,Weight);
    }

    public void SetOverlay(
        ReadOnlySpan<RenderOverlayVertex>Triangles,
        ReadOnlySpan<RenderOverlayVertex>Lines){
        this.Backend.SetOverlay(Triangles,Lines);
        this.PendingFrame.RecordOverlay(Triangles,Lines);
    }

    /// <summary>完成フレームの上に重ねるゲーム UI（ADR-0078/M0104）。毎フレーム設定する。設定のないフレームには何も描画しない。</summary>
    public void SetScreenLayer(ReadOnlySpan<RenderScreenVertex>Vertices,ReadOnlySpan<RenderScreenBatch>Batches){
        this.Backend.SetScreenLayer(Vertices,Batches);
        this.PendingFrame.RecordScreenLayer(Vertices,Batches);
    }

    public void SetCameraPreviews(ReadOnlySpan<RenderCameraPreview>Previews){
        this.Backend.SetCameraPreviews(Previews);
        this.PendingFrame.RecordCameraPreviews(Previews);
    }

    public RenderCameraPreviewDiagnostics[]GetCameraPreviewDiagnostics()=>
        this.Backend.GetCameraPreviewDiagnostics();

    public void Resize(int Width,int Height){
        ObjectDisposedException.ThrowIf(
            this.DeviceSnapshot.State==RenderDeviceState.Disposed,this);
        if(Width<=0||Height<=0){
            if(this.DeviceSnapshot.State==RenderDeviceState.Ready){
                IStageRenderBackend Renderer=this.Backend;
                Renderer.Resize(Width,Height);
                this.Lifecycle.Suspend(this.ObserveCompletedFence(Renderer));
            }
            return;
        }

        RenderQualityPlan ReplacementPlan=RenderQualityPlan.Build(
            checked((uint)Width),checked((uint)Height),
            this.DeclaredQualityPlanValue.Profile);
        this.Width=Width;
        this.Height=Height;
        this.DeclaredQualityPlanValue=ReplacementPlan;
        if(this.DeviceSnapshot.State==RenderDeviceState.Suspended){
            this.Recover(RenderRecoveryReason.Resume);
            return;
        }

        try {
            this.Backend.Resize(Width,Height);
        } catch(RenderBackendFailureException Exception){
            this.HandleBackendFailure(Exception,CaptureForVerification:false);
        }
    }

    public void RenderFrame(bool CaptureForVerification=false){
        try {
            IStageRenderBackend Renderer=this.Backend;
            if(this.PendingFrame.IsEmpty&&!this.CompletedFrame.IsEmpty){
                this.CompletedFrame.Replay(Renderer,this.Width,this.Height);
            }
            Renderer.RenderFrame(CaptureForVerification);
            _=this.ObserveCompletedFence(Renderer);
            this.RefreshObjectRosterArtifact();
            this.SynchronizeArtifactClosure(Renderer);
            this.CommitPendingFrame();
        } catch(RenderBackendFailureException Exception){
            this.HandleBackendFailure(Exception,CaptureForVerification);
        }
    }

    public void Dispose(){
        if(this.DeviceSnapshot.State==RenderDeviceState.Disposed){ return; }
        try {
            this.Renderer?.Dispose();
        } finally {
            this.Renderer=null;
            this.Lifecycle.Dispose();
        }
    }

    private void HandleBackendFailure(
        RenderBackendFailureException Exception,bool CaptureForVerification){
        if(Exception.RecoveryReason is { }RecoveryReason){
            this.Recover(RecoveryReason,CaptureForVerification);
            return;
        }

        this.Lifecycle.Fault(new RenderDeviceFailure(
            RenderDeviceFailureKind.NativeFailure,Exception.HResult,Exception.Message));
        this.Lifecycle.EnsureCanSubmit();
    }

    private void Recover(
        RenderRecoveryReason Reason,bool CaptureForVerification=false){
        IStageRenderBackend Prior=this.Renderer
            ??throw new InvalidOperationException("No render-device generation is available.");
        this.SynchronizeArtifactClosure(Prior);
        IStageShaderArtifact ShaderPackage=Prior.LastKnownGoodShaderArtifact;
        ulong CompletedFence=this.ObserveCompletedFence(Prior);
        this.Lifecycle.BeginRecovery(Reason,CompletedFence);
        this.Renderer=null;

        try {
            Prior.Dispose();
            var Replacement=this.Factory.Create(this.Width,this.Height,this.Meshes,this.Materials,this.Objects,ShaderPackage);
            this.Renderer=Replacement;
            if(this.ScalabilityValue is{ }Scalability){
                Replacement.Scalability=Scalability;
            }
            Replacement.LumenVisualization=this.LumenVisualizationValue;
            this.AppliedShaderPackage=ShaderPackage;
            this.FenceBase=CompletedFence;
            RenderFrameState RecoveryFrame=
                !this.PendingFrame.IsEmpty?this.PendingFrame:this.CompletedFrame;
            bool CompletesPendingFrame=!this.PendingFrame.IsEmpty;
            if(!RecoveryFrame.IsEmpty){
                RecoveryFrame.Replay(Replacement,this.Width,this.Height);
                Replacement.RenderFrame(CaptureForVerification);
            }
            this.Lifecycle.CompleteRecovery(
                this.Lifecycle.RequiredArtifacts,
                this.ObserveCompletedFence(Replacement));
            if(CompletesPendingFrame){ this.CommitPendingFrame(); }
        } catch(Exception Exception){
            this.Renderer?.Dispose();
            this.Renderer=null;
            this.Lifecycle.Fault(new RenderDeviceFailure(
                RenderDeviceFailureKind.ArtifactRebuildFailed,
                Exception.HResult,Exception.Message));
            this.Lifecycle.EnsureCanSubmit();
            throw;
        }
    }

    private ulong ObserveCompletedFence(IStageRenderBackend Renderer){
        ulong NativeCompletedFence=Renderer.CompletedFence;
        if(NativeCompletedFence==ulong.MaxValue){
            return this.LastCompletedFence;
        }
        ulong CompletedFence=checked(this.FenceBase+NativeCompletedFence);
        if(CompletedFence>this.LastCompletedFence){
            this.LastCompletedFence=CompletedFence;
        }
        return this.LastCompletedFence;
    }

    private void CommitPendingFrame(){
        if(this.PendingFrame.IsEmpty){ return; }
        this.PendingFrame.InheritPersistentSettings(this.CompletedFrame);
        RenderFrameState PreviousCompleted=this.CompletedFrame;
        this.CompletedFrame=this.PendingFrame;
        this.PendingFrame=PreviousCompleted;
        this.PendingFrame.Reset();
    }

    /// <summary>オブジェクト名簿の新しい識別情報を、追加・削除のたびではなく各フレームにつき1回公開する。</summary>
    private void RefreshObjectRosterArtifact(){
        if(!this.ObjectRosterArtifactStale){ return; }
        this.ObjectRosterArtifactStale=false;
        var Closure=new List<AssetArtifactReference>(this.Lifecycle.RequiredArtifacts);
        Closure[^1]=CreateObjectRosterArtifact(this.Objects);
        this.Lifecycle.ReplaceRequiredArtifacts(Closure);
    }

    private void SynchronizeArtifactClosure(IStageRenderBackend Renderer){
        IStageShaderArtifact Package=Renderer.LastKnownGoodShaderArtifact;
        if(ReferenceEquals(Package,this.AppliedShaderPackage)){ return; }
        AssetArtifactReference[]Closure=[.. this.Lifecycle.RequiredArtifacts];
        Closure[0]=CreateShaderArtifact(Package);
        this.Lifecycle.ReplaceRequiredArtifacts(Closure);
        this.AppliedShaderPackage=Package;
    }

    private static StageMeshCreateInfo[]CloneMeshes(
        IReadOnlyList<StageMeshCreateInfo>Meshes){
        ArgumentNullException.ThrowIfNull(Meshes);
        StageMeshCreateInfo[]Result=new StageMeshCreateInfo[Meshes.Count];
        for(int Index=0;Index<Meshes.Count;Index++){
            StageMeshCreateInfo Mesh=Meshes[Index];
            Result[Index]=new StageMeshCreateInfo {
                Positions=[.. Mesh.Positions],
                Normals=[.. Mesh.Normals],
                Tangents=[.. Mesh.Tangents],
                Uvs=[.. Mesh.Uvs],
                Indices=[.. Mesh.Indices],
                Skin=Mesh.Skin is{ }Skin?new StageSkinCreateInfo { JointCount=Skin.JointCount,JointIndices=[.. Skin.JointIndices],Weights=[.. Skin.Weights] }:null,
                // ADR-0078/M0110：距離場は不変なのでコピー後も共有する（デバイス回復時は、この距離場からグローバル距離場を再構築する）。
                DistanceField=Mesh.DistanceField,
            };
        }
        return Result;
    }

    private static AssetArtifactReference[]BuildArtifactClosure(
        IStageShaderArtifact ShaderPackage,
        IReadOnlyList<StageMeshCreateInfo>Meshes,
        IReadOnlyList<StageMaterialCreateInfo>Materials,
        IReadOnlyList<StageObjectCreateInfo>Objects){
        List<AssetArtifactReference>Result=[
            CreateShaderArtifact(ShaderPackage),
        ];
        for(int Index=0;Index<Meshes.Count;Index++){
            StageMeshCreateInfo Mesh=Meshes[Index];
            Result.Add(CreateArtifact($"mesh/{Index}",Writer=>WriteMesh(Writer,Mesh)));
        }
        for(int Index=0;Index<Materials.Count;Index++){
            StageMaterialCreateInfo Material=Materials[Index];
            Result.Add(CreateArtifact(
                $"material/{Index}",Writer=>WriteMaterial(Writer,Material)));
        }
        Result.Add(CreateObjectRosterArtifact(Objects));
        return[.. Result];
    }

    private static AssetArtifactReference CreateObjectRosterArtifact(IReadOnlyList<StageObjectCreateInfo>Objects)=>
        CreateArtifact("object-roster",Writer=>{
            Writer.Write(Objects.Count);
            foreach(StageObjectCreateInfo Object in Objects){
                Writer.Write(Object.MeshIndex);
                Writer.Write(Object.MaterialIndex);
                Writer.Write(Object.Removed);
            }
        });

    private static AssetArtifactReference CreateShaderArtifact(IStageShaderArtifact ShaderPackage)=>
        CreateArtifact("shader/stage",Writer=>Writer.Write(ShaderPackage.Serialized.Span));

    private static AssetArtifactReference CreateArtifact(
        string Name,Action<BinaryWriter>Write){
        using MemoryStream Bytes=new();
        using(BinaryWriter Writer=new(Bytes,System.Text.Encoding.UTF8,leaveOpen:true)){
            Write(Writer);
        }
        AssetId AssetId=new(StableId.FromName(RecoveryArtifactNamespace,Name));
        return new AssetArtifactReference(
            new AssetKey(AssetId,null),Hash256.Compute(Bytes.ToArray()));
    }

    private static void WriteMesh(BinaryWriter Writer,in StageMeshCreateInfo Mesh){
        Writer.Write(Mesh.Positions.Count);
        foreach(Vector3 Value in Mesh.Positions){
            Writer.Write(Value.X); Writer.Write(Value.Y); Writer.Write(Value.Z);
        }
        foreach(Vector3 Value in Mesh.Normals){
            Writer.Write(Value.X); Writer.Write(Value.Y); Writer.Write(Value.Z);
        }
        foreach(Vector4 Value in Mesh.Tangents){
            Writer.Write(Value.X); Writer.Write(Value.Y);
            Writer.Write(Value.Z); Writer.Write(Value.W);
        }
        foreach(Vector2 Value in Mesh.Uvs){
            Writer.Write(Value.X); Writer.Write(Value.Y);
        }
        Writer.Write(Mesh.Indices.Count);
        foreach(uint Value in Mesh.Indices){ Writer.Write(Value); }
        Writer.Write(Mesh.Skin?.JointCount??0);
        if(Mesh.Skin is{ }Skin){
            foreach(ushort Value in Skin.JointIndices){ Writer.Write(Value); }
            foreach(float Value in Skin.Weights){ Writer.Write(Value); }
        }
        // M0110：距離場もレンダラーに渡されたデータの一部。距離場のないメッシュは以前の識別情報を維持する。
        if(Mesh.DistanceField is{ }Field){
            Writer.Write(Field.MeshHash.ToByteArray());
            Writer.Write(Field.ResolutionX); Writer.Write(Field.ResolutionY); Writer.Write(Field.ResolutionZ);
            Writer.Write(Field.BoundsMin.X); Writer.Write(Field.BoundsMin.Y); Writer.Write(Field.BoundsMin.Z);
            Writer.Write(Field.VoxelSize);
            foreach(Half Value in Field.Distances){ Writer.Write(Value); }
        }
    }

    private static void WriteMaterial(
        BinaryWriter Writer,in StageMaterialCreateInfo Material){
        Writer.Write(Material.BaseColorFactor.X);
        Writer.Write(Material.BaseColorFactor.Y);
        Writer.Write(Material.BaseColorFactor.Z);
        Writer.Write(Material.BaseColorFactor.W);
        Writer.Write(Material.RoughnessFactor);
        Writer.Write(Material.MetallicFactor);
        Writer.Write(Material.EmissiveFactor.X);
        Writer.Write(Material.EmissiveFactor.Y);
        Writer.Write(Material.EmissiveFactor.Z);
        Writer.Write(Material.EmissiveNits.X);
        Writer.Write(Material.EmissiveNits.Y);
        Writer.Write(Material.EmissiveNits.Z);
        Writer.Write(Material.EmissiveTextureLuminanceScale);
        WriteTexture(Writer,Material.BaseColorTexture);
        WriteTexture(Writer,Material.NormalTexture);
        WriteTexture(Writer,Material.EmissiveTexture);
        WriteTexture(Writer,Material.MetallicRoughnessTexture);
        Writer.Write(Material.WorldGrid is not null);
        if(Material.WorldGrid is{ }Grid){
            Writer.Write(Grid.GridSizeMeters);
            Writer.Write(Grid.SubGridDivisions);
            Writer.Write(Grid.WireHalfWidth);
            Writer.Write(Grid.GridColor.X); Writer.Write(Grid.GridColor.Y); Writer.Write(Grid.GridColor.Z);
            Writer.Write(Grid.SubGridColor.X); Writer.Write(Grid.SubGridColor.Y); Writer.Write(Grid.SubGridColor.Z);
        }
    }

    private static void WriteTexture(
        BinaryWriter Writer,RenderTextureResource?Texture){
        Writer.Write(Texture is not null);
        if(Texture is null){ return; }
        Writer.Write(Texture.Width);
        Writer.Write(Texture.Height);
        Writer.Write(Texture.ContentHash.ToString());
    }

    /// <summary>
    /// インポートまたはプロシージャル生成されたメッシュの正準かつ不変な名簿項目を作る。呼び出し側でレンダラー固有のオブジェクトを作らない。ジオメトリ、マテリアル、描画 ID は、作成済みワールドと独立
    /// プレビューが使うものと同じ Render Server 境界に登録する。
    /// </summary>
    public static (StageMeshCreateInfo[]Meshes,StageMaterialCreateInfo[]Materials,StageObjectCreateInfo[]Objects)BuildSingleMeshRoster(
        IReadOnlyList<Vector3>Positions,IReadOnlyList<Vector3>Normals,
        IReadOnlyList<Vector2>Uvs,IReadOnlyList<uint>Indices,
        in StageMaterialCreateInfo Material){
        ArgumentNullException.ThrowIfNull(Positions);
        ArgumentNullException.ThrowIfNull(Normals);
        ArgumentNullException.ThrowIfNull(Uvs);
        ArgumentNullException.ThrowIfNull(Indices);
        if(Positions.Count==0||Positions.Count!=Normals.Count
            ||Positions.Count!=Uvs.Count||Indices.Count==0){
            throw new ArgumentException(
                "Mesh must have positions, normals, and UVs for every vertex and at least one index.",
                nameof(Positions));
        }

        var Tangents=new Vector4[Positions.Count];
        for(int Index=0;Index<Tangents.Length;Index++){
            Tangents[Index]=StageMeshGeometry.ArbitraryTangentFor(Normals[Index]);
        }
        return(
            [new StageMeshCreateInfo {
                Positions=Positions,Normals=Normals,Tangents=Tangents,Uvs=Uvs,
                Indices=Indices,
            }],
            [Material],
            [new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 }]);
    }


}
