using System;
using System.Collections.Generic;
using CSharpGameEngine.Runtime.Windows;
namespace CSharpGameEngine.Entities;
public enum SpatialSyncFailure {
    InvalidBounds,
    MissingTransform,
    DeadParent,
    Cycle,
    NonInvertible,
    NumericOverflow,
}
public readonly record struct GlobalSpatialSyncReport(int ScannedCount,int IndexedCount,int ExcludedCount,int InvalidBoundsCount,int MissingTransformCount,int DeadParentCount,int CycleCount,int NumericOverflowCount);
public partial struct World {
    private sealed class GlobalSpatialState {
        internal readonly EntityLooseOctree Index=new();
        internal readonly FixedTransformWorldResolver Resolver=new();
        internal readonly HashSet<Entity>Seen=[];
        internal readonly Dictionary<Entity,SpatialSyncFailure>Failures=[];
        internal readonly Guid DebugWorldId=Guid.NewGuid();
        internal GlobalSpatialSyncReport Report;
    }
    private readonly GlobalSpatialState SpatialState=CreateSpatialState();
    private static GlobalSpatialState CreateSpatialState(){
        SpatialDebugHub.EnsureStarted();
        return new GlobalSpatialState();
    }
    public IReadOnlyEntitySpatialIndex GlobalSpatialIndex=>this.SpatialState.Index;
    public GlobalSpatialSyncReport LastGlobalSpatialSync=>this.SpatialState.Report;
    public bool TryGetGlobalSpatialFailure(Entity Entity,out SpatialSyncFailure Failure)=>this.SpatialState.Failures.TryGetValue(Entity,out Failure);
    /// <summary>Rebuilds the current World snapshot immediately. Calls are single-writer only.</summary>
    public void RefreshGlobalSpatialIndex(){
        this.SpatialState.Seen.Clear();
        this.SpatialState.Resolver.BeginFrame();
        this.SpatialState.Report=default;
        this.SynchronizeGlobalSpatialIndexCore();
        this.SpatialState.Index.RemoveAllExcept(this.SpatialState.Seen);
        List<Entity>ResolvedFailures=[];
        foreach(Entity Entity in this.SpatialState.Failures.Keys){
            if(!this.SpatialState.Seen.Contains(Entity)){
                ResolvedFailures.Add(Entity);
            }
        }
        foreach(Entity Entity in ResolvedFailures){
            this.SpatialState.Failures.Remove(Entity);
        }
        SpatialDebugHub.Publish(this.SpatialState.DebugWorldId,this.SpatialState.Index);
    }
    public void CompactGlobalSpatialIndex(){
        this.SpatialState.Index.Compact();
    }
    partial void SynchronizeGlobalSpatialIndexCore();
    private void SynchronizeGlobalSpatialEntity(Entity Entity,SpatialBounds Bounds,TryGetLocalTransform GetTransform,TryGetParentEntity GetParent){
        this.SpatialState.Seen.Add(Entity);
        GlobalSpatialSyncReport Report=this.SpatialState.Report;
        Report=Report with { ScannedCount=Report.ScannedCount+1 };
        if(!SpatialAabb.TryCreate(Bounds.Center,Bounds.HalfExtents,out SpatialAabb LocalBounds)){
            this.ExcludeGlobalSpatialEntity(Entity,SpatialSyncFailure.InvalidBounds,Report);
            return;
        }
        FixedTransformResolveStatus ResolveStatus=this.SpatialState.Resolver.Resolve(Entity.Id,GetTransform,GetParent,out FixedAffineTransform WorldTransform);
        if(ResolveStatus!=FixedTransformResolveStatus.Success){
            this.ExcludeGlobalSpatialEntity(Entity,ToSpatialFailure(ResolveStatus),Report);
            return;
        }
        if(!TryTransformBounds(WorldTransform,LocalBounds,out SpatialAabb WorldBounds)){
            this.ExcludeGlobalSpatialEntity(Entity,SpatialSyncFailure.NumericOverflow,Report);
            return;
        }
        SpatialUpdateStatus UpdateStatus=this.SpatialState.Index.Upsert(Entity,WorldBounds,Bounds.Layer);
        if(UpdateStatus!=SpatialUpdateStatus.Success){
            this.ExcludeGlobalSpatialEntity(Entity,SpatialSyncFailure.NumericOverflow,Report);
            return;
        }
        this.SpatialState.Failures.Remove(Entity);
        this.SpatialState.Report=Report with { IndexedCount=Report.IndexedCount+1 };
    }
    private void ExcludeGlobalSpatialEntity(Entity Entity,SpatialSyncFailure Failure,GlobalSpatialSyncReport Report){
        this.SpatialState.Index.Remove(Entity);
        this.SpatialState.Failures[Entity]=Failure;
        this.SpatialState.Report=Failure switch {
            SpatialSyncFailure.InvalidBounds=>Report with { ExcludedCount=Report.ExcludedCount+1,InvalidBoundsCount=Report.InvalidBoundsCount+1 },
            SpatialSyncFailure.MissingTransform=>Report with { ExcludedCount=Report.ExcludedCount+1,MissingTransformCount=Report.MissingTransformCount+1 },
            SpatialSyncFailure.DeadParent=>Report with { ExcludedCount=Report.ExcludedCount+1,DeadParentCount=Report.DeadParentCount+1 },
            SpatialSyncFailure.Cycle=>Report with { ExcludedCount=Report.ExcludedCount+1,CycleCount=Report.CycleCount+1 },
            _=>Report with { ExcludedCount=Report.ExcludedCount+1,NumericOverflowCount=Report.NumericOverflowCount+1 },
        };
    }
    private static SpatialSyncFailure ToSpatialFailure(FixedTransformResolveStatus Status)=>Status switch {
        FixedTransformResolveStatus.MissingTransform=>SpatialSyncFailure.MissingTransform,
        FixedTransformResolveStatus.DeadParent=>SpatialSyncFailure.DeadParent,
        FixedTransformResolveStatus.Cycle=>SpatialSyncFailure.Cycle,
        FixedTransformResolveStatus.NonInvertible=>SpatialSyncFailure.NonInvertible,
        _=>SpatialSyncFailure.NumericOverflow,
    };
    private static bool TryTransformBounds(in FixedAffineTransform Transform,in SpatialAabb Bounds,out SpatialAabb Result){
        Result=default;
        Logical.Vector3 Min=default;
        Logical.Vector3 Max=default;
        bool HasPoint=false;
        for(int Index=0;Index<8;Index++){
            Logical.Vector3 Point=new(
                (Index&1)==0?Bounds.Min.R:Bounds.Max.R,
                (Index&2)==0?Bounds.Min.U:Bounds.Max.U,
                (Index&4)==0?Bounds.Min.F:Bounds.Max.F);
            if(!TryTransformPoint(Transform,Point,out Logical.Vector3 Transformed)){
                return false;
            }
            if(!HasPoint){
                Min=Max=Transformed;
                HasPoint=true;
                continue;
            }
            Min=new Logical.Vector3(Min.R<Transformed.R?Min.R:Transformed.R,Min.U<Transformed.U?Min.U:Transformed.U,Min.F<Transformed.F?Min.F:Transformed.F);
            Max=new Logical.Vector3(Max.R>Transformed.R?Max.R:Transformed.R,Max.U>Transformed.U?Max.U:Transformed.U,Max.F>Transformed.F?Max.F:Transformed.F);
        }
        Result=new SpatialAabb(Min,Max);
        return true;
    }
    private static bool TryTransformPoint(in FixedAffineTransform Transform,Logical.Vector3 Point,out Logical.Vector3 Result){
        Result=default;
        if(!TryDot(Point.R,Point.U,Point.F,Transform.M11,Transform.M21,Transform.M31,out Logical.Scalar R)
            ||!Logical.Scalar.TryAdd(R,Transform.M41,out R)
            ||!TryDot(Point.R,Point.U,Point.F,Transform.M12,Transform.M22,Transform.M32,out Logical.Scalar U)
            ||!Logical.Scalar.TryAdd(U,Transform.M42,out U)
            ||!TryDot(Point.R,Point.U,Point.F,Transform.M13,Transform.M23,Transform.M33,out Logical.Scalar F)
            ||!Logical.Scalar.TryAdd(F,Transform.M43,out F)){
            return false;
        }
        Result=new Logical.Vector3(R,U,F);
        return true;
    }
    private static bool TryDot(Logical.Scalar LeftR,Logical.Scalar LeftU,Logical.Scalar LeftF,Logical.Scalar RightR,Logical.Scalar RightU,Logical.Scalar RightF,out Logical.Scalar Result){
        Result=default;
        if(!Logical.Scalar.TryMultiply(LeftR,RightR,out Logical.Scalar ProductR)
            ||!Logical.Scalar.TryMultiply(LeftU,RightU,out Logical.Scalar ProductU)
            ||!Logical.Scalar.TryMultiply(LeftF,RightF,out Logical.Scalar ProductF)
            ||!Logical.Scalar.TryAdd(ProductR,ProductU,out Logical.Scalar Sum)
            ||!Logical.Scalar.TryAdd(Sum,ProductF,out Result)){
            return false;
        }
        return true;
    }
}
