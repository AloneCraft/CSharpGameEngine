namespace CSharpGameEngine.Rendering;
/// <summary>
/// VirtualObject の段の選び方（ADR-0100 決定 6、M0133）。物が画面で占める画素の大きさだけで、描き方の段（0 がいちばん粗い）を選ぶ。距離では決めない。
/// 画素の大きさは <see cref="Streaming.ScreenSizeMetric"/> で求める（仮想ジオメトリと同じ式）。手本は Omni の VirtualObjectSelector と VirtualObjectController。
/// <list type="bullet">
/// <item><c>Enter[i]</c> は段 i+1 へ上がる画素の大きさ（正の昇順）。段の数は <c>Enter.Length + 1</c>（最大 <see cref="MaximumLevels"/>）。</item>
/// <item>段の境で行き来しない: 上がるには <c>Enter × h</c> 以上、下がるには <c>Enter ÷ h</c> 未満（既定 h = 1.25）。</item>
/// <item>上の段は準備ができたときだけ選ぶ。初めの 1 回はヒステリシスなしの段から始める。段 0 はいつも準備できているとみなす（何も描かない段でもよい）。</item>
/// </list>
/// 毎フレーム物ごとに呼ぶので、静的な関数は閾値を <see cref="ReadOnlySpan{T}"/> で受け取り、何も確保しない。
/// </summary>
public sealed class VirtualObjectSelector {
    public const int MaximumLevels=32;
    public const float DefaultHysteresis=1.25f;
    private readonly float[]EnterPixels;
    /// <param name="EnterPixels">段 i+1 へ上がる画素の大きさ（正の昇順、31 個まで）。</param>
    /// <param name="Hysteresis">1 以上。</param>
    public VirtualObjectSelector(ReadOnlySpan<float>EnterPixels,float Hysteresis=DefaultHysteresis){
        if(Validate(EnterPixels,Hysteresis) is{ }Problem){ throw new ArgumentException(Problem,nameof(EnterPixels)); }
        this.EnterPixels=EnterPixels.ToArray();
        this.Hysteresis=Hysteresis;
    }
    public float Hysteresis { get; }
    public int LevelCount=>this.EnterPixels.Length+1;
    /// <summary>段 i+1 へ上がる画素の大きさ。</summary>
    public ReadOnlySpan<float>Enter=>this.EnterPixels;
    public int Ideal(float Pixels)=>Ideal(this.EnterPixels,Pixels);
    public int Select(float Pixels,int Current,uint ReadyMask=uint.MaxValue)=>Select(this.EnterPixels,this.Hysteresis,Pixels,Current,ReadyMask);
    public int Step(float Pixels,int Current,bool Started,uint ReadyMask,out int Desired)=>Step(this.EnterPixels,this.Hysteresis,Pixels,Current,Started,ReadyMask,out Desired);
    /// <summary>閾値とヒステリシスが正しくなければ、その理由。正しければ null。</summary>
    public static string?Validate(ReadOnlySpan<float>Enter,float Hysteresis){
        if(Enter.Length>=MaximumLevels){ return $"A virtual object has at most {MaximumLevels} levels."; }
        for(var Index=0;Index<Enter.Length;Index++){
            if(!(Enter[Index]>0f)||!float.IsFinite(Enter[Index])||Index>0&&!(Enter[Index]>Enter[Index-1])){
                return "The enter pixels must be positive, finite and strictly increasing.";
            }
        }
        return Hysteresis>=1f&&float.IsFinite(Hysteresis)?null:"The hysteresis must be finite and at least one.";
    }
    /// <summary>ヒステリシスなしの段（状態を持たない。初回と診断用）。NaN と負は 0 として扱う。</summary>
    public static int Ideal(ReadOnlySpan<float>Enter,float Pixels){
        Pixels=Clean(Pixels);
        var Level=0;
        while(Level<Enter.Length&&Pixels>=Enter[Level]){ Level++; }
        return Level;
    }
    /// <summary>
    /// ヒステリシスつきの段。まず上へ（<c>Pixels ≥ Enter × h</c> で、その段が準備できている間、一度に何段でも）、次に下へ（<c>Pixels &lt; Enter ÷ h</c> の間）。
    /// <paramref name="ReadyMask"/> のビット i が段 i の準備。
    /// </summary>
    public static int Select(ReadOnlySpan<float>Enter,float Hysteresis,float Pixels,int Current,uint ReadyMask=uint.MaxValue){
        Pixels=Clean(Pixels);
        var Level=Math.Clamp(Current,0,Enter.Length);
        while(Level<Enter.Length&&Pixels>=Enter[Level]*Hysteresis&&IsReady(ReadyMask,Level+1)){ Level++; }
        while(Level>0&&Pixels<Enter[Level-1]/Hysteresis){ Level--; }
        return Level;
    }
    /// <summary>
    /// 1 フレームの切り替え（Omni の VirtualObjectController.Tick と同じ動き）。望む段 <paramref name="Desired"/> は、初回はヒステリシスなし、2 回目からはヒステリシスつきで、準備を問わずに決める
    /// （呼び出し側は、望む段の準備を進めてよい）。
    /// <list type="bullet">
    /// <item>上がるとき（初回を含む）: 今より上で望む段まで、準備できたいちばん高い段へ。無ければ今の段のまま（初回は段 0）。</item>
    /// <item>下がるとき: 望む段から今の段の手前まで、準備できたいちばん低い段へ。無ければ今の段のまま。</item>
    /// </list>
    /// </summary>
    /// <returns>描く段。</returns>
    public static int Step(ReadOnlySpan<float>Enter,float Hysteresis,float Pixels,int Current,bool Started,uint ReadyMask,out int Desired){
        Desired=Started?Select(Enter,Hysteresis,Pixels,Current):Ideal(Enter,Pixels);
        Current=Started?Math.Clamp(Current,0,Enter.Length):0;
        if(Desired>Current||!Started){
            for(var Level=Desired;Level>Current;Level--){
                if(IsReady(ReadyMask,Level)){ return Level; }
            }
            return Current;
        }
        for(var Level=Desired;Level<Current;Level++){
            if(IsReady(ReadyMask,Level)){ return Level; }
        }
        return Current;
    }
    private static bool IsReady(uint ReadyMask,int Level)=>Level==0||(ReadyMask&(1u<<Level))!=0;
    private static float Clean(float Pixels)=>float.IsNaN(Pixels)||Pixels<0f?0f:Pixels;
}
