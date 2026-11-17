namespace CSharpGameEngine.Rendering.Streaming;
/// <summary>
/// 読み込み・作り直しの数と、断った理由の報告（ADR-0100 決定 1、M0129 単位 6）。1 回の <see cref="PageSlotAllocator.Update"/> の分を数える。
/// 予算や置き場が足りずに細かさを落としたときは、ここに理由が残る（黙って粗くしない）。
/// </summary>
public sealed class StreamingReport {
    /// <summary>要求の数（同じページを 2 度要求すれば 2）。</summary>
    public int Requested { get;private set; }
    /// <summary>既に置き場にあった要求の数。</summary>
    public int AlreadyResident { get;private set; }
    /// <summary>新しく置き場を割り当てた数（送る・作るのはこの数）。</summary>
    public int Allocated { get;private set; }
    /// <summary>新しいページのために追い出した数。</summary>
    public int Evicted { get;private set; }
    /// <summary>このフレームの予算を使い切ったので断った数。次のフレームでまた要求される。</summary>
    public int DeniedByBudget { get;private set; }
    /// <summary>追い出せるページが無く、置き場が満杯で断った数。置き場を広げるか、要求を減らす必要がある。</summary>
    public int DeniedFull { get;private set; }
    public void Clear(){
        this.Requested=0;
        this.AlreadyResident=0;
        this.Allocated=0;
        this.Evicted=0;
        this.DeniedByBudget=0;
        this.DeniedFull=0;
    }
    internal void CountRequest()=>this.Requested++;
    internal void CountResident()=>this.AlreadyResident++;
    internal void CountAllocated(bool Evicted){
        this.Allocated++;
        if(Evicted){ this.Evicted++; }
    }
    internal void CountDeniedByBudget()=>this.DeniedByBudget++;
    internal void CountDeniedFull()=>this.DeniedFull++;
}
/// <summary>新しく割り当てた置き場 1 つ。<see cref="EvictedKey"/> は、その置き場から追い出したページ（空いていた置き場なら null）。</summary>
public readonly record struct PageAllocation(ulong Key,int Slot,ulong?EvictedKey);
/// <summary>
/// 空き枠の割り当て（ADR-0100 決定 1、M0129 単位 6）。決まった数の置き場に、要求されたページを入れる。仮想ジオメトリ、仮想シャドウ、仮想テクスチャが共有する。
/// ページのキーは使い手が決める 64 ビットの値。約束:
/// <list type="bullet">
/// <item>1 回に新しく割り当てる数は、渡した予算を超えない。置いているページの数は置き場の数を超えない。</item>
/// <item>固定したページは追い出さない。このフレームで要求したページも追い出さない。</item>
/// <item>同じ入力なら同じ結果になる。要求は渡した順（使い手が優先順に並べる）に処理し、空いた置き場は番号の小さい順に使い、追い出すのは
/// 最後に使ったフレームが最も古いページ（同じならキーの小さいほう）。</item>
/// </list>
/// </summary>
public sealed class PageSlotAllocator {
    private readonly ulong[]SlotKeys;
    private readonly bool[]SlotUsed;
    private readonly ulong[]SlotLastUsed;
    private readonly Dictionary<ulong,int>Slots=[];
    private readonly HashSet<ulong>Pinned=[];
    private readonly SortedSet<int>FreeSlots=[];
    /// <param name="Capacity">置き場の数（1 以上）。</param>
    public PageSlotAllocator(int Capacity){
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Capacity);
        this.Capacity=Capacity;
        this.SlotKeys=new ulong[Capacity];
        this.SlotUsed=new bool[Capacity];
        this.SlotLastUsed=new ulong[Capacity];
        for(var Slot=0;Slot<Capacity;Slot++){ this.FreeSlots.Add(Slot); }
    }
    public int Capacity { get; }
    /// <summary>今置いているページの数。</summary>
    public int ResidentCount=>this.Slots.Count;
    public bool TryGetSlot(ulong Key,out int Slot)=>this.Slots.TryGetValue(Key,out Slot);
    public bool IsPinned(ulong Key)=>this.Pinned.Contains(Key);
    /// <summary>置いているページを固定する（追い出さない）。粗い段のページなど、いつも要るものに使う。</summary>
    public void Pin(ulong Key){
        if(!this.Slots.ContainsKey(Key)){ throw new InvalidOperationException($"Page {Key} is not resident and cannot be pinned."); }
        this.Pinned.Add(Key);
    }
    public void Unpin(ulong Key)=>this.Pinned.Remove(Key);
    /// <summary>ページを置き場から外す（使い手がもう要らないと分かったとき）。固定したページは外せない。外したら true。</summary>
    public bool Release(ulong Key){
        if(this.Pinned.Contains(Key)){ throw new InvalidOperationException($"Page {Key} is pinned and cannot be released."); }
        if(!this.Slots.Remove(Key,out var Slot)){ return false; }
        this.SlotUsed[Slot]=false;
        this.FreeSlots.Add(Slot);
        return true;
    }
    /// <summary>
    /// このフレームの要求を、渡した順に処理する。既に置いているページは「このフレームで使った」と記録する。置いていないページは、予算の範囲で空いた置き場か、
    /// 追い出せるページの置き場に割り当て、<paramref name="Allocations"/> に足す。断ったものは <paramref name="Report"/> に理由ごとに数える。
    /// </summary>
    /// <param name="Frame">フレームの番号。呼ぶたびに増やす。</param>
    /// <param name="Requests">要求するページのキー（優先順）。</param>
    /// <param name="NewPageBudget">このフレームで新しく割り当ててよい数（0 以上）。</param>
    /// <param name="Allocations">新しく割り当てたものを足す先（消さずに足す）。</param>
    /// <param name="Report">このフレームの分を足す報告（消さずに足す）。</param>
    public void Update(ulong Frame,ReadOnlySpan<ulong>Requests,int NewPageBudget,List<PageAllocation>Allocations,StreamingReport Report){
        ArgumentOutOfRangeException.ThrowIfNegative(NewPageBudget);
        ArgumentNullException.ThrowIfNull(Allocations);
        ArgumentNullException.ThrowIfNull(Report);
        var Budget=NewPageBudget;
        foreach(var Key in Requests){
            Report.CountRequest();
            if(this.Slots.TryGetValue(Key,out var Resident)){
                this.SlotLastUsed[Resident]=Frame;
                Report.CountResident();
                continue;
            }
            if(Budget==0){
                Report.CountDeniedByBudget();
                continue;
            }
            ulong?Evicted=null;
            int Slot;
            if(this.FreeSlots.Count>0){
                Slot=this.FreeSlots.Min;
                this.FreeSlots.Remove(Slot);
            } else {
                Slot=this.FindVictim(Frame);
                if(Slot<0){
                    Report.CountDeniedFull();
                    continue;
                }
                Evicted=this.SlotKeys[Slot];
                this.Slots.Remove(this.SlotKeys[Slot]);
            }
            this.SlotKeys[Slot]=Key;
            this.SlotUsed[Slot]=true;
            this.SlotLastUsed[Slot]=Frame;
            this.Slots.Add(Key,Slot);
            Allocations.Add(new PageAllocation(Key,Slot,Evicted));
            Report.CountAllocated(Evicted is not null);
            Budget--;
        }
    }
    /// <summary>追い出すページの置き場。固定しておらず、このフレームで使っていないページのうち、最後に使ったのが最も古いもの（同じならキーの小さいほう）。無ければ -1。</summary>
    private int FindVictim(ulong Frame){
        var Best=-1;
        for(var Slot=0;Slot<this.Capacity;Slot++){
            if(!this.SlotUsed[Slot]||this.SlotLastUsed[Slot]>=Frame||this.Pinned.Contains(this.SlotKeys[Slot])){ continue; }
            if(Best<0||this.SlotLastUsed[Slot]<this.SlotLastUsed[Best]||(this.SlotLastUsed[Slot]==this.SlotLastUsed[Best]&&this.SlotKeys[Slot]<this.SlotKeys[Best])){ Best=Slot; }
        }
        return Best;
    }
}
