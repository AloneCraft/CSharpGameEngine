using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.Entities;

public interface ISystem {
}

/// <summary>Controls which execution host owns a <see cref="State"/> instance.</summary>
public enum EngineExecutionMode {
    Runtime,
    EditorPreview,
}

public interface IArchetype {
}

public readonly record struct Entity(int Index,uint Generation) {
    public Entity(int Index):this(Index,1){}
    public int Id=>this.Index;
}

/// <summary>A caller-visible token for generated component-change queries.</summary>
public readonly record struct ChangeVersion(uint Value) {
    public bool IsNewerThan(ChangeVersion Baseline){
        var Difference=unchecked(this.Value-Baseline.Value);
        return Difference!=0&&Difference<0x80000000u;
    }
}

public readonly record struct WorldStatistics(int AliveEntityCount,int EntitySlotCount,int PendingDestroyCount,int PendingSceneUnloadCount,int PendingCellUnloadCount=0);

internal enum EntityCommandKind:byte {
    Create,
    Move,
    Destroy,
    SceneBegin,
    SceneAttach,
    SceneEnd,
    SceneUnload,
    CellUnload,
}

internal readonly record struct EntityCommand(EntityCommandKind Kind,int MoveIndex,Entity Entity,SceneInstance Scene,WorldCellId Cell);

/// <summary>The single ordered structural-command stream for a world.</summary>
public sealed class EntityCommandBuffer {
    private readonly ChunkBuffer<EntityCommand>Items=new();
    private int PendingDestroyCount;
    private int PendingSceneUnloadCount;
    private int PendingCellUnloadCount;
    private int SystemExecutionDepth;
    internal int Count=>this.Items.Count;
    internal bool IsSystemExecuting=>this.SystemExecutionDepth>0;
    internal int Destroys=>this.PendingDestroyCount;
    internal int SceneUnloads=>this.PendingSceneUnloadCount;
    internal int CellUnloads=>this.PendingCellUnloadCount;
    public void Destroy(Entity Entity){
        this.Items.Add(new(EntityCommandKind.Destroy,-1,Entity,default,default));
        this.PendingDestroyCount=checked(this.PendingDestroyCount+1);
    }
    public void UnloadScene(SceneInstance Scene){
        this.Items.Add(new(EntityCommandKind.SceneUnload,-1,default,Scene,default));
        this.PendingSceneUnloadCount=checked(this.PendingSceneUnloadCount+1);
    }
    public void UnloadCell(WorldCellId Cell){
        if(!Cell.IsValid){ throw new ArgumentException("A valid world-cell ID is required.",nameof(Cell)); }
        this.Items.Add(new(EntityCommandKind.CellUnload,-1,default,default,Cell));
        this.PendingCellUnloadCount=checked(this.PendingCellUnloadCount+1);
    }
    internal void Create(int MoveIndex)=>this.Items.Add(new(EntityCommandKind.Create,MoveIndex,default,default,default));
    internal void Move(int MoveIndex)=>this.Items.Add(new(EntityCommandKind.Move,MoveIndex,default,default,default));
    internal void BeginScene(SceneInstance Scene)=>this.Items.Add(new(EntityCommandKind.SceneBegin,-1,default,Scene,default));
    internal void AttachScene(SceneInstance Scene,Entity Entity)=>this.Items.Add(new(EntityCommandKind.SceneAttach,-1,Entity,Scene,default));
    internal void EndScene(SceneInstance Scene,Entity Root)=>this.Items.Add(new(EntityCommandKind.SceneEnd,-1,Root,Scene,default));
    internal ref EntityCommand GetRefUnchecked(int Index)=>ref this.Items.GetRefUnchecked(Index);
    internal void BeginSystemExecution()=>this.SystemExecutionDepth=checked(this.SystemExecutionDepth+1);
    internal void EndSystemExecution(){
        if(this.SystemExecutionDepth<=0){ throw new InvalidOperationException("System execution is not active."); }
        this.SystemExecutionDepth--;
    }
    internal void CompletePlayback(){
        this.Items.Clear();
        this.PendingDestroyCount=0;
        this.PendingSceneUnloadCount=0;
        this.PendingCellUnloadCount=0;
    }
}

internal struct EntityLocation(Entity Entity,Archetype Archetype,int Index) {
    internal Entity Entity=Entity;
    internal Archetype Archetype=Archetype;
    internal int Index=Index;
    internal int SceneSlot=-1;
    internal WorldCellId Cell;
    internal PersistentEntityId Persistent;
    internal bool Reserved;
}

internal sealed class WorldRuntimeState {
    internal int AliveEntityCount;
    internal uint ChangeVersion;
}

/// <summary>Owns one ECS World storage location so copied State values never fork mutable World state.</summary>
public sealed class WorldOwner:IDisposable {
    private World Value;
    private bool Disposed;
    public WorldOwner(){ this.Value=new World(); }
    public WorldOwner(World World){ this.Value=World; }
    public ref World World {
        get {
            ObjectDisposedException.ThrowIf(this.Disposed,this);
            return ref this.Value;
        }
    }
    public State CreateState()=>new(this);
    public void Dispose(){
        if(this.Disposed){ return; }
        this.Disposed=true;
        this.Value=default;
    }
}

public partial struct State(World World){
    private readonly WorldOwner Owner=new(World);
    public bool Enabled { get; set;}
    public SimulationTiming Timing { get;internal set; }=SimulationTiming.Default;
    public SimulationPlaybackState PlaybackState { get;private set; }=SimulationPlaybackState.Running;
    public float InterpolationAlpha { get;internal set; }
    private int PendingSimulationSteps;
    public ulong FixedTick=>this.Timing.Tick;
    public int FixedTickRate=>this.Timing.TickRate;
    public SystemPhase Phase=>this.Timing.Phase;

    public void SetSimulationTiming(ulong Tick,int TickRate,SystemPhase Phase){
        if(TickRate<=0){ throw new ArgumentOutOfRangeException(nameof(TickRate)); }
        this.Timing=new SimulationTiming(Tick,TickRate,Phase);
    }
    public void PauseSimulation(){
        this.PlaybackState=SimulationPlaybackState.Paused;
        this.PendingSimulationSteps=0;
    }
    public void ResumeSimulation(){
        this.PlaybackState=SimulationPlaybackState.Running;
        this.PendingSimulationSteps=0;
    }
    public void StepSimulation(){
        this.PlaybackState=SimulationPlaybackState.Paused;
        this.PendingSimulationSteps=checked(this.PendingSimulationSteps+1);
    }
    internal bool TryConsumeSimulationStep(){
        if(this.PlaybackState!=SimulationPlaybackState.Paused||this.PendingSimulationSteps<=0){
            return false;
        }
        this.PendingSimulationSteps--;
        return true;
    }
    /// <summary>Gets or sets the host mode. Runtime is the safe default for user-created states.</summary>
    public EngineExecutionMode ExecutionMode { get; set; }

    public bool IsEditorPreview => this.ExecutionMode==EngineExecutionMode.EditorPreview;

    internal State(WorldOwner Owner):this(default(World)){
        this.Owner=Owner??throw new ArgumentNullException(nameof(Owner));
        this.Timing=SimulationTiming.Default;
    }

    public ref World World=>ref this.Owner.World;
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
    internal readonly ChunkBuffer<EntityLocation>EntityArchetypeIndices=new();
    internal readonly ChunkBuffer<int>FreeEntityIndices=new();
    private readonly WorldRuntimeState RuntimeState=new();

    internal readonly SceneInstanceTable SceneInstances=new();
    private readonly Dictionary<WorldCellId,ChunkBuffer<Entity>>CellEntities=new();
    private readonly Dictionary<PersistentEntityId,Entity>PersistentEntities=new();
    public EntityCommandBuffer Commands{get;}=new();

    /// <summary>
    /// System実行中に積まれた破棄要求。<see cref="ChunkList{T}"/>のrefを無効化しないよう、
    /// 適用はフレーム末尾の<c>PlaybackStructuralChanges</c>まで遅延する。
    /// </summary>
    /// <summary>
    /// 生きているEntityかどうか。破棄されたEntityの行には<see cref="Archetype"/>の墓石が書かれ、
    /// slot再利用時にはGenerationが進むため、古いハンドルが別のEntityに化けることは構造的に起こらない。
    /// </summary>
    public bool IsAlive(Entity Entity){
        if((uint)Entity.Index>=(uint)this.EntityArchetypeIndices.Count||Entity.Generation==0){
            return false;
        }

        ref var Row=ref this.EntityArchetypeIndices.GetRefUnchecked(Entity.Index);
        return Row.Archetype!=Archetype.None&&Row.Entity==Entity;
    }

    internal bool IsReserved(Entity Entity){
        if((uint)Entity.Index>=(uint)this.EntityArchetypeIndices.Count||Entity.Generation==0){ return false; }
        ref var Row=ref this.EntityArchetypeIndices.GetRefUnchecked(Entity.Index);
        return Row.Reserved&&Row.Entity==Entity;
    }

    /// <summary>Returns an O(1) read-only diagnostics snapshot.</summary>
    public WorldStatistics GetStatistics()=>new(this.RuntimeState.AliveEntityCount,this.EntityArchetypeIndices.Count,this.Commands.Destroys,this.Commands.SceneUnloads,this.Commands.CellUnloads);

    /// <summary>Captures the completed component-write boundary for a later generated changed query.</summary>
    public ChangeVersion CaptureChangeVersion()=>new(this.RuntimeState.ChangeVersion);

    internal ChangeVersion AdvanceChangeVersion(){
        this.RuntimeState.ChangeVersion=unchecked(this.RuntimeState.ChangeVersion+1);
        if(this.RuntimeState.ChangeVersion==0){ this.RuntimeState.ChangeVersion=1; }
        return new ChangeVersion(this.RuntimeState.ChangeVersion);
    }

    internal Entity AllocateEntity(Archetype Archetype,int ArchetypeIndex){
        this.AssertDirectStructuralMutationAllowed();
        Entity Entity;
        if(this.FreeEntityIndices.TryRemoveLast(out var SlotIndex)){
            ref var Row=ref this.EntityArchetypeIndices.GetRefUnchecked(SlotIndex);
            Entity=Row.Entity;
            Row=new EntityLocation(Entity,Archetype,ArchetypeIndex);
        } else {
            Entity=new Entity(this.EntityArchetypeIndices.Count,1);
            this.EntityArchetypeIndices.Add(new EntityLocation(Entity,Archetype,ArchetypeIndex));
        }
        this.RuntimeState.AliveEntityCount=checked(this.RuntimeState.AliveEntityCount+1);
        return Entity;
    }

    internal Entity ReserveEntity(){
        Entity Entity;
        if(this.FreeEntityIndices.TryRemoveLast(out var SlotIndex)){
            ref var Row=ref this.EntityArchetypeIndices.GetRefUnchecked(SlotIndex);
            Entity=Row.Entity;
            Row=new EntityLocation(Entity,Archetype.None,-1){ Reserved=true };
        } else {
            Entity=new Entity(this.EntityArchetypeIndices.Count,1);
            this.EntityArchetypeIndices.Add(new EntityLocation(Entity,Archetype.None,-1){ Reserved=true });
        }
        return Entity;
    }

    internal bool ActivateReservedEntity(Entity Entity,Archetype Archetype,int ArchetypeIndex){
        if(!this.IsReserved(Entity)){ return false; }
        ref var Row=ref this.EntityArchetypeIndices.GetRefUnchecked(Entity.Index);
        Row.Archetype=Archetype;
        Row.Index=ArchetypeIndex;
        Row.Reserved=false;
        this.RuntimeState.AliveEntityCount=checked(this.RuntimeState.AliveEntityCount+1);
        return true;
    }

    internal void ReleaseEntity(Entity Entity){
        ref var Row=ref this.EntityArchetypeIndices.GetRefUnchecked(Entity.Index);
        if(Row.Persistent.IsValid&&this.PersistentEntities.TryGetValue(Row.Persistent,out var Registered)&&Registered==Entity){
            this.PersistentEntities.Remove(Row.Persistent);
        }
        var NextGeneration=Entity.Generation+1;
        Row=new EntityLocation(new Entity(Entity.Index,NextGeneration),Archetype.None,-1);
        this.RuntimeState.AliveEntityCount--;
        if(NextGeneration!=0){ this.FreeEntityIndices.Add(Entity.Index); }
    }

    /// <summary>System実行中でも安全な破棄。実際の破棄はフレーム末尾に適用される。</summary>
    public bool DestroyDeferred(Entity Entity,ref State State){
        if(!this.IsAlive(Entity)){ return false; }
        this.Commands.Destroy(Entity);
        return true;
    }

    internal void AssertDirectStructuralMutationAllowed(){
        if(this.Commands.IsSystemExecuting){
            throw new InvalidOperationException("Direct structural mutation is forbidden during system execution. Queue it through World.Commands.");
        }
    }

    internal void BeginSystemExecution()=>this.Commands.BeginSystemExecution();

    internal void EndSystemExecution()=>this.Commands.EndSystemExecution();

    /// <summary>新しいSceneインスタンスを確保する。生成された<c>Instantiate</c>だけが呼ぶ。</summary>
    public SceneInstance BeginSceneInstance(){
        this.AssertDirectStructuralMutationAllowed();
        return this.SceneInstances.Allocate();
    }

    public SceneInstance BeginSceneInstanceDeferred(){
        var Instance=this.SceneInstances.Allocate(Active:false);
        this.Commands.BeginScene(Instance);
        return Instance;
    }

    /// <summary>
    /// 生成済みのEntityをSceneへ帰属させ、<b>そのまま返す</b>(生成コードが
    /// <c>World.AttachToScene(Instance, ArchetypeNN.Create(...))</c>と入れ子で書けるようにするため)。
    /// ゲームプレイが実行時に作ったEntityを後からSceneへ加えることもできる。
    /// </summary>
    public Entity AttachToScene(SceneInstance Instance,Entity Entity){
        this.AssertDirectStructuralMutationAllowed();
        if(this.SceneInstances.Resolve(Instance) is not { } Slot||!this.IsAlive(Entity)){
            return Entity;
        }

        ref var Location=ref this.EntityArchetypeIndices.GetRefUnchecked(Entity.Id);
        Location.Cell=default;
        Location.SceneSlot=Instance.Slot;
        Slot.Entities.Add(Entity);
        return Entity;
    }

    public Entity AttachToSceneDeferred(SceneInstance Instance,Entity Entity){
        if(this.SceneInstances.ResolvePending(Instance)is null||(!this.IsAlive(Entity)&&!this.IsReserved(Entity))){
            throw new InvalidOperationException("A deferred scene attachment requires matching pending handles.");
        }
        this.Commands.AttachScene(Instance,Entity);
        return Entity;
    }

    public void EndSceneInstance(SceneInstance Instance,Entity Root){
        this.AssertDirectStructuralMutationAllowed();
        if(this.SceneInstances.Resolve(Instance) is { } Slot){
            Slot.Root=Root;
        }
    }

    public void EndSceneInstanceDeferred(SceneInstance Instance,Entity Root){
        if(this.SceneInstances.ResolvePending(Instance)is null||(!this.IsAlive(Root)&&!this.IsReserved(Root))){
            throw new InvalidOperationException("A deferred scene root requires matching pending handles.");
        }
        this.Commands.EndScene(Instance,Root);
    }

    public bool IsSceneAlive(SceneInstance Instance){
        return this.SceneInstances.Resolve(Instance) is not null;
    }

    public Entity GetSceneRoot(SceneInstance Instance){
        return this.SceneInstances.Resolve(Instance) is { } Slot?Slot.Root:default;
    }

    /// <summary>
    /// The entities this scene instance attached, in attach order. For a generated scene that is its
    /// node order (pre-order: a node, then its children), so index N is the scene's N-th node.
    /// </summary>
    public Entity[]GetSceneEntities(SceneInstance Instance){
        if(this.SceneInstances.Resolve(Instance) is not { } Slot){
            return [];
        }
        var Entities=new Entity[Slot.Entities.Count];
        for(var Index=0;Index<Entities.Length;Index++){
            Entities[Index]=Slot.Entities.GetRefUnchecked(Index);
        }
        return Entities;
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
        this.AssertDirectStructuralMutationAllowed();
        if(this.SceneInstances.Resolve(Instance) is not { } Slot){
            return;
        }

        for(var Index=Slot.Entities.Count-1;Index>=0;Index--){
            var Owned=Slot.Entities.GetRefUnchecked(Index);

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
        this.Commands.UnloadScene(Instance);
        return true;
    }

    /// <summary>Assigns exclusive cell ownership and a stable identity to a live entity.</summary>
    public Entity AttachToCell(WorldCellId Cell,PersistentEntityId Persistent,Entity Entity){
        this.AssertDirectStructuralMutationAllowed();
        if(!Cell.IsValid){ throw new ArgumentException("A valid world-cell ID is required.",nameof(Cell)); }
        if(!Persistent.IsValid){ throw new ArgumentException("A valid persistent entity ID is required.",nameof(Persistent)); }
        if(!this.IsAlive(Entity)){ throw new InvalidOperationException("A stale entity cannot be attached to a world cell."); }
        ref var Location=ref this.EntityArchetypeIndices.GetRefUnchecked(Entity.Id);
        if(Location.Persistent.IsValid&&Location.Persistent!=Persistent){
            throw new InvalidOperationException("A live entity cannot change its persistent identity.");
        }
        if(this.PersistentEntities.TryGetValue(Persistent,out var Existing)&&Existing!=Entity&&this.IsAlive(Existing)){
            throw new InvalidOperationException("The persistent entity ID already belongs to another live entity.");
        }
        if(Location.Cell==Cell&&Location.Persistent==Persistent){ return Entity; }
        if(!this.CellEntities.TryGetValue(Cell,out var Owned)){
            Owned=new ChunkBuffer<Entity>();
            this.CellEntities.Add(Cell,Owned);
        }
        Location.SceneSlot=-1;
        Location.Cell=Cell;
        Location.Persistent=Persistent;
        this.PersistentEntities[Persistent]=Entity;
        Owned.Add(Entity);
        return Entity;
    }

    public WorldCellId GetOwningCell(Entity Entity)=>this.IsAlive(Entity)?this.EntityArchetypeIndices.GetRefUnchecked(Entity.Id).Cell:default;

    public PersistentEntityId GetPersistentEntityId(Entity Entity)=>this.IsAlive(Entity)?this.EntityArchetypeIndices.GetRefUnchecked(Entity.Id).Persistent:default;

    public bool TryResolve(PersistentEntityId Persistent,out Entity Entity){
        if(Persistent.IsValid&&this.PersistentEntities.TryGetValue(Persistent,out Entity)&&this.IsAlive(Entity)){
            return true;
        }
        Entity=default;
        return false;
    }

    public bool HasOwnedCell(WorldCellId Cell)=>Cell.IsValid&&this.CellEntities.ContainsKey(Cell);

    public bool CanAttachToCell(WorldCellId Cell,PersistentEntityId Persistent){
        if(!Cell.IsValid||!Persistent.IsValid){ return false; }
        return !this.PersistentEntities.TryGetValue(Persistent,out var Existing)||!this.IsAlive(Existing);
    }

    public bool UnloadCell(WorldCellId Cell){
        this.AssertDirectStructuralMutationAllowed();
        if(!this.CellEntities.TryGetValue(Cell,out var Owned)){ return false; }
        for(var Index=Owned.Count-1;Index>=0;Index--){
            var Entity=Owned.GetRefUnchecked(Index);
            if(this.IsAlive(Entity)&&this.EntityArchetypeIndices.GetRefUnchecked(Entity.Id).Cell==Cell){
                this.Destroy(Entity);
            }
        }
        this.CellEntities.Remove(Cell);
        return true;
    }

    /// <summary>
    /// Scene定義からRuntimeのEntity群を構築する唯一の公開API。
    /// <c>new MainScene()</c>は行われず(SGが生成partialへprivateコンストラクターを出す)、
    /// Scene DSL自体も実行されない — 生成された<c>Instantiate</c>が
    /// <c>ArchetypeNN.Create</c>を直接呼ぶだけである。
    /// </summary>
    public SceneInstance InstantiateScene<TScene>()where TScene:ISceneDefinition{
        return TScene.Instantiate(ref this);
    }

    public SceneInstance InstantiateSceneDeferred<TScene>()where TScene:ISceneDefinition{
        return TScene.InstantiateDeferred(ref this);
    }

    public void InstantiateCell<TScene>(WorldCellId Cell)where TScene:ISceneDefinition{
        TScene.InstantiateCell(ref this,Cell);
    }

    /// <summary>
    /// 生成された<c>PlaybackStructuralChanges</c>からglobal enqueue orderで呼ばれる非Move command。
    /// <see cref="Destroy"/>は冪等なので二重破棄も安全。
    /// </summary>
    internal void PlaybackNonMoveStructuralCommand(in EntityCommand Command){
        switch(Command.Kind){
            case EntityCommandKind.Destroy:this.Destroy(Command.Entity);break;
            case EntityCommandKind.SceneBegin:this.SceneInstances.Activate(Command.Scene);break;
            case EntityCommandKind.SceneAttach:this.AttachToScene(Command.Scene,Command.Entity);break;
            case EntityCommandKind.SceneEnd:this.EndSceneInstance(Command.Scene,Command.Entity);break;
            case EntityCommandKind.SceneUnload:this.UnloadScene(Command.Scene);break;
            case EntityCommandKind.CellUnload:this.UnloadCell(Command.Cell);break;
            default:throw new ArgumentOutOfRangeException(nameof(Command));
        }
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

    internal bool TryRemoveLast(out T Item){
        if(this.Count==0){ Item=default!;return false; }
        var Index=--this.Count;
        Item=this.Items.GetRefUnchecked(Index);
        this.Items.ClearAtUnchecked(Index);
        return true;
    }
}
