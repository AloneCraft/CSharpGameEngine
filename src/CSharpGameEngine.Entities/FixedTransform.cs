using System.Collections.Generic;
using System.Numerics;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Numerics;
using EcsWorld=CSharpGameEngine.Entities.World;

namespace CSharpGameEngine.Runtime.Windows;

/// <summary>ラップアラウンドやGPU姿勢の欠落を黙って許容できないTransform演算の結果。</summary>
public enum FixedTransformStatus {
    Success,
    NumericOverflow,
    NonInvertible,
    FloatConversionOverflow,
}

/// <summary>
/// 行ベクトル規約のTRSヘルパー。子のワールド行列は常に<c>local * parentWorld</c>であり、
/// System.Numericsとエディターの契約に一致する。
/// </summary>
public static class TransformMath {
    /// <summary>
    /// TRSをfloatに変換する前に、固定小数点のカメラワールド原点を差し引く。
    /// オブジェクト、ライト、ピッキング、ギズモの描画座標にはこの変換だけを使う。
    /// </summary>
    public static FixedTransformStatus TryToCameraRelativeMatrix(
        in Transform Transform,in Transform Camera,out Matrix4x4 Matrix){
        FixedTransformStatus Status=FixedAffineTransform.TryFromTransform(
            Transform,out FixedAffineTransform Affine);
        if(Status!=FixedTransformStatus.Success){
            Matrix=default;
            return Status;
        }
        return Affine.TryToCameraRelativeMatrix(Camera,out Matrix);
    }
}

/// <summary>
/// 階層評価に使う固定小数点のアフィン行列。カメラ原点を差し引くまでアフィン変換の全項を保持し、
/// 大きな絶対位置がfloat境界を越えるのを防ぐ。
/// </summary>
public readonly struct FixedAffineTransform {
    private readonly LogicalAffineTransform Value;
    private FixedAffineTransform(LogicalAffineTransform Value){ this.Value=Value; }
    public LogicalScalar M11=>this.Value.RAxis.R;
    public LogicalScalar M12=>this.Value.RAxis.U;
    public LogicalScalar M13=>this.Value.RAxis.F;
    public LogicalScalar M21=>this.Value.UAxis.R;
    public LogicalScalar M22=>this.Value.UAxis.U;
    public LogicalScalar M23=>this.Value.UAxis.F;
    public LogicalScalar M31=>this.Value.FAxis.R;
    public LogicalScalar M32=>this.Value.FAxis.U;
    public LogicalScalar M33=>this.Value.FAxis.F;
    public LogicalScalar M41=>this.Value.Translation.R;
    public LogicalScalar M42=>this.Value.Translation.U;
    public LogicalScalar M43=>this.Value.Translation.F;
    public LogicalAffineTransform Logical=>this.Value;
    public static FixedAffineTransform Identity=>new(LogicalAffineTransform.Identity);
    public static FixedAffineTransform FromLogical(LogicalAffineTransform Value)=>new(Value);

    public static FixedTransformStatus TryFromTransform(
        in Transform Transform,out FixedAffineTransform Matrix){
        if(!Transform.Rotation.TryNormalize(out _)){
            Matrix=default;
            return FixedTransformStatus.NonInvertible;
        }
        if(!LogicalAffineTransform.TryCreateTrs(Transform.Position,Transform.Rotation,Transform.Scale,out var Logical)){
            Matrix=default;
            return FixedTransformStatus.NumericOverflow;
        }
        Matrix=new FixedAffineTransform(Logical);
        return FixedTransformStatus.Success;
    }

    public static FixedTransformStatus TryMultiply(
        in FixedAffineTransform Left,in FixedAffineTransform Right,out FixedAffineTransform Result){
        if(!LogicalAffineTransform.TryMultiply(Left.Value,Right.Value,out var Logical)){
            Result=default;
            return FixedTransformStatus.NumericOverflow;
        }
        Result=new FixedAffineTransform(Logical);
        return FixedTransformStatus.Success;
    }

    public FixedTransformStatus TryToCameraRelativeMatrix(
        in Transform Camera,out Matrix4x4 Matrix){
        FixedTransformStatus CameraStatus=TryFromTransform(Camera,out FixedAffineTransform CameraWorld);
        if(CameraStatus!=FixedTransformStatus.Success){
            Matrix=default;
            return CameraStatus;
        }
        return this.TryToCameraRelativeMatrix(CameraWorld,out Matrix);
    }

    /// <summary>
    /// floatへの最初の変換より前に、合成済みの固定小数点カメラワールド原点を差し引く。
    /// これは階層対応レンダラーの境界である。オブジェクトとカメラに任意の親があっても、絶対位置が浮動小数点値に入ることはない。
    /// </summary>
    public FixedTransformStatus TryToCameraRelativeMatrix(
        in FixedAffineTransform CameraWorld,out Matrix4x4 Matrix){
        if(!LogicalScalar.TrySubtract(this.Value.Translation.R,CameraWorld.Value.Translation.R,out LogicalScalar X)
            ||!LogicalScalar.TrySubtract(this.Value.Translation.U,CameraWorld.Value.Translation.U,out LogicalScalar Y)
            ||!LogicalScalar.TrySubtract(this.Value.Translation.F,CameraWorld.Value.Translation.F,out LogicalScalar Z)){
            Matrix=default;
            return FixedTransformStatus.NumericOverflow;
        }
        if(!this.Value.RAxis.TryToSingle(out Vector3 RAxis)
            ||!this.Value.UAxis.TryToSingle(out Vector3 UAxis)
            ||!this.Value.FAxis.TryToSingle(out Vector3 FAxis)
            ||!X.TryToSingle(out float M41)||!Y.TryToSingle(out float M42)||!Z.TryToSingle(out float M43)){
            Matrix=default;
            return FixedTransformStatus.FloatConversionOverflow;
        }
        Matrix=new Matrix4x4(
            RAxis.X,RAxis.Y,RAxis.Z,0f,
            UAxis.X,UAxis.Y,UAxis.Z,0f,
            FAxis.X,FAxis.Y,FAxis.Z,0f,
            M41,M42,M43,1f);
        return FixedTransformStatus.Success;
    }

    /// <summary>
    /// Transformのローカル+Z（前方）軸とローカル+Y（上方）軸について、解決済みのワールド方向を取り出す。
    /// 行ベクトル基底の各行はカメラビュー境界まで固定小数点で保持する。継承したスケールを除くため、呼び出し側で変換後に正規化する。
    /// </summary>
    public FixedTransformStatus TryGetCameraBasis(out Vector3 Forward,out Vector3 Up){
        if(!this.Value.FAxis.TryToSingle(out Forward)||!this.Value.UAxis.TryToSingle(out Up)){
            Forward=default;
            Up=default;
            return FixedTransformStatus.FloatConversionOverflow;
        }
        float ForwardLengthSquared=Forward.LengthSquared();
        float UpLengthSquared=Up.LengthSquared();
        if(!float.IsFinite(Forward.X)||!float.IsFinite(Forward.Y)||!float.IsFinite(Forward.Z)
            ||!float.IsFinite(Up.X)||!float.IsFinite(Up.Y)||!float.IsFinite(Up.Z)
            ||!float.IsFinite(ForwardLengthSquared)||!float.IsFinite(UpLengthSquared)
            ||ForwardLengthSquared<=1e-12f||UpLengthSquared<=1e-12f){
            Forward=default;
            Up=default;
            return FixedTransformStatus.NonInvertible;
        }

        return FixedTransformStatus.Success;
    }
}

/// <summary>演算失敗状態も含む、固定小数点階層の解決状態。</summary>
public enum FixedTransformResolveStatus {
    Success,
    MissingTransform,
    DeadParent,
    Cycle,
    NumericOverflow,
    NonInvertible,
    FrameMismatch,
}

/// <summary>
/// 親子のアフィン合成を、設定された固定精度だけで評価する。解決後にのみ
/// <see cref="FixedAffineTransform.TryToCameraRelativeMatrix"/>を呼び出すこと。
/// </summary>
public sealed class FixedTransformWorldResolver {
    private readonly Dictionary<Entity,FixedAffineTransform>WorldByEntity=[];
    private readonly Dictionary<Entity,FixedTransformResolveStatus>StatusByEntity=[];
    private readonly HashSet<Entity>Resolving=[];

    public void BeginFrame(){
        this.WorldByEntity.Clear();
        this.StatusByEntity.Clear();
        this.Resolving.Clear();
    }

    public FixedTransformResolveStatus Resolve(
        Entity Entity,
        TryGetLocalTransform GetTransform,
        TryGetParentEntity GetParent,
        out FixedAffineTransform World){
        if(this.WorldByEntity.TryGetValue(Entity,out World)){
            return FixedTransformResolveStatus.Success;
        }
        if(this.StatusByEntity.TryGetValue(Entity,out FixedTransformResolveStatus PreviousStatus)){
            World=default;
            return PreviousStatus;
        }
        if(!this.Resolving.Add(Entity)){
            World=default;
            return this.StatusByEntity[Entity]=FixedTransformResolveStatus.Cycle;
        }

        try {
            if(!GetTransform(Entity,out Transform Local)){
                World=default;
                return this.StatusByEntity[Entity]=FixedTransformResolveStatus.MissingTransform;
            }
            FixedTransformStatus LocalStatus=FixedAffineTransform.TryFromTransform(Local,out FixedAffineTransform LocalWorld);
            if(LocalStatus!=FixedTransformStatus.Success){
                World=default;
                return this.StatusByEntity[Entity]=LocalStatus==FixedTransformStatus.NonInvertible
                    ?FixedTransformResolveStatus.NonInvertible
                    :FixedTransformResolveStatus.NumericOverflow;
            }
            if(!GetParent(Entity,out Entity ParentEntity)){
                this.WorldByEntity.Add(Entity,World=LocalWorld);
                return FixedTransformResolveStatus.Success;
            }

            FixedTransformResolveStatus ParentStatus=this.Resolve(
                ParentEntity,GetTransform,GetParent,out FixedAffineTransform ParentWorld);
            if(ParentStatus!=FixedTransformResolveStatus.Success){
                World=default;
                return this.StatusByEntity[Entity]=ParentStatus==FixedTransformResolveStatus.MissingTransform
                    ?FixedTransformResolveStatus.DeadParent
                    :ParentStatus;
            }

            FixedTransformStatus ComposeStatus=FixedAffineTransform.TryMultiply(
                LocalWorld,ParentWorld,out World);
            if(ComposeStatus!=FixedTransformStatus.Success){
                World=default;
                return this.StatusByEntity[Entity]=FixedTransformResolveStatus.NumericOverflow;
            }
            this.WorldByEntity.Add(Entity,World);
            return FixedTransformResolveStatus.Success;
        } finally {
            this.Resolving.Remove(Entity);
        }
    }
}

/// <summary>
/// 生成されたECS階層と、シーンで選択した固定小数点Transform実装を橋渡しする。
/// トップレベルの解決ごとに新しいリゾルバーを用意し、前のECSフレームで行われた構造変更や親変更によって古い親チェーンが再利用されないようにする。
/// 1回の解決中はすべての祖先をキャッシュし、アフィン合成を完全に固定小数点で行う。
/// ECSのみに依存するため、物理システムはWindowsホストなしで使用できる（M0096 G）。
/// </summary>
internal static partial class RuntimeWorldTransform {
    public static FixedTransformResolveStatus TryResolveWorld(
        Entity Entity,EcsWorld EcsWorld,out FixedAffineTransform World)=>
        TryResolveWorld(Entity,EcsWorld,out World,out _);

    public static FixedTransformResolveStatus TryResolveWorld(
        Entity Entity,EcsWorld EcsWorld,out FixedAffineTransform World,out ReferenceFrameId Frame){
        FixedTransformWorldResolver Resolver=new();
        FixedTransformResolveStatus Status=Resolver.Resolve(
            Entity,
            (Entity Candidate,out Transform Transform)=>{
                if(EcsWorld.TryGetComponent(Candidate,out Transform)){
                    return true;
                }
                // シーンルートは、環境／コンポーネントを所有する論理ノードで、
                // 空間Transformを持たない場合がある。この恒等変換へのフォールバックを認めるのは、
                // 実際のシーンルートだけ。Transformのない任意の実行中エンティティは、不正な親として扱う。
                SceneInstance OwningScene=EcsWorld.GetOwningScene(Candidate);
                if(EcsWorld.IsAlive(Candidate)&&!OwningScene.IsNone
                    &&EcsWorld.GetSceneRoot(OwningScene).Equals(Candidate)){
                    Transform=new global::CSharpGameEngine.Entities.Transform();
                    return true;
                }
                Transform=default;
                return false;
            },
            (Entity Candidate,out Entity ParentEntity)=>{
                if(EcsWorld.TryGetComponent(Candidate,out Parent Parent)){
                    ParentEntity=Parent.Value;
                    return true;
                }
                ParentEntity=default;
                return false;
            },out World);
        if(Status!=FixedTransformResolveStatus.Success){ Frame=default;return Status; }
        if(!TryResolveFrame(Entity,EcsWorld,out Frame)){
            World=default;
            return FixedTransformResolveStatus.FrameMismatch;
        }
        return FixedTransformResolveStatus.Success;
    }

    private static bool TryResolveFrame(Entity Entity,EcsWorld EcsWorld,out ReferenceFrameId Frame){
        Frame=ReferenceFrameId.Root;
        bool HasFrame=false;
        Entity Current=Entity;
        while(true){
            if(EcsWorld.TryGetComponent(Current,out Transform Transform)){
                if(!HasFrame){ Frame=Transform.Frame;HasFrame=true; }
                else if(Frame!=Transform.Frame){ return false; }
            }
            if(!EcsWorld.TryGetComponent(Current,out Parent Parent)){ return Frame.IsValid; }
            Current=Parent.Value;
        }
    }
}

public delegate bool TryGetLocalTransform(Entity Entity,out Transform Transform);
public delegate bool TryGetParentEntity(Entity Entity,out Entity ParentEntity);
