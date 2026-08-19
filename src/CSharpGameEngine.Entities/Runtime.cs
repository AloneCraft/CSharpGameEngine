using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CSharpGameEngine.Entities;

public interface ISystem {
}

public interface IArchetype {
}

public record struct Entity(int Id);

public partial struct State(World World){
    public bool Enabled { get; set;}

    public World World { get;}=World;
}

[StructLayout(LayoutKind.Auto)]
public partial struct World {
    /// <summary>
    /// Entityの位置表。行番号がそのまま<see cref="Entity.Id"/>であり、<b>ここではswap-backしない</b>
    /// (swap-backするのはArchetypeのSoA列の側で、その結果は<c>Index</c>の更新として反映される)。
    /// <c>SceneSlot</c>はこのEntityを生成した<see cref="SceneInstance"/>のスロット番号で、
    /// どのSceneにも属さないEntityは-1(ADR-0040)。<b>Component構成に一切影響しない</b>のが要点で、
    /// 所有権をComponentとして持たせるとあらゆる構成が「Scene所属版/非所属版」の2倍に増えてしまう。
    /// </summary>
    internal readonly ChunkBuffer<(Entity Entity,Archetype Archetype,int Index,int SceneSlot)>EntityArchetypeIndices=new();

    internal readonly SceneInstanceTable SceneInstances=new();

    /// <summary>
    /// System実行中に積まれた破棄要求。<see cref="ChunkList{T}"/>のrefを無効化しないよう、
    /// 適用はフレーム末尾の<c>PlaybackStructuralChanges</c>まで遅延する。
    /// </summary>
    internal readonly ChunkBuffer<Entity>DestroyCommands=new();

    internal readonly ChunkBuffer<SceneInstance>SceneUnloadCommands=new();

    /// <summary>
    /// 生きているEntityかどうか。破棄されたEntityの行には<see cref="Archetype"/>の墓石が書かれ、
    /// そのIdは<b>二度と再配布されない</b>ため、古いハンドルが別のEntityに化けることは構造的に起こらない。
    /// </summary>
    public bool IsAlive(Entity Entity){
        if((uint)Entity.Id>=(uint)this.EntityArchetypeIndices.Count){
            return false;
        }

        return this.EntityArchetypeIndices.GetRefUnchecked(Entity.Id).Archetype!=Archetype.None;
    }

    /// <summary>System実行中でも安全な破棄。実際の破棄はフレーム末尾に適用される。</summary>
    public bool DestroyDeferred(Entity Entity,ref State State){
        this.DestroyCommands.Add(Entity);
        return true;
    }

    /// <summary>新しいSceneインスタンスを確保する。生成された<c>Instantiate</c>だけが呼ぶ。</summary>
    public SceneInstance BeginSceneInstance(){
        return this.SceneInstances.Allocate();
    }

    /// <summary>
    /// 生成済みのEntityをSceneへ帰属させ、<b>そのまま返す</b>(生成コードが
    /// <c>World.AttachToScene(Instance, ArchetypeNN.Create(...))</c>と入れ子で書けるようにするため)。
    /// ゲームプレイが実行時に作ったEntityを後からSceneへ加えることもできる。
    /// </summary>
    public Entity AttachToScene(SceneInstance Instance,Entity Entity){
        if(this.SceneInstances.Resolve(Instance) is not { } Slot||!this.IsAlive(Entity)){
            return Entity;
        }

        this.EntityArchetypeIndices.GetRefUnchecked(Entity.Id).SceneSlot=Instance.Slot;
        Slot.Entities.Add(Entity);
        return Entity;
    }

    public void EndSceneInstance(SceneInstance Instance,Entity Root){
        if(this.SceneInstances.Resolve(Instance) is { } Slot){
            Slot.Root=Root;
        }
    }

    public bool IsSceneAlive(SceneInstance Instance){
        return this.SceneInstances.Resolve(Instance) is not null;
    }

    public Entity GetSceneRoot(SceneInstance Instance){
        return this.SceneInstances.Resolve(Instance) is { } Slot?Slot.Root:default;
    }

    /// <summary>このEntityを生成したScene。Scene直属でなければ<see cref="SceneInstance.None"/>。</summary>
    public SceneInstance GetOwningScene(Entity Entity){
        if(!this.IsAlive(Entity)){
            return SceneInstance.None;
        }

        return this.SceneInstances.ToInstance(this.EntityArchetypeIndices.GetRefUnchecked(Entity.Id).SceneSlot);
    }

    /// <summary>
    /// このSceneが生成したEntityだけを破棄する。<b>System実行中に呼んではならない</b> —
    /// swap-backが現在の<c>ref</c>を無効化するため。System内からは
    /// <see cref="UnloadSceneDeferred"/>を使うこと(実行中かどうかを知る手段が無いのでアサートは置けない)。
    /// </summary>
    public void UnloadScene(SceneInstance Instance){
        if(this.SceneInstances.Resolve(Instance) is not { } Slot){
            return;
        }

        for(var Index=Slot.Entities.Count-1;Index>=0;Index--){
            Entity Owned=Slot.Entities.GetRefUnchecked(Index);

            // 所有権は位置表のSceneSlotが正であり、この一覧は追記のみの記録にすぎない。別のSceneへ
            // 付け替えられたEntityは古い側の一覧にも残るため、現在の所有者が自分であることを確認してから
            // 破棄する(この確認が無いと、付け替え元のUnloadで新しいSceneのEntityを巻き添えに壊す)。
            if(this.IsAlive(Owned)&&this.EntityArchetypeIndices.GetRefUnchecked(Owned.Id).SceneSlot==Instance.Slot){
                this.Destroy(Owned);
            }
        }

        this.SceneInstances.Free(Instance);
    }

    public bool UnloadSceneDeferred(SceneInstance Instance,ref State State){
        this.SceneUnloadCommands.Add(Instance);
        return true;
    }

    /// <summary>
    /// Scene定義からRuntimeのEntity群を構築する唯一の公開API。
    /// <c>new MainScene()</c>は行われず(SGが生成partialへprivateコンストラクターを出す)、
    /// Scene DSL自体も実行されない — 生成された<c>Instantiate</c>が
    /// <c>ArchetypeNN.Create</c>を直接呼ぶだけである。
    /// </summary>
    public SceneInstance InstantiateScene<TScene>()where TScene:IScene,ISceneDefinition{
        return TScene.Instantiate(ref this);
    }

    /// <summary>
    /// 遅延された破棄要求を適用する。生成された<c>PlaybackStructuralChanges</c>が、Archetype移動の
    /// 適用を終えたあとに呼ぶ(移動してから破棄したEntityへ、破棄後の墓石行に対して移動を適用して
    /// しまわないようにするため)。<see cref="Destroy"/>は冪等なので二重破棄も安全。
    /// </summary>
    internal void PlaybackSceneCommands(){
        for(var Index=0;Index<this.DestroyCommands.Count;Index++){
            this.Destroy(this.DestroyCommands.GetRefUnchecked(Index));
        }

        this.DestroyCommands.Clear();

        for(var Index=0;Index<this.SceneUnloadCommands.Count;Index++){
            this.UnloadScene(this.SceneUnloadCommands.GetRefUnchecked(Index));
        }

        this.SceneUnloadCommands.Clear();
    }
}

/// <summary>SoA列の物理ストレージ。論理長は所有者が管理する。</summary>
public sealed class ChunkList<T>{
    private const int ChunkShift=12;
    private const int ChunkSize=1<<ChunkShift;
    private const int ChunkMask=ChunkSize-1;

    private T[][]Chunks=[];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void EnsureCapacity(int RequiredCount){
        Debug.Assert(RequiredCount>=0,"CGE-ECS-UNCHECKED-001: requiredCount must be non-negative; remove this assertion after hot-path validation.");

        if(RequiredCount==0){
            return;
        }

        var ChunkIndex=(RequiredCount-1) >> ChunkShift;

        if(ChunkIndex>=this.Chunks.Length){
            var Length=this.Chunks.Length==0?1:this.Chunks.Length;
            while(Length<=ChunkIndex){
                Length<<=1;
            }

            Array.Resize(ref this.Chunks,Length);
        }

        if(this.Chunks[ChunkIndex]is null){
            this.Chunks[ChunkIndex]=new T[ChunkSize];
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ref T GetRefUnchecked(int Index){
        return ref this.Chunks[Index >> ChunkShift][Index&ChunkMask];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void ClearAtUnchecked(int Index){
        if(RuntimeHelpers.IsReferenceOrContainsReferences<T>()){
            this.GetRefUnchecked(Index)=default!;
        }
    }
}

/// <summary>位置表・構造変更キュー用の論理長を持つ内部バッファ。</summary>
internal sealed class ChunkBuffer<T>{
    private readonly ChunkList<T>Items=new();

    internal int Count { get;private set;}

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Add(T Item){
        var Index=this.Count;
        this.Items.EnsureCapacity(Index+1);
        this.Items.GetRefUnchecked(Index)=Item;
        this.Count=Index+1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ref T GetRefUnchecked(int Index){
        Debug.Assert((uint)Index<(uint)this.Count,"CGE-ECS-UNCHECKED-002: buffer index must be valid; remove this assertion after hot-path validation.");
        return ref this.Items.GetRefUnchecked(Index);
    }

    internal void Clear(){
        if(RuntimeHelpers.IsReferenceOrContainsReferences<T>()){
            for(var Index=0;Index<this.Count;Index++){
                this.Items.ClearAtUnchecked(Index);
            }
        }

        this.Count=0;
    }
}
