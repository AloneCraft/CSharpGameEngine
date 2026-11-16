namespace CSharpGameEngine.Runtime.Animation;
/// <summary>
/// 規則格子上の2軸ブレンドスペース（UE の BS_Idle_Walk_Run、ADR-0078/M0107）。各格子点をサンプルとして扱い、問い合わせ点が入るセルを求め、その点を含む三角形の3頂点間で
/// 重みを分配する。UE が格子を三角形分割するのと同じ方式で、X 軸の左半分にあるセルは左下から右上、右半分のセルは右下から左上へ対角線を引く。この左右対称な分割により、前進方向の列が共有辺になる。重み
/// は毎秒 <see cref="InterpolationSpeed"/> の速さで目標に近づく（UE の TargetWeightInterpolationSpeedPerSec）。入力が急に変わって
/// もクリップを瞬時に切り替えず、滑らかに遷移する。サンプル <c>Row * ColumnCount + Column</c> の位置は (<see cref="XAxis"/>[Column]、
/// <see cref="YAxis"/>[Row])。
/// </summary>
public sealed class BlendSpace2D {
    private readonly float[]XValues;
    private readonly float[]YValues;
    private readonly float[]Smoothed;
    private readonly float[]Target;
    private bool Initialized;
    public BlendSpace2D(ReadOnlySpan<float>XAxis,ReadOnlySpan<float>YAxis,float InterpolationSpeed=5f){
        if(XAxis.Length<2||YAxis.Length<2){
            throw new ArgumentException("A blend space needs at least two grid values on each axis.");
        }
        CheckAscending(XAxis,nameof(XAxis));
        CheckAscending(YAxis,nameof(YAxis));
        if(!float.IsFinite(InterpolationSpeed)||InterpolationSpeed<0f){
            throw new ArgumentOutOfRangeException(nameof(InterpolationSpeed),"The interpolation speed must be finite and not negative.");
        }
        this.XValues=XAxis.ToArray();
        this.YValues=YAxis.ToArray();
        this.InterpolationSpeed=InterpolationSpeed;
        this.Smoothed=new float[XAxis.Length*YAxis.Length];
        this.Target=new float[this.Smoothed.Length];
    }
    public ReadOnlySpan<float>XAxis=>this.XValues;
    public ReadOnlySpan<float>YAxis=>this.YValues;
    public int ColumnCount=>this.XValues.Length;
    public int RowCount=>this.YValues.Length;
    public int SampleCount=>this.Smoothed.Length;
    /// <summary>0なら目標値に瞬時に切り替える。それ以外では、指数的に近づき、残りの距離のうち毎秒およそこの割合を移動する。</summary>
    public float InterpolationSpeed { get; }
    /// <summary>各サンプルの現在の（平滑化済み）重み。最初の <see cref="Update"/> 後は合計が1になる。</summary>
    public ReadOnlySpan<float>Weights=>this.Smoothed;
    public int SampleIndex(int Column,int Row)=>(Row*this.XValues.Length)+Column;
    /// <summary>平滑化前に、(<paramref name="X"/>、<paramref name="Y"/>) の問い合わせが要求する重み。入力は格子の範囲に収める。</summary>
    public void TargetWeights(float X,float Y,Span<float>Weights){
        if(Weights.Length<this.SampleCount){
            throw new ArgumentException($"The blend space has {this.SampleCount} samples.",nameof(Weights));
        }
        Weights[..this.SampleCount].Clear();
        X=float.IsFinite(X)?Math.Clamp(X,this.XValues[0],this.XValues[^1]):this.XValues[0];
        Y=float.IsFinite(Y)?Math.Clamp(Y,this.YValues[0],this.YValues[^1]):this.YValues[0];
        var Column=Cell(this.XValues,X);
        var Row=Cell(this.YValues,Y);
        var X0=this.XValues[Column];
        var X1=this.XValues[Column+1];
        var Y0=this.YValues[Row];
        var Y1=this.YValues[Row+1];
        var U=(X-X0)/(X1-X0);
        var V=(Y-Y0)/(Y1-Y0);
        var LowerLeft=this.SampleIndex(Column,Row);
        var LowerRight=this.SampleIndex(Column+1,Row);
        var UpperLeft=this.SampleIndex(Column,Row+1);
        var UpperRight=this.SampleIndex(Column+1,Row+1);
        var MiddleX=(this.XValues[0]+this.XValues[^1])*0.5f;
        var LeftHalf=(X0+X1)*0.5f<MiddleX;
        if(LeftHalf){
            // 左下から右上への対角線。左下の三角形はその下側の点を含む。
            if(U>=V){
                Weights[LowerLeft]=1f-U;
                Weights[LowerRight]=U-V;
                Weights[UpperRight]=V;
            } else {
                Weights[LowerLeft]=1f-V;
                Weights[UpperLeft]=V-U;
                Weights[UpperRight]=U;
            }
        } else {
            // 右下から左上への対角線。右下の三角形はその下側の点を含む。
            if(U+V<=1f){
                Weights[LowerLeft]=MathF.Max(1f-U-V,0f);
                Weights[LowerRight]=U;
                Weights[UpperLeft]=V;
            } else {
                Weights[UpperRight]=MathF.Max(U+V-1f,0f);
                Weights[LowerRight]=1f-V;
                Weights[UpperLeft]=1f-U;
            }
        }
    }
    /// <summary><paramref name="DeltaSeconds"/> の間に、各サンプルの重みを問い合わせ値へ近づけて再正規化する。初回呼び出しでは即座に切り替える。</summary>
    public void Update(float DeltaSeconds,float X,float Y){
        this.TargetWeights(X,Y,this.Target);
        if(!this.Initialized||this.InterpolationSpeed<=0f){
            this.Target.CopyTo(this.Smoothed,0);
            this.Initialized=true;
            return;
        }
        var Step=Math.Clamp((float.IsFinite(DeltaSeconds)?Math.Max(DeltaSeconds,0f):0f)*this.InterpolationSpeed,0f,1f);
        var Total=0f;
        for(var Index=0;Index<this.Smoothed.Length;Index++){
            var Moved=this.Smoothed[Index]+((this.Target[Index]-this.Smoothed[Index])*Step);
            // UE の FInterpTo は値が近づくと停止する。ほぼ到達済みの重みを到達済みとみなして、遷移を完了させる。
            this.Smoothed[Index]=MathF.Abs(this.Target[Index]-Moved)<=1e-4f?this.Target[Index]:Moved;
            Total+=this.Smoothed[Index];
        }
        if(!(Total>1e-6f)){
            this.Target.CopyTo(this.Smoothed,0);
            return;
        }
        for(var Index=0;Index<this.Smoothed.Length;Index++){
            this.Smoothed[Index]/=Total;
        }
    }
    /// <summary>平滑化状態を破棄する。次の <see cref="Update"/> で問い合わせ値に即座に切り替える。</summary>
    public void Reset()=>this.Initialized=false;
    private static int Cell(float[]Axis,float Value){
        var Low=0;
        var High=Axis.Length-1;
        while(High-Low>1){
            var Middle=(Low+High)/2;
            if(Axis[Middle]<=Value){
                Low=Middle;
            } else {
                High=Middle;
            }
        }
        return Low;
    }
    private static void CheckAscending(ReadOnlySpan<float>Axis,string Name){
        for(var Index=0;Index<Axis.Length;Index++){
            if(!float.IsFinite(Axis[Index])||(Index>0&&!(Axis[Index]>Axis[Index-1]))){
                throw new ArgumentException("A blend space axis must be finite and strictly ascending.",Name);
            }
        }
    }
}
