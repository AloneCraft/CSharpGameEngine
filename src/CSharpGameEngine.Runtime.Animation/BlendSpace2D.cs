namespace CSharpGameEngine.Runtime.Animation;
/// <summary>
/// A two-axis blend space on a regular grid (UE's BS_Idle_Walk_Run, ADR-0078/M0107). Each grid point is a sample; a query lands in one cell
/// and is shared among the three corners of the triangle that holds it, exactly as UE triangulates the grid: the cells in the left half
/// of the X axis are cut from lower-left to upper-right, those in the right half from lower-right to upper-left (mirror-symmetric, which
/// keeps the forward column the shared edge). The weights smooth toward their targets at <see cref="InterpolationSpeed"/> per second
/// (UE's TargetWeightInterpolationSpeedPerSec), so a sudden turn of the input fades between the clips instead of snapping.
/// Sample <c>Row * ColumnCount + Column</c> sits at (<see cref="XAxis"/>[Column], <see cref="YAxis"/>[Row]).
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
    /// <summary>0 snaps to the target; otherwise the fraction of the remaining distance covered per second is about this (an exponential approach).</summary>
    public float InterpolationSpeed { get; }
    /// <summary>The current (smoothed) weight of each sample; they sum to 1 after the first <see cref="Update"/>.</summary>
    public ReadOnlySpan<float>Weights=>this.Smoothed;
    public int SampleIndex(int Column,int Row)=>(Row*this.XValues.Length)+Column;
    /// <summary>The weights a query at (<paramref name="X"/>, <paramref name="Y"/>) asks for, before smoothing. The input is clamped to the grid.</summary>
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
            // Diagonal lower-left to upper-right: the lower-right triangle holds the points below it.
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
            // Diagonal lower-right to upper-left: the lower-left triangle holds the points below it.
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
    /// <summary>Moves every sample's weight toward the query's by <paramref name="DeltaSeconds"/> and renormalizes. The first call snaps.</summary>
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
            // UE's FInterpTo stops once it is close; a weight that has all but arrived is taken as arrived so the fade ends.
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
    /// <summary>Forgets the smoothing: the next <see cref="Update"/> snaps to its query.</summary>
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
