using CSharpGameEngine.Rendering.View;
using CSharpGameEngine.Rendering.Server;
using CSharpGameEngine.Rendering.Pipeline;
using CSharpGameEngine.Runtime.Lighting;
using CSharpGameEngine.Lighting;
using CSharpGameEngine.Rendering;
using System.Numerics;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Platform.Windows;
using RenderCamera=CSharpGameEngine.Rendering.View.RenderCamera;
using Camera=CSharpGameEngine.Runtime.Windows.Camera;
using EcsWorld=CSharpGameEngine.Entities.World;

namespace CSharpGameEngine.Runtime.Windows;

/// <summary>Transformの位置とローカル+Zを、ライト用のScale非依存なワールド入力へ写像する。</summary>
internal static class LightTransform {
    internal static FixedTransformStatus TryGetRectanglePose(Entity Entity,EcsWorld World,EngineHost Host,
        out Vector3 Position,out Vector3 Forward,out Vector3 Up,out FixedTransformResolveStatus ResolveStatus){
        var Status=RuntimeWorldTransform.TryGetCameraRelativeMatrix(Entity,World,Host,out var Matrix,out ResolveStatus);
        Position=default;Forward=default;Up=default;
        if(Status!=FixedTransformStatus.Success){return Status;}
        var Z=Vector3.TransformNormal(Vector3.UnitZ,Matrix);
        var Y=Vector3.TransformNormal(Vector3.UnitY,Matrix);
        if(!float.IsFinite(Z.LengthSquared())||!float.IsFinite(Y.LengthSquared())||Z.LengthSquared()<=1e-12f||Y.LengthSquared()<=1e-12f){return FixedTransformStatus.NonInvertible;}
        Forward=Vector3.Normalize(Z);
        var Right=Vector3.Cross(Vector3.Normalize(Y),Forward);
        if(Right.LengthSquared()<=1e-12f){return FixedTransformStatus.NonInvertible;}
        Up=Vector3.Normalize(Vector3.Cross(Forward,Vector3.Normalize(Right)));
        Position=Matrix.Translation;
        return FixedTransformStatus.Success;
    }
    public static FixedTransformStatus TryGetCameraRelativePose(
        Entity Entity,EcsWorld EcsWorld,EngineHost Host,
        out Vector3 Position,
        out Vector3 Forward,
        out FixedTransformResolveStatus ResolveStatus)=>
        RuntimeWorldTransform.TryGetCameraRelativePose(
            Entity,EcsWorld,Host,out Position,out Forward,out ResolveStatus);
}

/// <summary>固定小数点階層のローカル+Z／+Yカメラ基底を、最終レンダラー境界で適用する。</summary>
internal static class CameraTransform {
    public static FixedTransformStatus TryApplyWorldOrientation(
        in FixedAffineTransform CameraWorld,
        ref RenderCamera Camera,
        out bool UsesTransformOrientation){
        FixedTransformStatus Status=CameraWorld.TryGetCameraBasis(
            out Vector3 Forward,out Vector3 Up);
        if(Status!=FixedTransformStatus.Success){
            UsesTransformOrientation=false;
            return Status;
        }

        UsesTransformOrientation=!RenderCamera.IsIdentityWorldOrientation(Forward,Up);
        if(!UsesTransformOrientation||Camera.TrySetWorldOrientation(Forward,Up)){
            return FixedTransformStatus.Success;
        }

        return FixedTransformStatus.NonInvertible;
    }

}

[SystemPhase(SystemPhase.FrameInput)]
internal struct Window:ISystem {
    private Entity HostEntity;
    private bool HasHostEntity;

    public void Update(Entity Entity,ref AppHost App,ref AppHostRuntime Runtime,EngineHost Host,ref State State){
        if(!this.HasHostEntity){this.HostEntity=Entity;this.HasHostEntity=true;}
        else if(!this.HostEntity.Equals(Entity)){
            throw new InvalidOperationException("Runtime requires exactly one AppHost.");
        }
        if(Host.ExternalSurface is not null){
            // ADR-0070 B1：サーフェスとその入力はエディターが所有する。
            return;
        }
        if(Host.Window is null){
            Host.EnsureWindow(App);
            return;
        }
        Win32Window Window=Host.Window;
        if(App.CaptureMouse&&Host.Automation is null&&!Window.IsMouseCaptured){
            Window.SetMouseCaptured(true);
        }
        Window.PumpEvents();
        Host.Automation?.ApplyInput(Window);
        Host.Input.ProcessEvents(Window.DrainInputEvents());
        if(Window.IsClosed){
            State.Enabled=false;
            return;
        }
    }
}

internal static class LightValidation {
    internal static int Count(Entity Entity,EcsWorld World)=>
        (World.TryGetComponent(Entity,out DirectionalLight _)?1:0)
        +(World.TryGetComponent(Entity,out PointLight _)?1:0)
        +(World.TryGetComponent(Entity,out SpotLight _)?1:0)
        +(World.TryGetComponent(Entity,out RectAreaLight _)?1:0);
    internal static string? SceneFailure(Entity Entity,EcsWorld World){
        if(Count(Entity,World)!=1){return "LightMultiplicity";}
        if(!World.TryGetComponent(Entity,out Transform _)){return "Transform";}
        if(World.TryGetComponent(Entity,out LightShadow Shadow)&&!Shadow.Bias.IsValid){return "LightShadow.Bias";}
        return null;
    }
    /// <summary>ノードの<see cref="LightVisibility"/>によってライトが非表示になっている。通常どおり検査するが、描画には送信しない。</summary>
    internal static bool Hidden(Entity Entity,EcsWorld World)=>World.TryGetComponent(Entity,out LightVisibility Visibility)&&!Visibility.Visible;
    internal static LightShadowSettings? Shadow(Entity Entity,EcsWorld World)=>
        World.TryGetComponent(Entity,out LightShadow Value)?new LightShadowSettings(Value.Priority,Value.Bias):null;
}
// ADR-0073：[SystemOrder]の下でPresentationシステムが持っていた順序を、1つのチェーンとして維持する。
// OrbitCameraUpdate、ここで宣言されたシステムの順、アニメーションシステム、SkinnedMeshSync、
// audio、Presentの順にする。データ依存のないシステム間の辺は、解析後に緩和できる可能性がある。
[SystemPhase(SystemPhase.Presentation)]
[RunsAfter(typeof(CSharpGameEngine.Runtime.Character.OrbitCameraUpdate))]
internal struct LightShadowValidation:ISystem {
    public void Update(Entity Entity,ref LightShadow Shadow,ref LightShadowRuntime Runtime,ref State State){
        Runtime.InvalidProperty=LightValidation.SceneFailure(Entity,State.World);
    }
}

/// <summary>
/// Scene DSLが生成した個別メッシュと環境Componentを一巡収集してから、次フレームで
/// 固定ロースター型StageRendererを構築する。Scene由来の子Archetypeは実System由来の
/// {AppHost,Level}より後ろへ登録されるため、同一フレームで確定してはならない。
/// </summary>
[SystemPhase(SystemPhase.Presentation)]
[RunsAfter(typeof(LightShadowValidation))]
internal struct LevelLoad:ISystem {
    private Entity LevelEntity;
    private bool HasLevelEntity;
    private List<(Entity Entity,Transform Transform,StaticMesh Mesh)>?PendingStaticMeshes;
    private List<(Entity Entity,Transform Transform,PrimitiveMesh Mesh)>?PendingPrimitives;
    private List<(Entity Entity,Transform Transform,SkeletalMesh Mesh)>?PendingSkeletalMeshes;
    private Entity PendingCameraEntity;
    private Camera PendingCamera;
    private int ActiveCameraCount;
    private bool RosterCaptured;
    // ADR-0078/M0107：カメラの所有Pawnが破棄されて新しいPawnが生成されたとき、新しいPawnのカメラへ切り替える。
    private Entity ReplacementCameraEntity;
    private bool HasReplacementCamera;

    public void Update(Entity Entity,ref Transform Transform,ref StaticMesh Mesh,ref State State){
        if(!this.RosterCaptured)(this.PendingStaticMeshes??=[]).Add((Entity,Transform,Mesh));
    }

    /// <summary>
    /// ビューで使う投影設定。Scene Designerがエディターカメラを指定した場合（ADR-0070 B1）は、その視野角とクリップ範囲を使い、
    /// それ以外はシーンカメラの値を使う。露出と深度マッピングはエディター内でもシーンカメラの設定を使い、ゲームと同じ見た目にする。
    /// 放射状ビューは追加コストなしで任意の範囲を表示できるため、シーンカメラが見る範囲を少なくとも含める
    ///（M0099：エディターのフォーカス基準範囲だけでは、放射状深度を使う目的に反する）。エディターでは、デザイナーが構図に収めた範囲も含める（M0100）。
    /// </summary>
    private static void ApplyProjection(ref RenderCamera View,EditorCameraPose?Editor,bool HasSceneCamera,in Camera SceneCamera){
        if(Editor is { }EditorCamera){
            View.FovYRadians=EditorCamera.FovYRadians;
            View.NearZ=EditorCamera.NearZ;
            View.FarZ=EditorCamera.FarZ;
        } else if(HasSceneCamera){
            View.FovYRadians=SceneCamera.FovYRadians;
            View.NearZ=SceneCamera.NearZ;
            View.FarZ=SceneCamera.FarZ;
        }
        if(!HasSceneCamera){
            return;
        }
        View.ExposureEv100=SceneCamera.ExposureEv100;
        View.RadialTransitionMeters=SceneCamera.RadialTransitionMeters;
        View.NearZ=Editor is { }RadialEditor?MathF.Min(RadialEditor.NearZ,SceneCamera.NearZ):SceneCamera.NearZ;
        View.FarZ=Editor is { }WideEditor?MathF.Max(WideEditor.FarZ,SceneCamera.FarZ):SceneCamera.FarZ;
    }

    /// <summary>
    /// <paramref name="Renderer"/> が結果を使用できない場合を除き、<paramref name="View"/> に投影（
    /// <see cref="ApplyProjection"/>）を適用する。使用できない場合は <paramref name="View"/> の現在の投影を維持する。使用不能なメンバーのうち最初のものを
    /// 返す。すべて使用可能なら null。
    /// </summary>
    private static string?ProjectIfUsable(ref RenderCamera View,EditorCameraPose?Editor,bool HasSceneCamera,in Camera SceneCamera,RenderServer?Renderer){
        var Projected=View;
        ApplyProjection(ref Projected,Editor,HasSceneCamera,SceneCamera);
        var Invalid=Renderer is null?null:InvalidProjectionProperty(Projected,Renderer.SurfaceWidth/(float)Renderer.SurfaceHeight);
        if(Invalid is null){
            View=Projected;
        }
        return Invalid;
    }

    /// <summary>レンダラーが使用できない <paramref name="View"/> の投影メンバーのうち最初のもの。すべて使用可能なら null。</summary>
    private static string?InvalidProjectionProperty(in RenderCamera View,float AspectRatio){
        if(!(View.FovYRadians>0f&&View.FovYRadians<MathF.PI)){ return nameof(Camera.FovYRadians); }
        if(!(float.IsFinite(View.NearZ)&&View.NearZ>0f)){ return nameof(Camera.NearZ); }
        if(!(float.IsFinite(View.FarZ)&&View.FarZ>View.NearZ)){ return nameof(Camera.FarZ); }
        if(!float.IsFinite(View.ExposureEv100)){ return nameof(Camera.ExposureEv100); }
        if(!RadialDepthCurve.TryCreate(View.RadialTransitionMeters,out var Curve)){ return nameof(Camera.RadialTransitionMeters); }
        if(!RadialDepthProjection.TryCreate(Curve,View.NearZ,View.FarZ,View.FovYRadians,AspectRatio,out _)){ return nameof(Camera.FarZ); }
        return null;
    }

    public void Update(Entity Entity,ref Transform Transform,ref PrimitiveMesh Mesh,ref State State){
        if(!this.RosterCaptured)(this.PendingPrimitives??=[]).Add((Entity,Transform,Mesh));
    }

    public void Update(Entity Entity,ref Transform Transform,ref SkeletalMesh Mesh,ref State State){
        if(!this.RosterCaptured)(this.PendingSkeletalMeshes??=[]).Add((Entity,Transform,Mesh));
    }

    public void Update(Entity Entity,ref Transform Transform,ref Camera Camera,ref State State){
        if(!this.RosterCaptured&&Camera.Active){
            this.ActiveCameraCount++;
            if(this.ActiveCameraCount==1){
                this.PendingCameraEntity=Entity;
                this.PendingCamera=Camera;
            }
        }
        else if(this.RosterCaptured&&Camera.Active&&Entity!=this.PendingCameraEntity){
            this.ReplacementCameraEntity=Entity;
            this.HasReplacementCamera=true;
        }
    }
    public void Update(Entity Entity,ref AppHost App,ref AppHostRuntime AppRuntime,ref Level Level,ref LevelRuntime LevelRuntime,EngineHost Host,ref State State){
        if(!this.HasLevelEntity){this.LevelEntity=Entity;this.HasLevelEntity=true;}
        else if(!this.LevelEntity.Equals(Entity)){
            throw new InvalidOperationException("Runtime requires exactly one Level.");
        }
        if(LevelRuntime.Loaded){
            // 描画に使うカメラは、破棄時（M0107：再生成されたポーンのカメラに切り替わる）や、ゲームがカメラを無効にして
            // 別のカメラを有効にしたときに切り替わる（ADR-0078/M0109：車両の前後カメラ、UE のカメラコンポーネントの Activate / Deactivate）。
            if(this.HasReplacementCamera&&(!State.World.IsAlive(this.PendingCameraEntity)
                ||!State.World.TryGetComponent(this.PendingCameraEntity,out Camera Current)||!Current.Active)){
                this.PendingCameraEntity=this.ReplacementCameraEntity;
                this.ActiveCameraCount=1;
            }
            this.HasReplacementCamera=false;
            FixedTransformResolveStatus CameraResolveStatus=ResolveCamera(
                Host,this.PendingCameraEntity,State.World,out FixedAffineTransform CameraWorld,out var CameraFrame);
            if(CameraResolveStatus==FixedTransformResolveStatus.Success){ ApplyAutomationCamera(Host,this.PendingCameraEntity,State.World,ref CameraWorld); }
            LevelRuntime.LastCameraWorldResolveStatus=CameraResolveStatus;
            if(CameraResolveStatus==FixedTransformResolveStatus.Success){
                RenderCamera ActiveRenderCamera=Host.RenderCamera;
                FixedTransformStatus CameraStatus=CameraTransform.TryApplyWorldOrientation(
                    CameraWorld,ref ActiveRenderCamera,out _);
                LevelRuntime.LastCameraTransformStatus=CameraStatus;
                // 値を変更すると（Scene Designer またはゲームによる変更）、シーンカメラのコンポーネントが置き換わるため、
                // 投影を毎フレーム再取得する。ライトと同様に、使用不能な値なら最後に有効だった値を維持する。
                Camera SceneCamera=default;
                var HasSceneCamera=this.ActiveCameraCount>0&&State.World.TryGetComponent(this.PendingCameraEntity,out SceneCamera);
                LevelRuntime.InvalidCameraProperty=ProjectIfUsable(ref ActiveRenderCamera,Host.EditorCamera,HasSceneCamera,SceneCamera,Host.Renderer);
                if(CameraStatus==FixedTransformStatus.Success){
                    // プレゼンテーション処理はシステム単位で実行される。後続の Renderable / ライトの
                    // アーキタイプを実行する前に更新し、このフレーム内のすべての GPU 送信で同じカメラ原点を使う。
                    Host.RenderCamera=ActiveRenderCamera;
                    Host.SetCameraPose(CameraWorld,CameraFrame);
                    LevelRuntime.HasLastCameraWorldPose=true;
                }
            } else {
                LevelRuntime.LastCameraTransformStatus=RuntimeWorldTransform.ToTransformStatus(
                    CameraResolveStatus);
            }
            Host.Renderer?.SetCamera(Host.RenderCamera);
            return;
        }
        Win32Window?Window=Host.Window;
        if(Host.ExternalSurface is null&&Window is null){
            // Windowが未生成のフレームで集めたロースターは次フレームと二重集計されるため破棄する。
            this.PendingStaticMeshes=null;
            this.PendingPrimitives=null;
            this.PendingSkeletalMeshes=null;
            this.PendingCameraEntity=default;
            this.PendingCamera=default;
            this.ActiveCameraCount=0;
            return;
        }
        this.RosterCaptured=true;
        if(this.ActiveCameraCount==0&&Host.EditorCamera is null){
            throw new InvalidOperationException("Runtime requires exactly one active Camera, but none was found.");
        }
        // ADR-0070 B1：エディターカメラがすべてのシーンカメラに優先する。エディターのワールド（起動
        // シーンと編集中のシーン）にはカメラが複数ある場合があるが、ゲーム内ではちょうど1つ必要。
        if(this.ActiveCameraCount>1&&Host.EditorCamera is null){
            throw new InvalidOperationException($"Runtime requires exactly one active Camera, but {this.ActiveCameraCount} were found.");
        }

        var Scene=StageSceneDataBuilder.Build(
            this.PendingStaticMeshes??=[],
            this.PendingPrimitives??=[],
            this.PendingSkeletalMeshes??=[]);
        var Renderer=Host.ExternalSurface is { }Surface
            ?D3D12RenderServer.Create(
                                  Surface.Handle,Surface.Width,Surface.Height,Surface.DeviceOptions,
                                  ContentRoot.Resolve(Level.ShaderArtifactPath),Scene.Meshes,Scene.Materials,Scene.Objects)
            :D3D12RenderServer.Create(
                                  Window!.Handle,Window.ClientWidth,Window.ClientHeight,GraphicsDeviceOptionsResolver.FromEnvironment(),
                                  ContentRoot.Resolve(Level.ShaderArtifactPath),Scene.Meshes,Scene.Materials,Scene.Objects);
        Host.OwnRenderer(Renderer);
        if(Window is not null){
            Window.Resized+=(Width,Height)=>Renderer.Resize(Width,Height);
        }

        var Camera=RenderCamera.CreateFirstPerson(Vector3.Zero);
        FixedTransformResolveStatus InitialCameraResolveStatus=ResolveCamera(
            Host,this.PendingCameraEntity,State.World,out FixedAffineTransform InitialCameraWorld,out var InitialCameraFrame);
        LevelRuntime.LastCameraWorldResolveStatus=InitialCameraResolveStatus;
        if(InitialCameraResolveStatus!=FixedTransformResolveStatus.Success){
            throw new InvalidOperationException(
                $"The active camera hierarchy cannot be resolved in fixed precision: {InitialCameraResolveStatus}.");
        }

        FixedTransformStatus InitialCameraStatus=CameraTransform.TryApplyWorldOrientation(
            InitialCameraWorld,ref Camera,out _);
        LevelRuntime.LastCameraTransformStatus=InitialCameraStatus;
        if(InitialCameraStatus!=FixedTransformStatus.Success){
            throw new InvalidOperationException(
                $"The active camera Transform basis cannot produce a renderer view: {InitialCameraStatus}.");
        }

        // 後続フレームと同様に、レンダラーが使用できないシーン値は内容を示し、既定の投影を維持する。
        // 読み込みを失敗させない（保存された近平面0が原因で、ゲームと Scene Designer のビューが以前終了していた）。
        LevelRuntime.InvalidCameraProperty=ProjectIfUsable(ref Camera,Host.EditorCamera,this.ActiveCameraCount>0,this.PendingCamera,Renderer);
        Host.AttachRenderer(Renderer,Camera,InitialCameraWorld,InitialCameraFrame);
        Host.RegisterWorldGridMaterials(Scene.Materials);
        // ここからはレンダラー内のオブジェクトが Entity に追従する（M0105）。オブジェクトに変換されたばかりの Entity も登録済みとして扱う。
        Host.Roster=RenderRoster.FromScene(Scene);
        LevelRuntime.HasLastCameraWorldPose=true;
        LevelRuntime.Loaded=true;
        LevelRuntime.ShaderSemanticHash=Renderer.ShaderSemanticHash;
        LevelRuntime.ShaderArtifactLoaded=Renderer.ShaderArtifactLoaded;
        LevelRuntime.ShaderArtifactDiagnosticCount=Renderer.ShaderArtifactDiagnostics.Count;
        Renderer.SetCamera(Camera);


        for(var ObjectIndex=0;ObjectIndex<Scene.Objects.Length;ObjectIndex++){
            FixedTransformStatus Status=RuntimeWorldTransform.TryGetCameraRelativeMatrix(
                Scene.SourceEntities[ObjectIndex],State.World,Host,
                out Matrix4x4 CameraRelativeWorld,
                out FixedTransformResolveStatus ResolveStatus);
            if(Status!=FixedTransformStatus.Success){
                throw new InvalidOperationException(
                    $"The initial render pose cannot be represented relative to the active camera: {Status}.");
            }
            Renderer.SetObjectTransform(
                ObjectIndex,
                Scene.ObjectLocalTransforms[ObjectIndex]*CameraRelativeWorld);
            Host.RegisterRenderObject(Scene.SourceEntities[ObjectIndex],ObjectIndex);
            State.World.CreateDeferred(
                Scene.ObjectTransforms[ObjectIndex],
                new Renderable {
                    ObjectIndex=ObjectIndex,
                    SourceEntity=Scene.SourceEntities[ObjectIndex],
                    HasSourceEntity=true,
                    MeshLocalTransform=Scene.ObjectLocalTransforms[ObjectIndex],
                    HasMeshLocalTransform=true,
                    LastCameraRelativeWorld=CameraRelativeWorld,
                    HasLastCameraRelativeWorld=true,
                    LastTransformStatus=FixedTransformStatus.Success,
                    LastWorldResolveStatus=ResolveStatus,
                });
            StageMaterialCreateInfo Material=Scene.Materials[
                Scene.Objects[ObjectIndex].MaterialIndex];
            var Emission=RenderRoster.EmissionOf(Material);
            if(RenderRoster.IsEmissive(Emission.Nits)&&RenderRoster.LightsTheScene(Scene.SourceEntities[ObjectIndex],State.World)){
                State.World.CreateDeferred(
                    Scene.ObjectTransforms[ObjectIndex],
                    RenderRoster.NewEmissiveLighting(
                        Scene.SourceEntities[ObjectIndex],Scene.Meshes[Scene.Objects[ObjectIndex].MeshIndex],Emission,
                        Scene.ObjectLocalTransforms[ObjectIndex],CameraRelativeWorld,ResolveStatus));
            }
        }

        foreach(var (SkinnedEntity,MeshIndices) in Scene.SkinnedMeshes){
            Host.RegisterSkinnedMeshes(SkinnedEntity,MeshIndices);
        }

        this.RosterCaptured=true;
        this.PendingStaticMeshes=null;
        this.PendingPrimitives=null;
        this.PendingSkeletalMeshes=null;
    }

    /// <summary>フレームを描画するカメラ。Scene Designer がエディターカメラを渡した場合はそのカメラ（ADR-0070 B1）、そうでなければシーンのアクティブカメラ。</summary>
    /// <summary>
    /// <see cref="RuntimeAutomation.CameraVariable"/>：カメラが属する Entity のワールド姿勢を、そこからのオフセット位置に置き換える（車両を横から撮影する
    /// デバッグビューなど）。同じ置き換えを <see cref="CameraOriginSync"/> にも適用し、レンダラーの原点とカメラの位置を一致させる。
    /// </summary>
    internal static void ApplyAutomationCamera(EngineHost Host,Entity Camera,EcsWorld World,ref FixedAffineTransform CameraWorld){
        if(Host.Automation?.CameraOverride is not{ }Values||!World.TryGetComponent(Camera,out Parent Hanger)
            ||RuntimeWorldTransform.TryResolveWorld(Hanger.Value,World,out FixedAffineTransform HangerWorld,out _)!=FixedTransformResolveStatus.Success){
            return;
        }
        var Offset=new LogicalVector3(LogicalScalar.FromSingle(Values[0]),LogicalScalar.FromSingle(Values[1]),LogicalScalar.FromSingle(Values[2]));
        var Position=HangerWorld.Logical.Translation+Offset;
        if(!LogicalQuaternion.TryCreateAxisAngle(LogicalVector3.UnitU,LogicalScalar.FromSingle(Values[3]*(MathF.PI/180f)),out var Yaw)
            ||!LogicalQuaternion.TryCreateAxisAngle(LogicalVector3.UnitR,LogicalScalar.FromSingle(Values[4]*(MathF.PI/180f)),out var Pitch)
            ||!LogicalQuaternion.TryMultiply(Yaw,Pitch,out var Rotation)){
            return;
        }
        var Replaced=new Transform { Position=Position,Rotation=Rotation };
        if(FixedAffineTransform.TryFromTransform(Replaced,out var Result)==FixedTransformStatus.Success){
            CameraWorld=Result;
        }
    }
    private static FixedTransformResolveStatus ResolveCamera(
        EngineHost Host,Entity SceneCamera,EcsWorld World,out FixedAffineTransform CameraWorld,out CSharpGameEngine.Contracts.ReferenceFrameId Frame){
        if(Host.EditorCamera is not { }Editor){
            return RuntimeWorldTransform.TryResolveWorld(SceneCamera,World,out CameraWorld,out Frame);
        }
        Frame=CSharpGameEngine.Contracts.ReferenceFrameId.Root;
        return FixedAffineTransform.TryFromTransform(Editor.Pose,out CameraWorld) switch {
            FixedTransformStatus.Success=>FixedTransformResolveStatus.Success,
            FixedTransformStatus.NumericOverflow=>FixedTransformResolveStatus.NumericOverflow,
            _=>FixedTransformResolveStatus.NonInvertible,
        };
    }
}

/// <summary>アニメーター実行後、各 SkeletalMesh のパレットをレンダラーに渡す（ADR-0066）。デバイス回復後に現在の姿勢を再生できるよう、毎フレーム送信する。</summary>
[SystemPhase(SystemPhase.Presentation)]
[RunsAfter(typeof(CSharpGameEngine.Runtime.Animation.LocomotionAnimation))]
[RunsAfter(typeof(CSharpGameEngine.Runtime.Character.FootIkUpdate))]
[RunsAfter(typeof(CSharpGameEngine.Runtime.Character.FirstPersonPoseUpdate))]
internal struct SkinnedMeshSync:ISystem {
    public void Update(Entity Entity,ref SkeletalMeshRuntime Runtime,EngineHost Host,ref State State){
        if(Runtime.Palette is not{ }Palette||Host.Renderer is not{ }Renderer||!Host.TryGetSkinnedMeshes(Entity,out var MeshIndices)){
            return;
        }
        foreach(var MeshIndex in MeshIndices){
            Renderer.SetSkinPalette(MeshIndex,Palette);
        }
    }
}

[SystemPhase(SystemPhase.Presentation)]
[RunsAfter(typeof(LevelLoad))]
internal struct CameraOriginSync:ISystem {
    public void Update(Entity Entity,ref Transform Transform,ref Camera Camera,EngineHost Host,ref State State){
        // ADR-0070 B1：Scene Designer のカメラがシーンカメラに優先する。LevelLoad が今フレームの姿勢をすでに設定した。
        // ここでシーンカメラの姿勢を取得すると、すべてのオブジェクトを
        // 実際の表示に使わないカメラ基準で描画してしまう（Designer のホイール操作や軌道操作がビューに反映されなくなる）。
        if(Host.EditorCamera is not null){
            return;
        }
        if(Camera.Active
            &&RuntimeWorldTransform.TryResolveWorld(Entity,State.World,out FixedAffineTransform CameraWorld,out var CameraFrame)
                ==FixedTransformResolveStatus.Success
            &&CameraWorld.TryGetCameraBasis(out Vector3 Forward,out Vector3 Up)
                ==FixedTransformStatus.Success
            &&RenderCamera.TryNormalizeOrientation(Forward,Up,
                out _,out _)){
            LevelLoad.ApplyAutomationCamera(Host,Entity,State.World,ref CameraWorld);
            Host.SetCameraPose(CameraWorld,CameraFrame);
            // M0107：カメラが属するキャラクターを自動化レポートに含める（追従が遅れるカメラでは現在位置が分からない）。
            Host.AutomationFocusPosition=null;
            if(State.World.TryGetComponent(Entity,out Parent Hanger)
                &&RuntimeWorldTransform.TryResolveWorld(Hanger.Value,State.World,out FixedAffineTransform HangerWorld,out _)==FixedTransformResolveStatus.Success
                &&HangerWorld.M41.TryToSingle(out var HangerR)&&HangerWorld.M42.TryToSingle(out var HangerU)&&HangerWorld.M43.TryToSingle(out var HangerF)){
                Host.AutomationFocusPosition=new Vector3(HangerR,HangerU,HangerF);
            }
        }
    }
}

/// <summary>
/// M0099（ADR-0076）：有効な <see cref="RadialObserver"/> をそれぞれ、アクティブカメラが放射カメラの場合の観測者カメラプレビューにする。ゲーム実行時専用。
/// エディターカメラ使用中は、エディター側のプレビューが優先する。
/// </summary>
[SystemPhase(SystemPhase.Presentation)]
[RunsAfter(typeof(CameraOriginSync))]
internal struct RadialObserverSync:ISystem {
    public void Update(Entity Entity,ref Transform Transform,ref RadialObserver Observer,EngineHost Host,ref State State){
        if(!Observer.Enabled||Host.EditorCamera is not null||Host.Renderer is not{ }Renderer){
            return;
        }
        if(LightTransform.TryGetRectanglePose(Entity,State.World,Host,out var Eye,out var Forward,out var Up,out _)!=FixedTransformStatus.Success){
            return;
        }
        if(RadialObserverView.TryCreatePreview(Host.RenderCamera,Renderer.SurfaceWidth,Renderer.SurfaceHeight,Observer,Eye,Forward,Up,out var Preview)){
            Host.AddRadialObserverPreview(Preview);
        }
    }
}

/// <summary>放射カメラの観測者プレビュー（M0099、Omni 仕様の「Observer view」）。</summary>
internal static class RadialObserverView {
    /// <summary>
    /// 放射カメラの周囲を圧縮したワールドをインセットに描画する。<paramref name="Center"/> はレンダリング原点であり、<paramref name="EyeFromCenter"/>
    ///  から <paramref name="Forward"/> / <paramref name="Up"/> 方向を見る。遠平面は圧縮後のワールド全体を囲む。カメラが放射カメラでない場合、または値を
    /// 使用できない場合は false。
    /// </summary>
    internal static bool TryCreatePreview(in RenderCamera Center,int SurfaceWidth,int SurfaceHeight,in RadialObserver Observer,
        Vector3 EyeFromCenter,Vector3 Forward,Vector3 Up,out RenderCameraPreview Preview){
        Preview=default;
        if(SurfaceWidth<=0||SurfaceHeight<=0
            ||!RadialDepthCurve.TryCreate(Center.RadialTransitionMeters,out var Curve)
            ||!RadialDepthProjection.TryCreate(Curve,Center.NearZ,Center.FarZ,Center.FovYRadians,SurfaceWidth/(float)SurfaceHeight,out var Projection)){
            return false;
        }
        var X=(int)MathF.Round(Observer.ViewportX*SurfaceWidth);
        var Y=(int)MathF.Round(Observer.ViewportY*SurfaceHeight);
        var Width=(int)MathF.Round(Observer.ViewportWidth*SurfaceWidth);
        var Height=(int)MathF.Round(Observer.ViewportHeight*SurfaceHeight);
        if(Width<1||Height<1||!float.IsFinite(Observer.NearZ)||Observer.NearZ<=0f
            ||!(Observer.FovYRadians>0f&&Observer.FovYRadians<MathF.PI)){
            return false;
        }
        var Far=EyeFromCenter.Length()+1.05f*Projection.ObservedWorldRadius;
        if(!float.IsFinite(Far)||Far<=Observer.NearZ){
            return false;
        }
        var View=RenderCamera.CreateFirstPerson(Vector3.Zero);
        if(!RenderCamera.IsIdentityWorldOrientation(Forward,Up)&&!View.TrySetWorldOrientation(Forward,Up)){
            return false;
        }
        var ViewProjection=View.GetView()*Matrix4x4.CreatePerspectiveFieldOfView(Observer.FovYRadians,Width/(float)Height,Observer.NearZ,Far);
        Preview=new RenderCameraPreview(X,Y,Width,Height,ViewProjection,EyeFromCenter,
            Depth:RenderViewDepth.ForObserver(Projection,Vector3.Zero),MarkRadialCenter:Observer.ShowCenterCamera);
        return true;
    }
}

[SystemPhase(SystemPhase.Presentation)]
[RunsAfter(typeof(CameraOriginSync))]
internal struct DirectionalLightSync:ISystem {
    public void Update(Entity Entity,ref DirectionalLight Light,ref DirectionalLightRuntime Runtime,EngineHost Host,ref State State){
        Runtime.InvalidProperty=LightValidation.SceneFailure(Entity,State.World);
        if(Runtime.InvalidProperty is not null){return;}
        if(!Light.Color.IsValid){Runtime.InvalidProperty="Color";return;}
        if(!Light.Illuminance.IsValid){Runtime.InvalidProperty="Illuminance";return;}
        if(LightValidation.Hidden(Entity,State.World)){return;}
        var Shadow=LightValidation.Shadow(Entity,State.World);
        if(Host.Renderer is { }Renderer){
            FixedTransformStatus Status=LightTransform.TryGetCameraRelativePose(
                Entity,State.World,Host,out _,out Vector3 Forward,out FixedTransformResolveStatus ResolveStatus);
            Runtime.LastTransformStatus=Status;
            Runtime.LastWorldResolveStatus=ResolveStatus;
            if(Status==FixedTransformStatus.Success){
                Runtime.LastWorldForward=Forward;
                Runtime.HasLastWorldForward=true;
            } else if(!Runtime.HasLastWorldForward){
                return;
            }
            var Submission=new DirectionalLightSubmission(Runtime.LastWorldForward,Light.Color,Light.Illuminance,Shadow);
            if(!Submission.IsValid){Runtime.InvalidProperty="Submission";return;}
            try{ Renderer.AddDirectionalLight(Submission); }
            catch(ArgumentException Error){ Runtime.InvalidProperty=Error is ArgumentOutOfRangeException RangeError?RangeError.ParamName??"Submission":"Submission"; }
        }
    }
}

[SystemPhase(SystemPhase.Presentation)]
[RunsAfter(typeof(DirectionalLightSync))]
internal struct AmbientLightSync:ISystem {
    public void Update(ref AmbientLight Light,EngineHost Host,ref State State){
        var Ambient=new Vector3(Light.ColorR,Light.ColorG,Light.ColorB)*Light.Intensity;
        // 型付きライトの場合と同様に、レンダラーが使用できない値なら最後に使用可能だった値を維持する。負の放射輝度はトーン
        // マッパーによって白くなり、範囲を超える値も使用できない。
        if(Light.ColorR>=0f&&Light.ColorG>=0f&&Light.ColorB>=0f&&Light.Intensity>=0f
            &&float.IsFinite(Ambient.X)&&float.IsFinite(Ambient.Y)&&float.IsFinite(Ambient.Z)){
            Host.Renderer?.SetAmbientLight(Ambient);
        } else {
            Host.Environment.InvalidReason=nameof(AmbientLight);
        }
        Host.Environment.AmbientSubmitted=true;
    }
}

/// <summary>環境コンポーネントを収集する（ADR-0069）。EngineHost は描画前に一度だけ送信する。</summary>
[SystemPhase(SystemPhase.Presentation)]
[RunsAfter(typeof(AmbientLightSync))]
internal struct EnvironmentCollect:ISystem {
    public void Update(ref DirectionalShadowRendering Value,EngineHost Host,ref State State){
        Host.Environment.DirectionalShadows=Value;
    }
    public void Update(ref SkyAtmosphere Value,EngineHost Host,ref State State){
        Host.Environment.Atmosphere=Value;
    }
    public void Update(ref SkyLight Value,EngineHost Host,ref State State){
        Host.Environment.SkyLight=Value;
    }
    public void Update(ref PostProcessVolume Value,EngineHost Host,ref State State){
        Host.Environment.PostProcess=Value;
    }
    public void Update(ref ExponentialHeightFog Value,EngineHost Host,ref State State){
        Host.Environment.Fog=Value;
    }
    public void Update(Entity Entity,ref GlobalIlluminationVolume Value,EngineHost Host,ref State State){
        // プローブ格子は各オブジェクトと同様、レンダラーのカメラ相対座標に配置する。そのため、
        // ボリュームの絶対位置が float の境界を越えて渡されることはない。
        if(RuntimeWorldTransform.TryGetCameraRelativeMatrix(Entity,State.World,Host,out var Matrix,out _)!=FixedTransformStatus.Success){
            Host.Environment.InvalidReason="GlobalIlluminationVolume.Transform";
            return;
        }
        Host.Environment.GlobalIllumination=Value;
        Host.Environment.GlobalIlluminationCenter=Matrix.Translation;
    }
    public void Update(ref VolumetricCloud Value,EngineHost Host,ref State State){
        Host.Environment.Clouds=Value;
    }
    public void Update(ref GlobalDistanceField Value,EngineHost Host,ref State State){
        Host.Environment.DistanceField=Value;
    }
    public void Update(Entity Entity,ref SkySphere Value,EngineHost Host,ref State State){
        // ADR-0078/M0110：プローブボリュームと同様に、球の中心をカメラ相対のレンダリング空間に置く。
        if(RuntimeWorldTransform.TryGetCameraRelativeMatrix(Entity,State.World,Host,out var Matrix,out _)!=FixedTransformStatus.Success){
            Host.Environment.InvalidReason="SkySphere.Transform";
            return;
        }
        Host.Environment.SkySphere=Value;
        Host.Environment.SkySphereCenter=Matrix.Translation;
    }
}

[SystemPhase(SystemPhase.Presentation)]
[RunsAfter(typeof(EnvironmentCollect))]
internal struct HeightFogSync:ISystem {
    public void Update(ref HeightFog Fog,EngineHost Host,ref State State){
        Host.Renderer?.SetFog(
            new FogParams(Fog.Density,Fog.HeightFalloff,new Vector3(Fog.ColorR,Fog.ColorG,Fog.ColorB)));
    }
}

[SystemPhase(SystemPhase.Presentation)]
[RunsAfter(typeof(HeightFogSync))]
internal struct PointLightSync:ISystem {
    public void Update(Entity Entity,ref PointLight Light,ref PointLightRuntime Runtime,EngineHost Host,ref State State){
        Runtime.InvalidProperty=LightValidation.SceneFailure(Entity,State.World);
        if(Runtime.InvalidProperty is not null){return;}
        if(!Light.Color.IsValid){Runtime.InvalidProperty="Color";return;}
        if(!Light.Flux.IsValid){Runtime.InvalidProperty="Flux";return;}
        if(!Light.Range.IsValid){Runtime.InvalidProperty="Range";return;}
        if(LightValidation.Hidden(Entity,State.World)){return;}
        var Shadow=LightValidation.Shadow(Entity,State.World);
        if(Host.Renderer is { }Renderer){
            FixedTransformStatus Status=LightTransform.TryGetCameraRelativePose(
                Entity,State.World,Host,out Vector3 Position,out _,out FixedTransformResolveStatus ResolveStatus);
            Runtime.LastTransformStatus=Status;
            Runtime.LastWorldResolveStatus=ResolveStatus;
            if(Status==FixedTransformStatus.Success){
                Runtime.LastCameraRelativePosition=Position;
                Runtime.HasLastCameraRelativePosition=true;
            } else if(!Runtime.HasLastCameraRelativePosition){
                return;
            }
            var Submission=new PointLightSubmission(Runtime.LastCameraRelativePosition,Light.Color,Light.Flux,Light.Range,Shadow);
            if(!Submission.IsValid){Runtime.InvalidProperty="Submission";return;}
            try{ Renderer.AddPointLight(Submission); }
            catch(ArgumentException Error){ Runtime.InvalidProperty=Error is ArgumentOutOfRangeException RangeError?RangeError.ParamName??"Submission":"Submission"; }
        }
    }
}

[SystemPhase(SystemPhase.Presentation)]
[RunsAfter(typeof(PointLightSync))]
internal struct SpotLightSync:ISystem {
    public void Update(Entity Entity,ref SpotLight Light,ref SpotLightRuntime Runtime,EngineHost Host,ref State State){
        Runtime.InvalidProperty=LightValidation.SceneFailure(Entity,State.World);
        if(Runtime.InvalidProperty is not null){return;}
        if(!Light.Color.IsValid){Runtime.InvalidProperty="Color";return;}
        if(!Light.Flux.IsValid){Runtime.InvalidProperty="Flux";return;}
        if(!Light.Range.IsValid){Runtime.InvalidProperty="Range";return;}
        if(!Light.Cone.IsValid){Runtime.InvalidProperty="Cone";return;}
        if(LightValidation.Hidden(Entity,State.World)){return;}
        var Shadow=LightValidation.Shadow(Entity,State.World);
        if(Host.Renderer is { }Renderer){
            FixedTransformStatus Status=LightTransform.TryGetCameraRelativePose(
                Entity,State.World,Host,out Vector3 Position,out Vector3 Forward,out FixedTransformResolveStatus ResolveStatus);
            Runtime.LastTransformStatus=Status;
            Runtime.LastWorldResolveStatus=ResolveStatus;
            if(Status==FixedTransformStatus.Success){
                Runtime.LastCameraRelativePosition=Position;
                Runtime.LastWorldForward=Forward;
                Runtime.HasLastCameraRelativePosition=true;
                Runtime.HasLastWorldForward=true;
            } else if(!Runtime.HasLastCameraRelativePosition||!Runtime.HasLastWorldForward){
                return;
            }
            var Submission=new SpotLightSubmission(Runtime.LastCameraRelativePosition,Runtime.LastWorldForward,Light.Color,Light.Flux,Light.Range,Light.Cone,Shadow);
            if(!Submission.IsValid){Runtime.InvalidProperty="Submission";return;}
            try{ Renderer.AddSpotLight(Submission); }
            catch(ArgumentException Error){ Runtime.InvalidProperty=Error is ArgumentOutOfRangeException RangeError?RangeError.ParamName??"Submission":"Submission"; }
        }
    }
}

[SystemPhase(SystemPhase.Presentation)]
[RunsAfter(typeof(SpotLightSync))]
internal struct RectAreaLightSync:ISystem {
    public void Update(Entity Entity,ref RectAreaLight Light,ref RectAreaLightRuntime Runtime,EngineHost Host,ref State State){
        Runtime.InvalidProperty=LightValidation.SceneFailure(Entity,State.World);
        if(Runtime.InvalidProperty is not null){return;}
        if(!Light.Color.IsValid){Runtime.InvalidProperty="Color";return;}
        if(!Light.Luminance.IsValid){Runtime.InvalidProperty="Luminance";return;}
        if(!Light.Size.IsValid){Runtime.InvalidProperty="Size";return;}
        if(!Light.Range.IsValid){Runtime.InvalidProperty="Range";return;}
        if(LightValidation.Hidden(Entity,State.World)){return;}
        var Shadow=LightValidation.Shadow(Entity,State.World);
        if(Host.Renderer is { }Renderer){
            FixedTransformStatus Status=LightTransform.TryGetRectanglePose(
                Entity,State.World,Host,out Vector3 Position,out Vector3 Forward,out Vector3 Up,out FixedTransformResolveStatus ResolveStatus);
            Runtime.LastTransformStatus=Status;
            Runtime.LastWorldResolveStatus=ResolveStatus;
            if(Status==FixedTransformStatus.Success){
                Runtime.LastCameraRelativePosition=Position;
                Runtime.LastWorldForward=Forward;
                Runtime.LastWorldUp=Up;
                Runtime.HasLastCameraRelativePosition=true;
                Runtime.HasLastWorldForward=true;
            } else if(!Runtime.HasLastCameraRelativePosition||!Runtime.HasLastWorldForward){
                return;
            }
            var Submission=new RectAreaLightSubmission(Runtime.LastCameraRelativePosition,Runtime.LastWorldForward,Runtime.LastWorldUp,Light.Color,Light.Luminance,Light.Size,Light.Range,Shadow);
            if(!Submission.IsValid){Runtime.InvalidProperty="Submission";return;}
            try{ Renderer.AddRectAreaLight(Submission); }
            catch(ArgumentException Error){ Runtime.InvalidProperty=Error is ArgumentOutOfRangeException RangeError?RangeError.ParamName??"Submission":"Submission"; }
        }
    }
}

[SystemPhase(SystemPhase.Presentation)]
[RunsAfter(typeof(RectAreaLightSync))]
internal struct TransformSync:ISystem {
    public void Update(Entity Entity,ref Transform Transform,ref Renderable Renderable,EngineHost Host,ref State State){
        if(Renderable.ObjectIndex<0){
            return;
        }
        if(Renderable.HasSourceEntity&&!RenderRoster.IsDrawnSource(Renderable.SourceEntity,State.World)){
            // M0105：Entity が破棄されたかメッシュを失った。対応するオブジェクトの描画を止め、ラッパーも削除する。
            Host.ReleaseRenderObject(Renderable.SourceEntity,Renderable.ObjectIndex);
            Renderable.ObjectIndex=-1;
            State.World.DestroyDeferred(Entity,ref State);
            return;
        }
        if(Host.Renderer is{ }Renderer){
            Entity SourceEntity=Renderable.HasSourceEntity?Renderable.SourceEntity:Entity;
            FixedTransformStatus Status=RuntimeWorldTransform.TryGetCameraRelativeMatrix(
                SourceEntity,State.World,Host,out Matrix4x4 CameraRelativeWorld,
                out FixedTransformResolveStatus ResolveStatus);
            Renderable.LastTransformStatus=Status;
            Renderable.LastWorldResolveStatus=ResolveStatus;
            if(Status==FixedTransformStatus.Success){
                Renderable.LastCameraRelativeWorld=CameraRelativeWorld;
                Renderable.HasLastCameraRelativeWorld=true;
            } else if(!Renderable.HasLastCameraRelativeWorld){
                return;
            }

            Matrix4x4 Local=Renderable.HasMeshLocalTransform
                ?Renderable.MeshLocalTransform
                :Matrix4x4.Identity;
            var World=Local*Renderable.LastCameraRelativeWorld;
            // ADR-0078/M0107：一人称メッシュは専用の視野角とスケールで描画する。メッシュをカメラの映像から除外することもできる。
            // または影の外にする。これらのコンポーネントを持つメッシュだけが処理コストを負担する。
            if(State.World.TryGetComponent(SourceEntity,out FirstPersonView FirstPerson)){
                World*=FirstPersonViewWarp.Matrix(Host.RenderCamera,FirstPerson);
            }
            Renderer.SetObjectTransform(Renderable.ObjectIndex,World);
            if(State.World.TryGetComponent(SourceEntity,out MeshVisibility Visibility)){
                Renderer.SetObjectVisibility(Renderable.ObjectIndex,Visibility.VisibleToCamera,Visibility.CastsShadow);
                Renderable.VisibilityApplied=true;
            } else if(Renderable.VisibilityApplied){
                Renderer.SetObjectVisibility(Renderable.ObjectIndex,true,true);
                Renderable.VisibilityApplied=false;
            }
            // ADR-0078/M0109：ゲーム実行中に変更されるマテリアル発光（車両のブレーキランプなど）。
            if(State.World.TryGetComponent(SourceEntity,out MeshEmissive Emissive)){
                var Scale=float.IsFinite(Emissive.Scale)?MathF.Max(0f,Emissive.Scale):1f;
                Renderer.SetObjectEmissiveScale(Renderable.ObjectIndex,Scale);
                Renderable.EmissiveApplied=true;
            } else if(Renderable.EmissiveApplied){
                Renderer.SetObjectEmissiveScale(Renderable.ObjectIndex,1f);
                Renderable.EmissiveApplied=false;
            }
        }
    }
}

[SystemPhase(SystemPhase.Presentation)]
[RunsAfter(typeof(TransformSync))]
[RunsBefore(typeof(CSharpGameEngine.Runtime.Animation.SkeletalMeshSetup))]
internal struct EmissiveMeshLightSync:ISystem {
    public void Update(
        Entity Entity,
        ref Transform Transform,
        ref EmissiveMeshLighting Lighting,
        EngineHost Host,
        ref State State){
        if(Lighting.HasSourceEntity&&!RenderRoster.IsDrawnSource(Lighting.SourceEntity,State.World)){
            State.World.DestroyDeferred(Entity,ref State);
            return;
        }
        if(Host.Renderer is { }Renderer){
            Entity SourceEntity=Lighting.HasSourceEntity?Lighting.SourceEntity:Entity;
            FixedTransformStatus Status=RuntimeWorldTransform.TryGetCameraRelativeMatrix(
                SourceEntity,State.World,Host,out Matrix4x4 CameraRelativeWorld,
                out FixedTransformResolveStatus ResolveStatus);
            Lighting.LastTransformStatus=Status;
            Lighting.LastWorldResolveStatus=ResolveStatus;
            if(Status==FixedTransformStatus.Success){
                Lighting.LastCameraRelativeWorld=CameraRelativeWorld;
                Lighting.HasLastCameraRelativeWorld=true;
            } else if(!Lighting.HasLastCameraRelativeWorld){
                return;
            }

            IReadOnlyList<EmissivePatch> Patches=EmissivePatchBuilder.Build(
                new EmissivePatchBuildInput {
                    Positions=Lighting.Mesh.Positions,
                    Indices=Lighting.Mesh.Indices,
                    ObjectWorld=Lighting.MeshLocalTransform*Lighting.LastCameraRelativeWorld,
                    EmissiveNits=Lighting.EmissiveNits,
                    EmissiveTextureLuminanceScale=Lighting.EmissiveTextureLuminanceScale,
                });
            Renderer.SubmitDirectLighting(
                Patches,
                Lighting.RangeMeters,
                Lighting.CastsShadows,
                Lighting.ShadowPriority,
                Lighting.ShadowBiasMeters);
        }
    }
}

/// <summary><see cref="StaticMesh"/> があり、まだ描画されていない Entity に、レンダラーのオブジェクトを割り当てる（ADR-0078/M0105）。</summary>
[SystemPhase(SystemPhase.Presentation)]
[RunsAfter(typeof(CameraOriginSync))]
[RunsBefore(typeof(TransformSync))]
internal struct StaticMeshBind:ISystem {
    public void Update(Entity Entity,ref Transform Transform,ref StaticMesh Mesh,EngineHost Host,ref State State){
        Host.Roster?.BindStaticMesh(Entity,in Transform,in Mesh,Host,ref State);
    }
}

/// <summary><see cref="PrimitiveMesh"/> に対して同じ処理を行う（ADR-0078/M0105）。</summary>
[SystemPhase(SystemPhase.Presentation)]
[RunsAfter(typeof(CameraOriginSync))]
[RunsBefore(typeof(TransformSync))]
internal struct PrimitiveMeshBind:ISystem {
    public void Update(Entity Entity,ref Transform Transform,ref PrimitiveMesh Mesh,EngineHost Host,ref State State){
        Host.Roster?.BindPrimitiveMesh(Entity,in Transform,in Mesh,Host,ref State);
    }
}

/// <summary>レベル読み込み後に追加された <see cref="SkeletalMesh"/>（再生成されたキャラクターなど）に、レンダラーのオブジェクトを割り当てる（ADR-0078/M0107）。</summary>
[SystemPhase(SystemPhase.Presentation)]
[RunsAfter(typeof(CameraOriginSync))]
[RunsBefore(typeof(TransformSync))]
internal struct SkeletalMeshBind:ISystem {
    public void Update(Entity Entity,ref Transform Transform,ref SkeletalMesh Mesh,EngineHost Host,ref State State){
        Host.Roster?.BindSkeletalMesh(Entity,in Transform,in Mesh,Host,ref State);
    }
}

/// <summary>シーンに現在ルートだけが含まれる場合でも、生成された World の検索対象として Parent を登録する。RuntimeWorldTransform は、この API を通じて将来の任意の親変更も解決する。</summary>
internal struct HierarchyQueryRegistration:ISystem { public void Spawn(ref Parent Parent){} }

/// <summary>
/// ADR-0078/M0107、M0109：<see cref="TransformSync"/> はメッシュ Entity から <see cref="MeshVisibility"/>、
/// <see cref="FirstPersonView"/>、<see cref="MeshEmissive"/> を読み取る。ライト同期処理もライト Entity から
/// <see cref="LightVisibility"/> を読み取る。そのためシーンで使用されていない場合でも、生成 World にこれらがコンポーネントとして登録されている必要がある。
/// </summary>
internal struct MeshPresentationRegistration:ISystem {
    public void Spawn(ref MeshVisibility Visibility){}
    public void Spawn(ref FirstPersonView View){}
    public void Spawn(ref MeshEmissive Emissive){}
    public void Spawn(ref LightVisibility Visibility){}
}

[SystemPhase(SystemPhase.Presentation)]
[RunsAfter(typeof(SkinnedMeshSync))]
internal struct AudioFrameBegin:ISystem {
    public void Update(ref AppHostRuntime Runtime,EngineHost Host,ref State State){
        Host.BeginAudioFrame();
    }
}

[SystemPhase(SystemPhase.Presentation)]
[RunsAfter(typeof(AudioFrameBegin))]
internal struct AudioListenerSync:ISystem {
    public void Update(Entity Entity,ref Transform Transform,ref AudioListener Listener,EngineHost Host,ref State State){
        FixedTransformResolveStatus Status=RuntimeWorldTransform.TryResolveWorld(
            Entity,State.World,out FixedAffineTransform Pose,out var Frame);
        if(Status==FixedTransformResolveStatus.Success){ Host.SetAudioListener(Entity,Pose,Frame); }
    }
}

[SystemPhase(SystemPhase.Presentation)]
[RunsAfter(typeof(AudioListenerSync))]
internal struct AudioSourceSync:ISystem {
    public void Update(Entity Entity,ref Transform Transform,ref AudioSource Source,EngineHost Host,ref State State){
        FixedTransformResolveStatus Status=RuntimeWorldTransform.TryResolveWorld(
            Entity,State.World,out FixedAffineTransform Pose,out var Frame);
        if(Status==FixedTransformResolveStatus.Success){
            Host.TrySubmitAudioSource(
                Entity,Pose,Frame,Source.Volume,Source.MinimumDistance,Source.MaximumDistance,Source.Loop);
        }
    }
}


/// <summary>表示されているすべての <see cref="ScreenText"/> をスクリーン UI サービスに渡す（ADR-0078/M0104）。</summary>
[SystemPhase(SystemPhase.Presentation)]
[RunsAfter(typeof(AudioSourceSync))]
internal struct ScreenTextCollect:ISystem {
    public void Update(in ScreenText Text,ScreenUi Ui){
        Ui.Add(in Text);
    }
}

/// <summary>表示されているすべての <see cref="ScreenImage"/> をスクリーン UI サービスに渡す（ADR-0078/M0104）。</summary>
[SystemPhase(SystemPhase.Presentation)]
[RunsAfter(typeof(ScreenTextCollect))]
internal struct ScreenImageCollect:ISystem {
    public void Update(in ScreenImage Image,ScreenUi Ui){
        Ui.Add(in Image);
    }
}

/// <summary>フレームの表示直前に、そのフレームのスクリーンレイヤーを設定する（ADR-0078/M0104）。</summary>
[SystemPhase(SystemPhase.Presentation)]
[RunsAfter(typeof(ScreenImageCollect))]
internal struct ScreenUiSubmit:ISystem {
    public void Update(ref LevelRuntime Runtime,EngineHost Host,ref State State){
        Host.SubmitScreenUi();
    }
}

[SystemPhase(SystemPhase.Presentation)]
[RunsAfter(typeof(ScreenUiSubmit))]
internal struct Present:ISystem {
    public void Update(ref LevelRuntime Runtime,EngineHost Host,ref State State){
        Host.Sound.Flush();
        if(State.Enabled&&!Host.Present()){ State.Enabled=false; }
    }
}
