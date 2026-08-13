using System;
using System.Collections.Generic;
using System.Numerics;

namespace CSharpGameEngine.Runtime.Windows;

/// <summary>??Transform????????cycle?????????????????????????</summary>
public enum TransformResolveStatus {
    Success,
    MissingTransform,
    DeadParent,
    Cycle,
}

/// <summary>World????Component?????????reflection?????delegate?????</summary>
public delegate bool TryGetLocalTransform(int EntityId,out Transform Transform);
public delegate bool TryGetParentEntity(int EntityId,out int ParentEntityId);

/// <summary>
/// Local Transform???????????????????cache???world = local * parentWorld ???
/// ????Entity.Id????stack???????????frame?????<see cref="BeginFrame"/>??????
/// </summary>
public sealed class TransformWorldResolver {
    private readonly Dictionary<int,Matrix4x4>WorldByEntity=[];
    private readonly Dictionary<int,TransformResolveStatus>StatusByEntity=[];
    private readonly HashSet<int>Resolving=[];

    public void BeginFrame(){
        this.WorldByEntity.Clear();
        this.StatusByEntity.Clear();
        this.Resolving.Clear();
    }

    public TransformResolveStatus Resolve(
        int EntityId,
        TryGetLocalTransform GetTransform,
        TryGetParentEntity GetParent,
        out Matrix4x4 World){
        if(this.WorldByEntity.TryGetValue(EntityId,out World)){
            return TransformResolveStatus.Success;
        }

        if(this.StatusByEntity.TryGetValue(EntityId,out TransformResolveStatus PreviousStatus)){
            World=default;
            return PreviousStatus;
        }

        if(!this.Resolving.Add(EntityId)){
            World=default;
            return this.StatusByEntity[EntityId]=TransformResolveStatus.Cycle;
        }

        try {
            if(!GetTransform(EntityId,out Transform Local)){
                World=default;
                return this.StatusByEntity[EntityId]=TransformResolveStatus.MissingTransform;
            }

            Matrix4x4 LocalWorld=TransformMath.ToMatrix(Local);
            if(!GetParent(EntityId,out int ParentEntityId)){
                World=LocalWorld;
                this.WorldByEntity.Add(EntityId,World);
                return TransformResolveStatus.Success;
            }

            TransformResolveStatus ParentStatus=this.Resolve(ParentEntityId,GetTransform,GetParent,out Matrix4x4 ParentWorld);
            if(ParentStatus!=TransformResolveStatus.Success){
                World=default;
                return this.StatusByEntity[EntityId]=ParentStatus==TransformResolveStatus.MissingTransform
                    ?TransformResolveStatus.DeadParent
                    :ParentStatus;
            }

            World=TransformMath.ToWorld(Local,ParentWorld);
            this.WorldByEntity.Add(EntityId,World);
            return TransformResolveStatus.Success;
        } finally {
            this.Resolving.Remove(EntityId);
        }
    }
}
