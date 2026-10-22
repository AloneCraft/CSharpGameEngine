namespace CSharpGameEngine.Entities;

/// <summary>
/// イベントの印(ADR-0077)。<c>struct</c>に付けると、<c>Update(in TEvent Event, ...)</c>と書いたSystemが、送られたイベント1件ごとに
/// 呼ばれる。イベントはその1フレームの間だけ存在し、フレームの頭で破棄される(次のフレームには持ち越さない)。
/// 送り手は<see cref="EventWriter{T}"/>を引数に取る。受け手は、送り手より後に動くよう自動で並べられる。
/// </summary>
public interface IEvent {
}

/// <summary>
/// イベントを送る口。<c>Update(..., in EventWriter&lt;TEvent&gt; Events)</c>と引数に書くと、生成されたループが渡す。
/// 送ったイベントは、同じフレームのうちに、送り手より後に動く受け手が1件ずつ読む。
/// </summary>
public readonly struct EventWriter<T> where T:struct,IEvent {
    private readonly List<T>Queue;

    public EventWriter(List<T>Queue){
        this.Queue=Queue;
    }

    public void Send(in T Event)=>this.Queue.Add(Event);
}
