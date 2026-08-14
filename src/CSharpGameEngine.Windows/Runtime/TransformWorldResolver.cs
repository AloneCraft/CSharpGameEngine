using System;
using System.Collections.Generic;
using System.Numerics;

namespace CSharpGameEngine.Runtime.Windows;

/// <summary>親Transform解決の結果。cycleや親の欠落を呼び出し側が区別して扱えるようにする。</summary>
public enum TransformResolveStatus {
    Success,
    MissingTransform,
    DeadParent,
    Cycle,
}

/// <summary>WorldからComponentを引くためのアクセサ。reflectionを使わずdelegateで注入する。</summary>
public delegate bool TryGetLocalTransform(int EntityId,out Transform Transform);
public delegate bool TryGetParentEntity(int EntityId,out int ParentEntityId);

/// <summary>
/// Local Transformを親へ遡って合成し、結果をcacheしながら world = local * parentWorld を解決する。
/// 再帰はEntity.Idの訪問集合でcycleを検出し、frameごとに<see cref="BeginFrame"/>でcacheを破棄する。
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
