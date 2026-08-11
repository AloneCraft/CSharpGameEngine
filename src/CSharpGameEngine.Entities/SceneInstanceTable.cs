namespace CSharpGameEngine.Entities;

/// <summary>1つのSceneインスタンスが所有する情報。スロットは<see cref="SceneInstanceTable"/>が再利用する。</summary>
internal sealed class SceneInstanceSlot {
    /// <summary>スロット再利用時に加算する。古い<see cref="SceneInstance"/>ハンドルはこれで弾かれる。</summary>
    internal int Version;

    internal bool Alive;

    internal Entity Root;

    /// <summary>
    /// このSceneが生成したEntity。<c>UnloadScene</c>はこれを辿って破棄する。
    /// ゲームプレイ側が個別に破棄したEntityは死んだハンドルとして残るが、破棄は冪等なので問題ない。
    /// </summary>
    internal readonly ChunkBuffer<Entity>Entities=new();
}

/// <summary>
/// <c>World</c>が持つSceneインスタンスの表(ADR-0040)。
///
/// <para>
/// <c>List</c>/<c>Stack</c>の使用は「Runtime reflectionと<c>Dictionary&lt;Type,…&gt;</c>による
/// query/archetype dispatchを導入しない」という制約に触れない。禁止されているのは型をキーにした
/// 動的ディスパッチであって、通常のコレクションではない。ここは毎フレーム走る経路でもない。
/// </para>
/// </summary>
internal sealed class SceneInstanceTable {
    private readonly List<SceneInstanceSlot>Slots=[];

    private readonly Stack<int>FreeSlots=new();

    internal SceneInstance Allocate(){
        if(this.FreeSlots.Count>0){
            var ReusedIndex=this.FreeSlots.Pop();
            SceneInstanceSlot Reused=this.Slots[ReusedIndex];
            Reused.Alive=true;
            Reused.Root=default;
            return new SceneInstance(ReusedIndex,Reused.Version);
        }

        // 世代を1から始めることで、default(SceneInstance)(= Slot 0 / Version 0)が
        // 実在するスロット0と一致しなくなる。
        SceneInstanceSlot Created=new(){ Version=1,Alive=true };
        this.Slots.Add(Created);
        return new SceneInstance(this.Slots.Count-1,Created.Version);
    }

    /// <summary>世代が一致しない(= 既にUnloadされた)ハンドルは<c>null</c>を返す。</summary>
    internal SceneInstanceSlot?Resolve(SceneInstance Instance){
        if((uint)Instance.Slot>=(uint)this.Slots.Count){
            return null;
        }

        SceneInstanceSlot Slot=this.Slots[Instance.Slot];
        return Slot.Alive&&Slot.Version==Instance.Version?Slot:null;
    }

    internal void Free(SceneInstance Instance){
        if(this.Resolve(Instance) is not { } Slot){
            return;
        }

        Slot.Alive=false;
        Slot.Version++;
        Slot.Root=default;
        Slot.Entities.Clear();
        this.FreeSlots.Push(Instance.Slot);
    }

    /// <summary>位置表が持つスロット番号から、世代付きのハンドルへ復元する。</summary>
    internal SceneInstance ToInstance(int SlotIndex){
        if((uint)SlotIndex>=(uint)this.Slots.Count){
            return SceneInstance.None;
        }

        SceneInstanceSlot Slot=this.Slots[SlotIndex];
        return Slot.Alive?new SceneInstance(SlotIndex,Slot.Version):SceneInstance.None;
    }
}
