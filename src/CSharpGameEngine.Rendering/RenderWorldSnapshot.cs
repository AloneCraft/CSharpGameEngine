using System.Numerics;
using CSharpGameEngine.Core;

namespace CSharpGameEngine.Rendering;

public readonly struct RenderMeshId(StableId Value):IEquatable<RenderMeshId>,IComparable<RenderMeshId> {
    public StableId Value{get;}=Value;
    public bool IsValid=>this.Value.IsValid;
    public int CompareTo(RenderMeshId Other)=>this.Value.CompareTo(Other.Value);
    public bool Equals(RenderMeshId Other)=>this.Value==Other.Value;
    public override bool Equals(object?Object)=>Object is RenderMeshId Other&&this.Equals(Other);
    public override int GetHashCode()=>this.Value.GetHashCode();
    public static bool operator ==(RenderMeshId Left,RenderMeshId Right)=>Left.Equals(Right);
    public static bool operator !=(RenderMeshId Left,RenderMeshId Right)=>!Left.Equals(Right);
}

public readonly struct RenderMaterialId(StableId Value):IEquatable<RenderMaterialId>,IComparable<RenderMaterialId> {
    public StableId Value{get;}=Value;
    public bool IsValid=>this.Value.IsValid;
    public int CompareTo(RenderMaterialId Other)=>this.Value.CompareTo(Other.Value);
    public bool Equals(RenderMaterialId Other)=>this.Value==Other.Value;
    public override bool Equals(object?Object)=>Object is RenderMaterialId Other&&this.Equals(Other);
    public override int GetHashCode()=>this.Value.GetHashCode();
    public static bool operator ==(RenderMaterialId Left,RenderMaterialId Right)=>Left.Equals(Right);
    public static bool operator !=(RenderMaterialId Left,RenderMaterialId Right)=>!Left.Equals(Right);
}

public readonly record struct RenderLodLevel(RenderMeshId Mesh,float MaximumDistance);

public readonly record struct RenderInstanceRecord(
    StableId InstanceId,RenderMaterialId Material,
    Matrix4x4 CameraRelativeTransform,Vector3 CameraRelativeBoundsCenter,float BoundsRadius,
    int LodStart,int LodCount);

/// <summary>Immutable, stable-ID ordered rendering input extracted from one completed tick.</summary>
public sealed class RenderWorldSnapshot {
    private static readonly IComparer<RenderInstanceRecord>InstanceComparer=
        Comparer<RenderInstanceRecord>.Create(static(Left,Right)=>Left.InstanceId.CompareTo(Right.InstanceId));
    private readonly RenderInstanceRecord[]InstancesStorage;
    private readonly RenderLodLevel[]LodsStorage;

    private RenderWorldSnapshot(
        ulong Tick,RenderInstanceRecord[]Instances,RenderLodLevel[]Lods){
        this.Tick=Tick;
        this.InstancesStorage=Instances;
        this.LodsStorage=Lods;
    }

    public ulong Tick{get;}
    public ReadOnlySpan<RenderInstanceRecord>Instances=>this.InstancesStorage;
    public ReadOnlySpan<RenderLodLevel>Lods=>this.LodsStorage;

    public static RenderWorldSnapshot Create(
        ulong Tick,ReadOnlySpan<RenderInstanceRecord>Instances,
        ReadOnlySpan<RenderLodLevel>Lods){
        RenderInstanceRecord[]CanonicalInstances=Instances.ToArray();
        RenderLodLevel[]CanonicalLods=Lods.ToArray();
        ValidateLods(CanonicalLods);
        foreach(RenderInstanceRecord Instance in CanonicalInstances){
            ValidateInstance(Instance,CanonicalLods);
        }
        Array.Sort(CanonicalInstances,InstanceComparer);
        for(var Index=1;Index<CanonicalInstances.Length;Index++){
            if(CanonicalInstances[Index-1].InstanceId==CanonicalInstances[Index].InstanceId){
                throw new ArgumentException("Render instance IDs must be unique.",nameof(Instances));
            }
        }
        return new(Tick,CanonicalInstances,CanonicalLods);
    }

    private static void ValidateLods(ReadOnlySpan<RenderLodLevel>Lods){
        foreach(RenderLodLevel Lod in Lods){
            if(!Lod.Mesh.IsValid){ throw new ArgumentException("Every LOD requires a valid mesh ID.",nameof(Lods)); }
            if(!float.IsFinite(Lod.MaximumDistance)||Lod.MaximumDistance<=0f){
                throw new ArgumentOutOfRangeException(nameof(Lods),"LOD distances must be finite and positive.");
            }
        }
    }

    private static void ValidateInstance(
        in RenderInstanceRecord Instance,ReadOnlySpan<RenderLodLevel>Lods){
        if(!Instance.InstanceId.IsValid){ throw new ArgumentException("Every render instance requires a stable ID.",nameof(Instance)); }
        if(!Instance.Material.IsValid){ throw new ArgumentException("Every render instance requires a material ID.",nameof(Instance)); }
        if(!IsFinite(Instance.CameraRelativeTransform)
            ||!IsFinite(Instance.CameraRelativeBoundsCenter)
            ||!float.IsFinite(Instance.BoundsRadius)||Instance.BoundsRadius<0f){
            throw new ArgumentException("Camera-relative transform and bounds must be finite.",nameof(Instance));
        }
        if(Instance.LodStart<0||Instance.LodCount<=0
            ||Instance.LodStart>Lods.Length-Instance.LodCount){
            throw new ArgumentOutOfRangeException(nameof(Instance),"The LOD range is outside the snapshot.");
        }
        float Previous=0f;
        for(var Index=0;Index<Instance.LodCount;Index++){
            float Current=Lods[Instance.LodStart+Index].MaximumDistance;
            if(Index>0&&Current<=Previous){
                throw new ArgumentException("LOD distances must increase strictly within each instance range.",nameof(Instance));
            }
            Previous=Current;
        }
    }

    private static bool IsFinite(Vector3 Value)=>
        float.IsFinite(Value.X)&&float.IsFinite(Value.Y)&&float.IsFinite(Value.Z);

    private static bool IsFinite(Matrix4x4 Value)=>
        float.IsFinite(Value.M11)&&float.IsFinite(Value.M12)&&float.IsFinite(Value.M13)&&float.IsFinite(Value.M14)
        &&float.IsFinite(Value.M21)&&float.IsFinite(Value.M22)&&float.IsFinite(Value.M23)&&float.IsFinite(Value.M24)
        &&float.IsFinite(Value.M31)&&float.IsFinite(Value.M32)&&float.IsFinite(Value.M33)&&float.IsFinite(Value.M34)
        &&float.IsFinite(Value.M41)&&float.IsFinite(Value.M42)&&float.IsFinite(Value.M43)&&float.IsFinite(Value.M44);
}

public readonly struct RenderPlane {
    private RenderPlane(Vector3 Normal,float Distance){
        this.Normal=Normal;
        this.Distance=Distance;
    }
    public Vector3 Normal{get;}
    public float Distance{get;}
    internal bool IsValid=>float.IsFinite(this.Normal.X)
        &&float.IsFinite(this.Normal.Y)&&float.IsFinite(this.Normal.Z)
        &&float.IsFinite(this.Distance)&&this.Normal.LengthSquared()>0f;
    public static RenderPlane Create(Vector3 Normal,float Distance){
        if(!float.IsFinite(Normal.X)||!float.IsFinite(Normal.Y)||!float.IsFinite(Normal.Z)
            ||!float.IsFinite(Distance)){
            throw new ArgumentException("A render plane must be finite.");
        }
        float Length=Normal.Length();
        if(!float.IsFinite(Length)||Length<=0f){
            throw new ArgumentException("A render plane requires a non-zero normal.",nameof(Normal));
        }
        return new(Normal/Length,Distance/Length);
    }
    internal bool ContainsSphere(Vector3 Center,float Radius)=>
        Vector3.Dot(this.Normal,Center)+this.Distance>=-Radius;
}

public readonly struct RenderFrustum(
    RenderPlane Left,RenderPlane Right,RenderPlane Bottom,
    RenderPlane Top,RenderPlane Near,RenderPlane Far) {
    public RenderPlane Left{get;}=Left;
    public RenderPlane Right{get;}=Right;
    public RenderPlane Bottom{get;}=Bottom;
    public RenderPlane Top{get;}=Top;
    public RenderPlane Near{get;}=Near;
    public RenderPlane Far{get;}=Far;

    public static RenderFrustum CreateAxisAligned(Vector3 Min,Vector3 Max){
        if(!float.IsFinite(Min.X)||!float.IsFinite(Min.Y)||!float.IsFinite(Min.Z)
            ||!float.IsFinite(Max.X)||!float.IsFinite(Max.Y)||!float.IsFinite(Max.Z)
            ||Min.X>Max.X||Min.Y>Max.Y||Min.Z>Max.Z){
            throw new ArgumentException("Frustum bounds must be finite and ordered.");
        }
        return new(
            RenderPlane.Create(Vector3.UnitX,-Min.X),
            RenderPlane.Create(-Vector3.UnitX,Max.X),
            RenderPlane.Create(Vector3.UnitY,-Min.Y),
            RenderPlane.Create(-Vector3.UnitY,Max.Y),
            RenderPlane.Create(Vector3.UnitZ,-Min.Z),
            RenderPlane.Create(-Vector3.UnitZ,Max.Z));
    }

    internal bool ContainsSphere(Vector3 Center,float Radius)=>
        this.Left.ContainsSphere(Center,Radius)
        &&this.Right.ContainsSphere(Center,Radius)
        &&this.Bottom.ContainsSphere(Center,Radius)
        &&this.Top.ContainsSphere(Center,Radius)
        &&this.Near.ContainsSphere(Center,Radius)
        &&this.Far.ContainsSphere(Center,Radius);
}

public readonly record struct RenderView(RenderFrustum Frustum,float LodHysteresis);
public readonly record struct RenderLodState(StableId InstanceId,int LodIndex);
public readonly record struct VisibleRenderInstance(
    int SourceIndex,StableId InstanceId,int LodIndex,RenderMeshId Mesh,RenderMaterialId Material);
public readonly record struct RenderVisibilityQueryResult(int VisibleCount,int StateCount);
public readonly record struct RenderInstanceBatch(
    RenderMeshId Mesh,RenderMaterialId Material,int LodIndex,int OrderStart,int InstanceCount);

/// <summary>Stateless visibility/LOD selection and caller-buffer instance batching.</summary>
public static class RenderVisibility {
    public static RenderVisibilityQueryResult Select(
        RenderWorldSnapshot Snapshot,in RenderView View,
        ReadOnlySpan<RenderLodState>PreviousStates,
        Span<RenderLodState>NextStates,
        Span<VisibleRenderInstance>VisibleInstances){
        ArgumentNullException.ThrowIfNull(Snapshot);
        ValidateView(View);
        ValidatePreviousStates(PreviousStates);
        if(NextStates.Overlaps(PreviousStates)){
            throw new ArgumentException("Previous and next LOD state buffers must not overlap.",nameof(NextStates));
        }
        if(NextStates.Length<Snapshot.Instances.Length){
            throw new ArgumentException("The next-state buffer must cover every snapshot instance.",nameof(NextStates));
        }
        if(VisibleInstances.Length<Snapshot.Instances.Length){
            throw new ArgumentException("The visibility buffer must cover every snapshot instance.",nameof(VisibleInstances));
        }

        ReadOnlySpan<RenderInstanceRecord>Instances=Snapshot.Instances;
        ReadOnlySpan<RenderLodLevel>Lods=Snapshot.Lods;
        var PreviousIndex=0;
        var VisibleCount=0;
        for(var SourceIndex=0;SourceIndex<Instances.Length;SourceIndex++){
            RenderInstanceRecord Instance=Instances[SourceIndex];
            while(PreviousIndex<PreviousStates.Length
                &&PreviousStates[PreviousIndex].InstanceId.CompareTo(Instance.InstanceId)<0){
                PreviousIndex++;
            }
            var PriorLod=PreviousIndex<PreviousStates.Length
                &&PreviousStates[PreviousIndex].InstanceId==Instance.InstanceId
                ?PreviousStates[PreviousIndex].LodIndex
                :-1;
            float Distance=DistanceToBounds(Instance.CameraRelativeBoundsCenter,Instance.BoundsRadius);
            int LodIndex=SelectLod(Instance,Lods,Distance,PriorLod,View.LodHysteresis);
            NextStates[SourceIndex]=new(Instance.InstanceId,LodIndex);
            if(View.Frustum.ContainsSphere(
                Instance.CameraRelativeBoundsCenter,Instance.BoundsRadius)){
                RenderLodLevel Lod=Lods[Instance.LodStart+LodIndex];
                VisibleInstances[VisibleCount++]=new(
                    SourceIndex,Instance.InstanceId,LodIndex,Lod.Mesh,Instance.Material);
            }
        }
        return new(VisibleCount,Instances.Length);
    }

    public static int BuildBatches(
        ReadOnlySpan<VisibleRenderInstance>VisibleInstances,
        Span<int>InstanceOrder,Span<RenderInstanceBatch>Batches){
        if(InstanceOrder.Length<VisibleInstances.Length){
            throw new ArgumentException("The order buffer is too small.",nameof(InstanceOrder));
        }
        if(Batches.Length<VisibleInstances.Length){
            throw new ArgumentException("The batch buffer is too small.",nameof(Batches));
        }
        var RequiresSort=false;
        for(var Index=0;Index<VisibleInstances.Length;Index++){
            VisibleRenderInstance Instance=VisibleInstances[Index];
            if(Instance.SourceIndex<0||!Instance.InstanceId.IsValid
                ||Instance.LodIndex<0||!Instance.Mesh.IsValid||!Instance.Material.IsValid){
                throw new ArgumentException("Visible instance data is invalid.",nameof(VisibleInstances));
            }
            InstanceOrder[Index]=Index;
            RequiresSort|=Index>0&&Compare(VisibleInstances,Index-1,Index)>0;
        }
        if(RequiresSort){ HeapSort(VisibleInstances,InstanceOrder.Slice(0,VisibleInstances.Length)); }
        if(VisibleInstances.Length==0){ return 0; }
        var BatchCount=0;
        var Start=0;
        while(Start<VisibleInstances.Length){
            VisibleRenderInstance First=VisibleInstances[InstanceOrder[Start]];
            var End=Start+1;
            while(End<VisibleInstances.Length
                &&SameBatch(First,VisibleInstances[InstanceOrder[End]])){
                End++;
            }
            Batches[BatchCount++]=new(
                First.Mesh,First.Material,First.LodIndex,Start,End-Start);
            Start=End;
        }
        return BatchCount;
    }

    private static void ValidateView(in RenderView View){
        if(!float.IsFinite(View.LodHysteresis)
            ||View.LodHysteresis<0f||View.LodHysteresis>0.5f){
            throw new ArgumentOutOfRangeException(nameof(View),"LOD hysteresis must be between zero and one half.");
        }
        if(!View.Frustum.Left.IsValid||!View.Frustum.Right.IsValid
            ||!View.Frustum.Bottom.IsValid||!View.Frustum.Top.IsValid
            ||!View.Frustum.Near.IsValid||!View.Frustum.Far.IsValid){
            throw new ArgumentException("The render frustum is incomplete or invalid.",nameof(View));
        }
    }

    private static void ValidatePreviousStates(ReadOnlySpan<RenderLodState>States){
        for(var Index=0;Index<States.Length;Index++){
            if(!States[Index].InstanceId.IsValid||States[Index].LodIndex<0){
                throw new ArgumentException("Previous LOD states must contain valid IDs and LOD indices.",nameof(States));
            }
            if(Index>0&&States[Index-1].InstanceId.CompareTo(States[Index].InstanceId)>=0){
                throw new ArgumentException("Previous LOD states must be strictly stable-ID ordered.",nameof(States));
            }
        }
    }

    private static float DistanceToBounds(Vector3 Center,float Radius){
        float CenterDistance=Center.Length();
        if(!float.IsFinite(CenterDistance)){ return float.MaxValue; }
        return MathF.Max(0f,CenterDistance-Radius);
    }

    private static int SelectLod(
        in RenderInstanceRecord Instance,ReadOnlySpan<RenderLodLevel>Lods,
        float Distance,int PriorLod,float Hysteresis){
        var Candidate=Instance.LodCount-1;
        for(var Index=0;Index<Instance.LodCount;Index++){
            if(Distance<=Lods[Instance.LodStart+Index].MaximumDistance){
                Candidate=Index;
                break;
            }
        }
        if(PriorLod<0||PriorLod>=Instance.LodCount||PriorLod==Candidate){
            return Candidate;
        }
        if(Candidate>PriorLod){
            float Exit=Lods[Instance.LodStart+PriorLod].MaximumDistance*(1f+Hysteresis);
            return Distance<=Exit?PriorLod:Candidate;
        }
        float Enter=Lods[Instance.LodStart+Candidate].MaximumDistance*(1f-Hysteresis);
        return Distance>=Enter?PriorLod:Candidate;
    }

    private static bool SameBatch(
        in VisibleRenderInstance Left,in VisibleRenderInstance Right)=>
        Left.Mesh==Right.Mesh&&Left.Material==Right.Material&&Left.LodIndex==Right.LodIndex;

    private static int Compare(
        ReadOnlySpan<VisibleRenderInstance>Visible,int LeftIndex,int RightIndex){
        VisibleRenderInstance Left=Visible[LeftIndex];
        VisibleRenderInstance Right=Visible[RightIndex];
        var Result=Left.Mesh.CompareTo(Right.Mesh);
        if(Result!=0){ return Result; }
        Result=Left.Material.CompareTo(Right.Material);
        if(Result!=0){ return Result; }
        Result=Left.LodIndex.CompareTo(Right.LodIndex);
        return Result!=0?Result:Left.InstanceId.CompareTo(Right.InstanceId);
    }

    private static void HeapSort(
        ReadOnlySpan<VisibleRenderInstance>Visible,Span<int>Order){
        for(var Start=Order.Length/2-1;Start>=0;Start--){ SiftDown(Visible,Order,Start,Order.Length); }
        for(var End=Order.Length-1;End>0;End--){
            (Order[0],Order[End])=(Order[End],Order[0]);
            SiftDown(Visible,Order,0,End);
        }
    }

    private static void SiftDown(
        ReadOnlySpan<VisibleRenderInstance>Visible,Span<int>Order,int Root,int Count){
        while(true){
            var Child=checked(Root*2+1);
            if(Child>=Count){ return; }
            if(Child+1<Count&&Compare(Visible,Order[Child],Order[Child+1])<0){ Child++; }
            if(Compare(Visible,Order[Root],Order[Child])>=0){ return; }
            (Order[Root],Order[Child])=(Order[Child],Order[Root]);
            Root=Child;
        }
    }
}
