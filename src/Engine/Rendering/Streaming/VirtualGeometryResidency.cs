using CSharpGameEngine.Rendering.Pipeline;
namespace CSharpGameEngine.Rendering.Streaming;
/// <summary>
/// 仮想ジオメトリのページの出し入れ（ADR-0100 決定 1・3、M0131 単位 4）。メッシュ 1 つの置き場（索引の枠と頂点の区画、<see cref="VirtualGeometrySlotLayout"/>）に、
/// どのページを置くかを決め、まとまりごとの「準備済み」の印を求める。GPU を使わない。
/// <list type="bullet">
/// <item>ページ: 1 つのまとまりが作った塊の組（ページの番号はまとまりの番号）。根のページ（親の無い塊を持つページ）は最初に置き、追い出さない。</item>
/// <item>準備済み（<see cref="Ready"/>）: まとまり G を細かくしてよい。G に入力（1 段細かい塊）があり、そのページがすべて置かれていて、G が作った塊を使う上のまとまりも
/// すべて準備済みであること。親が準備済みでなければ子も準備済みにならないので、選択の「十分」に「準備済みでなければ十分とみなす」を足しても、
/// 十分さが親から子へ受け継がれ、描く塊は割れ目も重なりも無い。描く塊は必ず置かれている。</item>
/// <item>要求: 選択が「細かくしたいが準備済みでない」まとまりを、画面の誤差の画素数を優先度にして知らせる。そのまとまりの入力のページを、優先度の高い順に置く。</item>
/// <item>使用: 選択が描いたページを知らせる。描いたページから上へたどり、そこへ届くのに要るページ（上のまとまりの入力すべて）も使ったことにする
/// （途中のページを追い出すと、その下がまとめて使えなくなるため）。</item>
/// <item>追い出し: 置き場が足りないとき、固定しておらず、最後に使ったのが最も古いページ（同じなら番号の小さいほう）から追い出す。追い出した枠と区画は、
/// それを使ったかもしれないフレームが終わるまで（<c>ReuseDelayFrames</c> フレーム）使い直さない。
/// 知らせは同じフレーム数だけ遅れて届くので、置いてからその間のページは追い出さない（使う前に追い出して置き直すのを繰り返さないため）。</item>
/// </list>
/// 同じ入力なら同じ結果になる（空いた枠と区画は番号の小さい順に使う）。
/// </summary>
public sealed class VirtualGeometryResidency {
    private readonly VirtualGeometrySlotLayout Layout;
    private readonly int[][]PageClusters;
    private readonly int[][]PageInputs;
    private readonly int[][]GroupConsumers;
    private readonly bool[]Pinned;
    private readonly bool[]Resident;
    private readonly ulong[]LastUsed;
    private readonly int[]ClusterSlot;
    private readonly int[][]ClusterBlocks;
    private readonly uint[]ReadyFlags;
    private readonly SortedSet<int>FreeSlots=[];
    private readonly SortedSet<int>FreeBlocks=[];
    private readonly Queue<(ulong ReusableFrame,int[]Slots,int[]Blocks)>Releasing=[];
    private readonly int ReuseDelayFrames;
    /// <param name="Layout">塊の置き場の形。</param>
    /// <param name="Clusters">塊の表（Group と ParentGroup を使う）。</param>
    /// <param name="GroupCount">まとまりの数。</param>
    /// <param name="SlotCapacity">索引の枠の数。</param>
    /// <param name="BlockCapacity">頂点の区画の数。</param>
    /// <param name="ReuseDelayFrames">追い出した枠と区画を使い直すまでのフレーム数（GPU で同時に進むフレームの数）。</param>
    public VirtualGeometryResidency(VirtualGeometrySlotLayout Layout,ReadOnlySpan<StageVirtualGeometryCluster>Clusters,int GroupCount,int SlotCapacity,int BlockCapacity,int ReuseDelayFrames){
        ArgumentNullException.ThrowIfNull(Layout);
        if(Clusters.Length!=Layout.ClusterCount){ throw new ArgumentException("The cluster table does not match the layout.",nameof(Clusters)); }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(GroupCount);
        ArgumentOutOfRangeException.ThrowIfNegative(ReuseDelayFrames);
        this.Layout=Layout;
        this.ReuseDelayFrames=ReuseDelayFrames;
        var Pages=new List<int>[GroupCount];
        var Inputs=new HashSet<int>[GroupCount];
        var Consumers=new HashSet<int>[GroupCount];
        for(var Group=0;Group<GroupCount;Group++){
            Pages[Group]=[];
            Inputs[Group]=[];
            Consumers[Group]=[];
        }
        this.Pinned=new bool[GroupCount];
        for(var Cluster=0;Cluster<Clusters.Length;Cluster++){
            var Group=(int)Clusters[Cluster].Group;
            var Parent=Clusters[Cluster].ParentGroup;
            if((uint)Group>=(uint)GroupCount||Parent>=GroupCount){ throw new ArgumentException($"Cluster {Cluster} refers to a group outside the mesh.",nameof(Clusters)); }
            Pages[Group].Add(Cluster);
            if(Parent<0){
                this.Pinned[Group]=true;
                continue;
            }
            Inputs[Parent].Add(Group);
            Consumers[Group].Add(Parent);
        }
        this.PageClusters=[..Pages.Select(static Page=>Page.ToArray())];
        this.PageInputs=[..Inputs.Select(static Set=>Set.Order().ToArray())];
        this.GroupConsumers=[..Consumers.Select(static Set=>Set.Order().ToArray())];
        this.Resident=new bool[GroupCount];
        this.LastUsed=new ulong[GroupCount];
        this.ClusterSlot=new int[Clusters.Length];
        Array.Fill(this.ClusterSlot,-1);
        this.ClusterBlocks=new int[Clusters.Length][];
        this.ReadyFlags=new uint[GroupCount];
        var RootSlots=0;
        var RootBlocks=0;
        for(var Group=0;Group<GroupCount;Group++){
            if(!this.Pinned[Group]){ continue; }
            RootSlots+=this.PageSlotCount(Group);
            RootBlocks+=this.PageBlockCount(Group);
        }
        if(SlotCapacity<RootSlots||BlockCapacity<RootBlocks){
            throw new ArgumentException($"The pool ({SlotCapacity} slots, {BlockCapacity} blocks) cannot hold the root pages ({RootSlots} slots, {RootBlocks} blocks).");
        }
        this.SlotCapacity=SlotCapacity;
        this.BlockCapacity=BlockCapacity;
        for(var Slot=0;Slot<SlotCapacity;Slot++){ this.FreeSlots.Add(Slot); }
        for(var Block=0;Block<BlockCapacity;Block++){ this.FreeBlocks.Add(Block); }
    }
    public int SlotCapacity { get; }
    public int BlockCapacity { get; }
    public int GroupCount=>this.Resident.Length;
    /// <summary>まとまりごとの準備済みの印（1 なら細かくしてよい）。GPU の選択へそのまま送る。</summary>
    public ReadOnlySpan<uint>Ready=>this.ReadyFlags;
    /// <summary>空いている（使い直してよい）索引の枠の数。</summary>
    public int FreeSlotCount=>this.FreeSlots.Count;
    /// <summary>空いている（使い直してよい）頂点の区画の数。</summary>
    public int FreeBlockCount=>this.FreeBlocks.Count;
    public bool IsResident(int Page)=>this.Resident[Page];
    public bool IsPinned(int Page)=>this.Pinned[Page];
    public int ResidentPageCount=>this.Resident.Count(static Value=>Value);
    /// <summary>ページ <paramref name="Page"/> の塊の番号。</summary>
    public ReadOnlySpan<int>PageClusterIndices(int Page)=>this.PageClusters[Page];
    /// <summary>まとまり <paramref name="Group"/> の入力の塊を持つページ（細かくするのに要るページ）。</summary>
    public ReadOnlySpan<int>InputPages(int Group)=>this.PageInputs[Group];
    /// <summary>置いている塊 <paramref name="Cluster"/> の索引の枠と頂点の区画。置いていなければ false。</summary>
    public bool TryGetPlacement(int Cluster,out int Slot,out ReadOnlySpan<int>Blocks){
        Slot=this.ClusterSlot[Cluster];
        Blocks=Slot<0?default:this.ClusterBlocks[Cluster];
        return Slot>=0;
    }
    /// <summary>
    /// 根のページから細かい方へ、置き場に入る限りページを置く（最初の 1 回）。置いたページを <paramref name="Loads"/> に足す。
    /// 入りきらないページに出会うか、<paramref name="MaximumPages"/> 個（根のページを含む）を置いたら、その先は置かない（粗い方から順に詰める）。
    /// </summary>
    public void Preload(List<int>Loads,int MaximumPages=int.MaxValue){
        ArgumentNullException.ThrowIfNull(Loads);
        var Queue=new Queue<int>();
        var Seen=new bool[this.GroupCount];
        for(var Group=0;Group<this.GroupCount;Group++){
            if(!this.Pinned[Group]){ continue; }
            if(!this.TryPlace(Group,0)){ throw new InvalidOperationException("The root pages did not fit."); }
            Loads.Add(Group);
            Seen[Group]=true;
            Queue.Enqueue(Group);
        }
        while(Queue.Count>0){
            var Group=Queue.Dequeue();
            foreach(var Input in this.PageInputs[Group]){
                if(Seen[Input]){ continue; }
                Seen[Input]=true;
                if(Loads.Count>=MaximumPages||!this.TryPlace(Input,0)){
                    this.RecomputeReady();
                    return;
                }
                Loads.Add(Input);
                Queue.Enqueue(Input);
            }
        }
        this.RecomputeReady();
    }
    /// <summary>
    /// 1 フレーム分の知らせを受けて、ページを置き、追い出す。
    /// </summary>
    /// <param name="Frame">フレームの番号（呼ぶたびに増やす）。</param>
    /// <param name="UsedPages">選択が描いたページ。</param>
    /// <param name="Requests">細かくしたいが準備済みでないまとまりと、その優先度（画面の誤差の画素数、大きいほど先）。</param>
    /// <param name="PageBudget">このフレームで新しく置いてよいページの数。</param>
    /// <param name="Loads">新しく置いたページを足す先。中身は <see cref="TryGetPlacement"/> で引いて書く。</param>
    /// <param name="Report">このフレームの分を足す報告。</param>
    public void Update(ulong Frame,ReadOnlySpan<int>UsedPages,ReadOnlySpan<(int Group,float Priority)>Requests,int PageBudget,List<int>Loads,StreamingReport Report){
        ArgumentNullException.ThrowIfNull(Loads);
        ArgumentNullException.ThrowIfNull(Report);
        ArgumentOutOfRangeException.ThrowIfNegative(PageBudget);
        while(this.Releasing.Count>0&&this.Releasing.Peek().ReusableFrame<=Frame){
            var (_,Slots,Blocks)=this.Releasing.Dequeue();
            foreach(var Slot in Slots){ this.FreeSlots.Add(Slot); }
            foreach(var Block in Blocks){ this.FreeBlocks.Add(Block); }
        }
        var Visited=new bool[this.GroupCount];
        foreach(var Page in UsedPages){ this.MarkUsed(Page,Frame,Visited); }
        // 要求したまとまりのページ（描いている）と、そこへ届くのに要るページも、追い出さない。
        foreach(var (Group,_) in Requests){ this.MarkUsed(Group,Frame,Visited); }
        var Ordered=Requests.ToArray();
        Array.Sort(Ordered,static (Left,Right)=>Right.Priority!=Left.Priority?Right.Priority.CompareTo(Left.Priority):Left.Group.CompareTo(Right.Group));
        var Budget=PageBudget;
        var Changed=false;
        foreach(var (Group,_) in Ordered){
            // 細かくするには入力のページが全部同時に要る。既に置いている入力は、このフレームで使ったことにして追い出さない。
            long NeedSlots=0,NeedBlocks=0;
            var Missing=0;
            foreach(var Page in this.PageInputs[Group]){
                Report.CountRequest();
                if(this.Resident[Page]){
                    this.LastUsed[Page]=Math.Max(this.LastUsed[Page],Frame);
                    Report.CountResident();
                    continue;
                }
                Missing++;
                NeedSlots+=this.PageSlotCount(Page);
                NeedBlocks+=this.PageBlockCount(Page);
            }
            if(Missing==0){ continue; }
            // 足りない入力が全部は入らない（空き、使い直し待ち、追い出せるページを合わせても）なら、置いても使えないので、置かず、追い出さない。
            if(!this.CanHold(NeedSlots,NeedBlocks,Frame)){
                for(var Count=0;Count<Missing;Count++){ Report.CountDeniedFull(); }
                continue;
            }
            foreach(var Page in this.PageInputs[Group]){
                if(this.Resident[Page]){ continue; }
                if(Budget==0){
                    Report.CountDeniedByBudget();
                    continue;
                }
                while(!this.Fits(Page)&&!this.WillFit(Page)){
                    var Victim=this.FindVictim(Frame);
                    if(Victim<0){ break; }
                    this.Evict(Victim,Frame);
                    Report.CountEvicted();
                    Changed=true;
                }
                if(!this.TryPlace(Page,Frame)){
                    // 追い出した枠と区画は、まだ使い直せない（使い直せるようになったら、また要求される）。
                    Report.CountDeniedFull();
                    continue;
                }
                Loads.Add(Page);
                Report.CountAllocated(false);
                Budget--;
                Changed=true;
            }
        }
        if(Changed){ this.RecomputeReady(); }
    }
    private int PageSlotCount(int Page)=>this.PageClusters[Page].Length;
    private int PageBlockCount(int Page){
        var Count=0;
        foreach(var Cluster in this.PageClusters[Page]){ Count+=this.Layout.BlockCount(Cluster); }
        return Count;
    }
    /// <summary>空いている、使い直しを待っている、追い出せるページの枠と区画を合わせて、<paramref name="Slots"/> と <paramref name="Blocks"/> が入るか。</summary>
    private bool CanHold(long Slots,long Blocks,ulong Frame){
        long FreeSlots=this.FreeSlots.Count,FreeBlocks=this.FreeBlocks.Count;
        foreach(var (_,Waiting,WaitingBlocks) in this.Releasing){
            FreeSlots+=Waiting.Length;
            FreeBlocks+=WaitingBlocks.Length;
        }
        for(var Page=0;Page<this.GroupCount&&(FreeSlots<Slots||FreeBlocks<Blocks);Page++){
            if(!this.IsEvictable(Page,Frame)){ continue; }
            FreeSlots+=this.PageSlotCount(Page);
            FreeBlocks+=this.PageBlockCount(Page);
        }
        return FreeSlots>=Slots&&FreeBlocks>=Blocks;
    }
    private bool IsEvictable(int Page,ulong Frame)=>this.Resident[Page]&&!this.Pinned[Page]&&this.LastUsed[Page]+(ulong)this.ReuseDelayFrames<Frame;
    /// <summary>今は空いていないが、使い直しを待っている枠と区画が空けば入る（さらに追い出さなくてよい）。</summary>
    private bool WillFit(int Page){
        var Slots=this.FreeSlots.Count;
        var Blocks=this.FreeBlocks.Count;
        foreach(var (_,Waiting,WaitingBlocks) in this.Releasing){
            Slots+=Waiting.Length;
            Blocks+=WaitingBlocks.Length;
        }
        return Slots>=this.PageSlotCount(Page)&&Blocks>=this.PageBlockCount(Page);
    }
    private bool Fits(int Page)=>this.FreeSlots.Count>=this.PageSlotCount(Page)&&this.FreeBlocks.Count>=this.PageBlockCount(Page);
    private bool TryPlace(int Page,ulong Frame){
        if(!this.Fits(Page)){ return false; }
        foreach(var Cluster in this.PageClusters[Page]){
            var Slot=this.FreeSlots.Min;
            this.FreeSlots.Remove(Slot);
            var Blocks=new int[this.Layout.BlockCount(Cluster)];
            for(var Block=0;Block<Blocks.Length;Block++){
                Blocks[Block]=this.FreeBlocks.Min;
                this.FreeBlocks.Remove(Blocks[Block]);
            }
            this.ClusterSlot[Cluster]=Slot;
            this.ClusterBlocks[Cluster]=Blocks;
        }
        this.Resident[Page]=true;
        this.LastUsed[Page]=Frame;
        return true;
    }
    private void Evict(int Page,ulong Frame){
        List<int>Slots=[];
        List<int>Blocks=[];
        foreach(var Cluster in this.PageClusters[Page]){
            Slots.Add(this.ClusterSlot[Cluster]);
            Blocks.AddRange(this.ClusterBlocks[Cluster]);
            this.ClusterSlot[Cluster]=-1;
            this.ClusterBlocks[Cluster]=[];
        }
        this.Resident[Page]=false;
        this.Releasing.Enqueue((Frame+(ulong)this.ReuseDelayFrames,[..Slots],[..Blocks]));
    }
    /// <summary>
    /// 追い出すページ。置いていて、固定しておらず、最後に使ってから知らせが届くまでのフレーム数より古いもののうち、最後に使ったのが最も古いもの
    /// （同じなら番号の小さいほう）。無ければ -1。
    /// </summary>
    private int FindVictim(ulong Frame){
        var Best=-1;
        for(var Page=0;Page<this.GroupCount;Page++){
            if(!this.IsEvictable(Page,Frame)){ continue; }
            if(Best<0||this.LastUsed[Page]<this.LastUsed[Best]){ Best=Page; }
        }
        return Best;
    }
    /// <summary>ページ <paramref name="Page"/> を使ったことにし、上へたどって、そこへ届くのに要るページ（使う側のまとまりの入力すべてと、そのまとまりのページ）も使ったことにする。</summary>
    private void MarkUsed(int Page,ulong Frame,bool[]Visited){
        if((uint)Page>=(uint)this.GroupCount||Visited[Page]){ return; }
        Visited[Page]=true;
        if(this.Resident[Page]){ this.LastUsed[Page]=Math.Max(this.LastUsed[Page],Frame); }
        foreach(var Consumer in this.GroupConsumers[Page]){
            foreach(var Sibling in this.PageInputs[Consumer]){
                if(this.Resident[Sibling]){ this.LastUsed[Sibling]=Math.Max(this.LastUsed[Sibling],Frame); }
            }
            this.MarkUsed(Consumer,Frame,Visited);
        }
    }
    /// <summary>準備済みを、使う側（上）から先に求める。入力の無い葉のまとまりは細かくできないので、準備済みにしない。</summary>
    private void RecomputeReady(){
        var State=new sbyte[this.GroupCount];
        for(var Group=0;Group<this.GroupCount;Group++){ this.ReadyFlags[Group]=this.IsReady(Group,State)?1u:0u; }
    }
    private bool IsReady(int Group,sbyte[]State){
        if(State[Group]!=0){ return State[Group]>0; }
        var Ready=this.PageInputs[Group].Length>0;
        foreach(var Input in this.PageInputs[Group]){
            if(!Ready){ break; }
            Ready=this.Resident[Input];
        }
        foreach(var Consumer in this.GroupConsumers[Group]){
            if(!Ready){ break; }
            Ready=this.IsReady(Consumer,State);
        }
        State[Group]=(sbyte)(Ready?1:-1);
        return Ready;
    }
}
