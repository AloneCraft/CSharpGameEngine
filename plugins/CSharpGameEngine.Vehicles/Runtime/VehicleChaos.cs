using CSharpGameEngine.Numerics;
namespace CSharpGameEngine.Vehicles;
/// <summary>
/// UE の ChaosVehiclesCore(UE 5.7 のソース: EngineSystem、TransmissionSystem、SteeringSystem/SteeringUtility、SuspensionSystem/SuspensionUtility、WheelSystem、
/// AerodynamicsSystem、VehicleUtility)の式を、<see cref="LogicalScalar"/> で決定的に解く部品(ADR-0078/M0109)。単位は m・kg・s(UE の内部の cm の式は、
/// 同じ値になるように m へ直した)。
/// </summary>
public static class VehicleChaos {
    public static readonly LogicalScalar Gravity=LogicalScalar.ParseInvariant("9.80665");
    /// <summary>UE の RealWorldConsts::AirDensity(kg/m³)。</summary>
    public static readonly LogicalScalar AirDensity=LogicalScalar.ParseInvariant("1.225");
    /// <summary>UE の CmSToMPH の係数を m/s に直したもの(2236.94185 / 1000)。</summary>
    public static readonly LogicalScalar MetersPerSecondToMph=LogicalScalar.ParseInvariant("2.23694185");
    public static readonly LogicalScalar DegreesPerRadian=LogicalScalar.FromInt64(180)/LogicalMath.Pi;
    public static readonly LogicalScalar RadiansPerDegree=LogicalMath.Pi/LogicalScalar.FromInt64(180);
    /// <summary>rpm と rad/s の換算(UE の RPMToOmega: rpm × π / 30)。</summary>
    public static readonly LogicalScalar RpmPerRadianPerSecond=LogicalScalar.FromInt64(30)/LogicalMath.Pi;
    public static readonly LogicalScalar Half=LogicalScalar.One/LogicalScalar.FromInt64(2);
    /// <summary>UE の SMALL_NUMBER(1e-8)。</summary>
    public static readonly LogicalScalar SmallNumber=LogicalScalar.ParseInvariant("0.00000001");
    /// <summary>UE の KINDA_SMALL_NUMBER(1e-4)。</summary>
    public static readonly LogicalScalar KindaSmallNumber=LogicalScalar.ParseInvariant("0.0001");
    public static LogicalScalar Abs(LogicalScalar Value)=>Value<LogicalScalar.Zero?-Value:Value;
    public static LogicalScalar Min(LogicalScalar Left,LogicalScalar Right)=>Left<Right?Left:Right;
    public static LogicalScalar Max(LogicalScalar Left,LogicalScalar Right)=>Left>Right?Left:Right;
    public static LogicalScalar Clamp(LogicalScalar Value,LogicalScalar Minimum,LogicalScalar Maximum)=>Value<Minimum?Minimum:Value>Maximum?Maximum:Value;
    public static LogicalScalar Sqrt(LogicalScalar Value)=>LogicalScalar.TrySquareRoot(Max(Value,LogicalScalar.Zero),out var Result)?Result:throw new OverflowException();
    public static LogicalScalar Atan2(LogicalScalar Y,LogicalScalar X)=>LogicalMath.TryAtan2(Y,X,out var Result)?Result:throw new OverflowException();
    public static void SinCos(LogicalScalar Radians,out LogicalScalar Sin,out LogicalScalar Cos){
        if(!LogicalMath.TrySinCos(Radians,out Sin,out Cos)){ throw new OverflowException(); }
    }
    /// <summary>UE の FRichCurve::Eval(線形のキー、外側は端の値)。同じ時刻のキーが続くときは後のキーを使う。</summary>
    public static LogicalScalar Evaluate(VehicleCurveKey[]Keys,LogicalScalar Time){
        if(Keys.Length==0){ return LogicalScalar.Zero; }
        if(Time<=Keys[0].Time){ return Keys[0].Value; }
        for(var Index=1;Index<Keys.Length;Index++){
            var Next=Keys[Index];
            if(Time<Next.Time){
                var Previous=Keys[Index-1];
                var Span=Next.Time-Previous.Time;
                return Span<=LogicalScalar.Zero?Next.Value:Previous.Value+(Next.Value-Previous.Value)*((Time-Previous.Time)/Span);
            }
        }
        return Keys[^1].Value;
    }
    /// <summary>キーの値の最大(UE の GetValueRange の MaxValue)。0 以下なら 1。</summary>
    public static LogicalScalar MaximumValue(VehicleCurveKey[]Keys){
        var Result=LogicalScalar.Zero;
        var Found=false;
        foreach(var Key in Keys){
            if(!Found||Key.Value>Result){ Result=Key.Value; }
            Found=true;
        }
        return Found&&Result>LogicalScalar.Zero?Result:LogicalScalar.One;
    }
    /// <summary>
    /// UE が設定を物理へ渡すときの標本(FillEngineSetup・FillSteeringSetup): 0 から <paramref name="MaximumTime"/> を 20 等分した 21 点で、値を最大値で割る。
    /// </summary>
    public static LogicalScalar[]Sample(VehicleCurveKey[]Keys,LogicalScalar MaximumTime){
        var Samples=new LogicalScalar[21];
        var Maximum=MaximumValue(Keys);
        for(var Index=0;Index<Samples.Length;Index++){
            Samples[Index]=Evaluate(Keys,MaximumTime*LogicalScalar.FromInt64(Index)/LogicalScalar.FromInt64(20))/Maximum;
        }
        return Samples;
    }
    /// <summary>UE の FNormalisedGraph::GetValue: 標本を等間隔に並べて直線で補い、最後の点より先は最後の値。</summary>
    public static LogicalScalar NormalisedGraphValue(LogicalScalar[]Samples,LogicalScalar X,LogicalScalar MaximumX,LogicalScalar MaximumY){
        if(MaximumX<=LogicalScalar.Zero){ return LogicalScalar.Zero; }
        var Step=MaximumX/LogicalScalar.FromInt64(Samples.Length-1);
        var Scaled=X/Step;
        var StartIndex=Scaled<LogicalScalar.Zero?0:(int)Math.Min(int.MaxValue,(long)(Scaled.Raw>>64));
        LogicalScalar Value;
        if(StartIndex>=Samples.Length-1){
            Value=Samples[^1];
        }
        else {
            var Ramp=(X-LogicalScalar.FromInt64(StartIndex)*Step)/Step;
            Value=Samples[StartIndex]*(LogicalScalar.One-Ramp)+Samples[StartIndex+1]*Ramp;
        }
        return Value*MaximumY;
    }
    /// <summary>UE の FGraph::EvaluateY: 0 から <paramref name="MaximumX"/> に等間隔に並ぶ標本を直線で補う。外側は端の値。</summary>
    public static LogicalScalar GraphValue(LogicalScalar[]Samples,LogicalScalar X,LogicalScalar MaximumX){
        if(Samples.Length==0){ return LogicalScalar.Zero; }
        if(X<=LogicalScalar.Zero||MaximumX<=LogicalScalar.Zero){ return Samples[0]; }
        if(X>=MaximumX){ return Samples[^1]; }
        var Step=MaximumX/LogicalScalar.FromInt64(Samples.Length-1);
        var Scaled=X/Step;
        var StartIndex=(int)(long)(Scaled.Raw>>64);
        if(StartIndex>=Samples.Length-1){ return Samples[^1]; }
        var Ramp=(X-LogicalScalar.FromInt64(StartIndex)*Step)/Step;
        return Samples[StartIndex]*(LogicalScalar.One-Ramp)+Samples[StartIndex+1]*Ramp;
    }
    /// <summary>UE の FVehicleInputRateConfig::InterpInputValue: 絶対値が増えるときは RiseRate、減るときは FallRate で、1 tick に動ける量を切る。</summary>
    public static LogicalScalar InterpInput(in VehicleInputRate Rate,LogicalScalar DeltaSeconds,LogicalScalar Current,LogicalScalar Target){
        var Delta=Target-Current;
        var Rising=(Delta>LogicalScalar.Zero)==(Current>LogicalScalar.Zero)||(!Delta.IsZero&&Current.IsZero);
        var MaximumDelta=DeltaSeconds*(Rising?Rate.RiseRate:Rate.FallRate);
        return Current+Clamp(Delta,-MaximumDelta,MaximumDelta);
    }
    /// <summary>UE の CalcControlFunction(線形か、符号を保った二乗)。</summary>
    public static LogicalScalar ControlFunction(in VehicleInputRate Rate,LogicalScalar Value)=>Rate.Curve==VehicleInputCurve.Squared
        ?(Value<LogicalScalar.Zero?-(Value*Value):Value*Value)
        :Value;
    /// <summary>UE の FVehicleUtility::CalculateSlipAngle: 横の速さが 0.05 cm/s を超えるときだけ |atan2(横, 前)| を 0 から π/2 に折り返した角(rad)。</summary>
    public static LogicalScalar SlipAngle(LogicalScalar Lateral,LogicalScalar Longitudinal){
        if(Abs(Lateral)<=LateralSpeedThreshold){ return LogicalScalar.Zero; }
        var Value=Abs(Atan2(Lateral,Longitudinal));
        return Value>LogicalMath.HalfPi?LogicalMath.Pi-Value:Value;
    }
    private static readonly LogicalScalar LateralSpeedThreshold=LogicalScalar.ParseInvariant("0.0005");
    /// <summary>
    /// UE の ComputeSprungMasses: 質量の釣り合い(Σ mᵢ xᵢ = 0、Σ mᵢ yᵢ = 0、Σ mᵢ = M)を満たし、ばらつきが最小の配り方。位置は重心からの水平の座標(x は前、y は横)。
    /// 1 つなら全部、2 つなら線分への射影の比、3 つ以上ならラグランジュの未定乗数の解(UE と同じ)。解けなければ等分。
    /// </summary>
    public static LogicalScalar[]SprungMasses(ReadOnlySpan<(LogicalScalar X,LogicalScalar Y)>Positions,LogicalScalar TotalMass){
        var Count=Positions.Length;
        var Masses=new LogicalScalar[Count];
        if(Count==0){ return Masses; }
        var Even=TotalMass/LogicalScalar.FromInt64(Count);
        if(Count==1){
            Masses[0]=TotalMass;
            return Masses;
        }
        if(Count==2){
            var DifferenceX=Positions[1].X-Positions[0].X;
            var DifferenceY=Positions[1].Y-Positions[0].Y;
            var Distance=Sqrt(DifferenceX*DifferenceX+DifferenceY*DifferenceY);
            if(Distance<=SmallNumber){
                Masses[0]=Masses[1]=TotalMass*Half;
                return Masses;
            }
            var Along=(DifferenceX*Positions[0].X+DifferenceY*Positions[0].Y)/Distance;
            Masses[0]=-TotalMass*Along/Distance;
            Masses[1]=TotalMass-Masses[0];
            if(Masses[0]<LogicalScalar.Zero||Masses[1]<LogicalScalar.Zero){ Masses[0]=Masses[1]=Even; }
            return Masses;
        }
        // (Lᵀ L) λ = Lᵀ v - 2u、m = (v - L λ) / 2(L の行は (xᵢ, yᵢ, 1)、v = 2 m̄、u = (0, 0, M))。
        LogicalScalar SumX=LogicalScalar.Zero,SumY=LogicalScalar.Zero,XX=LogicalScalar.Zero,YY=LogicalScalar.Zero,XY=LogicalScalar.Zero;
        foreach(var (X,Y) in Positions){
            SumX+=X;
            SumY+=Y;
            XX+=X*X;
            YY+=Y*Y;
            XY+=X*Y;
        }
        var N=LogicalScalar.FromInt64(Count);
        var A=new LogicalScalar[3,3]{{XX,XY,SumX},{XY,YY,SumY},{SumX,SumY,N}};
        var TwoAverage=Even*LogicalScalar.FromInt64(2);
        var B0=TwoAverage*SumX;
        var B1=TwoAverage*SumY;
        var B2=TwoAverage*N-LogicalScalar.FromInt64(2)*TotalMass;
        if(!TrySolve3(A,B0,B1,B2,out var L0,out var L1,out var L2)){
            Array.Fill(Masses,Even);
            return Masses;
        }
        for(var Index=0;Index<Count;Index++){
            Masses[Index]=(TwoAverage-(L0*Positions[Index].X+L1*Positions[Index].Y+L2))*Half;
        }
        return Masses;
    }
    /// <summary>3 元の連立一次方程式をクラメルの公式で解く。行列式が 0 に近ければ偽。</summary>
    private static bool TrySolve3(LogicalScalar[,]A,LogicalScalar B0,LogicalScalar B1,LogicalScalar B2,out LogicalScalar X0,out LogicalScalar X1,out LogicalScalar X2){
        X0=X1=X2=default;
        var Determinant=Determinant3(A[0,0],A[0,1],A[0,2],A[1,0],A[1,1],A[1,2],A[2,0],A[2,1],A[2,2]);
        if(Abs(Determinant)<=SmallNumber){ return false; }
        X0=Determinant3(B0,A[0,1],A[0,2],B1,A[1,1],A[1,2],B2,A[2,1],A[2,2])/Determinant;
        X1=Determinant3(A[0,0],B0,A[0,2],A[1,0],B1,A[1,2],A[2,0],B2,A[2,2])/Determinant;
        X2=Determinant3(A[0,0],A[0,1],B0,A[1,0],A[1,1],B1,A[2,0],A[2,1],B2)/Determinant;
        return true;
    }
    private static LogicalScalar Determinant3(LogicalScalar A,LogicalScalar B,LogicalScalar C,LogicalScalar D,LogicalScalar E,LogicalScalar F,LogicalScalar G,LogicalScalar H,LogicalScalar I)=>
        A*(E*I-F*H)-B*(D*I-F*G)+C*(D*H-E*G);
}
/// <summary>
/// UE の FAckermannSim(SteeringSystem.cpp と SteeringUtility.h): 左右の前輪をつなぐ棒の幾何から、内側と外側の舵角を求める。幾何は UE と同じく cm で解く
/// (入力の倍率 10 が cm なので、m では同じ角にならない)。
/// </summary>
public sealed class VehicleAckermann {
    private static readonly LogicalScalar InputScaling=LogicalScalar.FromInt64(10);
    private readonly (LogicalScalar X,LogicalScalar Y)C2;
    private readonly LogicalScalar R1;
    private readonly LogicalScalar R2;
    private readonly LogicalScalar RestAngle;
    /// <summary>入力 1 のときの左右の大きい方の角(度)。</summary>
    public LogicalScalar MaximumAngle { get; }
    /// <param name="TrackWidthCentimeters">左右の車輪の間(cm。UE の CalculateWheelLayoutDimensions の Y)。</param>
    /// <param name="WheelBaseCentimeters">前後の車輪の間(cm。同じく X。前後の最大の距離の 2 倍)。</param>
    public VehicleAckermann(LogicalScalar TrackWidthCentimeters,LogicalScalar WheelBaseCentimeters){
        var Beta=VehicleChaos.Atan2(TrackWidthCentimeters*VehicleChaos.Half,WheelBaseCentimeters);
        var Rod=TrackWidthCentimeters/LogicalScalar.FromInt64(8);
        VehicleChaos.SinCos(Beta,out var SinBeta,out var CosBeta);
        var H=Rod*CosBeta;
        var S=TrackWidthCentimeters-LogicalScalar.FromInt64(2)*Rod*SinBeta;
        this.C2=(TrackWidthCentimeters*VehicleChaos.Half,H);
        this.R1=S*VehicleChaos.Half;
        this.R2=Rod;
        this.RestAngle=this.LinkAngle(false,LogicalScalar.Zero);
        this.Angles(LogicalScalar.One,out var Left,out var Right);
        this.MaximumAngle=VehicleChaos.Max(VehicleChaos.Abs(Left),VehicleChaos.Abs(Right));
    }
    /// <summary>UE の CalculateAkermannAngle(Input): 左と右の車輪の角(度)。</summary>
    public void Angles(LogicalScalar Input,out LogicalScalar Left,out LogicalScalar Right){
        Left=this.LinkAngle(true,Input*InputScaling)-this.RestAngle;
        Right=this.LinkAngle(false,Input*InputScaling)-this.RestAngle;
    }
    /// <summary>
    /// UE の GetSteeringAngle の Ackermann: 入力の符号を反転して左右の角を求め、車輪が左(<paramref name="WheelSide"/> &lt; 0)なら左の角の符号を反転し、
    /// <paramref name="MaximumSteerDegrees"/> / <see cref="MaximumAngle"/> を掛ける。右に切ると正。
    /// </summary>
    public LogicalScalar SteeringAngle(LogicalScalar Input,LogicalScalar MaximumSteerDegrees,LogicalScalar WheelSide){
        this.Angles(-Input,out var Left,out var Right);
        var Angle=WheelSide<LogicalScalar.Zero?-Left:Right;
        return this.MaximumAngle.IsZero?LogicalScalar.Zero:Angle*(MaximumSteerDegrees/this.MaximumAngle);
    }
    private LogicalScalar LinkAngle(bool Flip,LogicalScalar Input){
        if(Flip){ Input=-Input; }
        var C2RelativeX=this.C2.X-Input;
        var C2RelativeY=this.C2.Y;
        var Angle=VehicleChaos.Atan2(C2RelativeY,C2RelativeX);
        var Distance=VehicleChaos.Sqrt(C2RelativeX*C2RelativeX+C2RelativeY*C2RelativeY);
        IntersectTwoCircles(this.R1,this.R2,Distance,out var IntersectionX,out var IntersectionY);
        IntersectionY=-IntersectionY;
        VehicleChaos.SinCos(Angle,out var Sin,out var Cos);
        var ArmX=IntersectionX*Cos-IntersectionY*Sin;
        var ArmY=IntersectionX*Sin+IntersectionY*Cos;
        var PointX=Input+ArmX;
        var PointY=ArmY;
        var WheelArmX=this.C2.X-PointX;
        var WheelArmY=this.C2.Y-PointY;
        return VehicleChaos.Atan2(WheelArmX,WheelArmY)*VehicleChaos.DegreesPerRadian;
    }
    private static void IntersectTwoCircles(LogicalScalar R1,LogicalScalar R2,LogicalScalar Distance,out LogicalScalar X,out LogicalScalar Y){
        X=Y=LogicalScalar.Zero;
        if(Distance>R1+R2||Distance<R1-R2||Distance.IsZero){ return; }
        var TwoDistance=Distance*LogicalScalar.FromInt64(2);
        var R1Square=R1*R1;
        var DistanceSquare=Distance*Distance;
        var Numerator=DistanceSquare-R2*R2+R1Square;
        X=Numerator/TwoDistance;
        Y=VehicleChaos.Sqrt(LogicalScalar.FromInt64(4)*DistanceSquare*R1Square-Numerator*Numerator)/TwoDistance;
    }
}
/// <summary>
/// 1 台の車の <see cref="VehicleSetup"/> と車輪の取り付けから前もって求める値(UE の SetupVehicle・SetupSuspension に当たる)。
/// </summary>
public sealed class VehicleTuning {
    public VehicleSetup Setup { get; }
    public VehicleWheelMount[]Mounts { get; }
    public int WheelCount { get; }
    public VehicleWheelSetup[]Wheels { get; }
    public bool[]EngineEnabled { get; }
    public LogicalScalar[]TorqueRatio { get; }
    public LogicalScalar[]SprungMass { get; }
    /// <summary>ダンパーの係数(N·s/m。UE の ComputeDamping、縮みと伸びで同じ)。</summary>
    public LogicalScalar[]Damping { get; }
    /// <summary>静止の荷重(N。ばね上の質量 × 重力)。</summary>
    public LogicalScalar[]RestingForce { get; }
    /// <summary>車輪の回転の慣性(kg·m²。0.5 × 質量 × 半径²)。</summary>
    public LogicalScalar[]Inertia { get; }
    /// <summary>同じ車軸の左右の組(ロールバー)。</summary>
    public (int First,int Second)[]Axles { get; }
    public LogicalScalar[]TorqueSamples { get; }
    public LogicalScalar[]SteeringSamples { get; }
    public LogicalScalar SteeringCurveMaximum { get; }
    public VehicleAckermann Ackermann { get; }
    public bool HasEngine { get; }
    /// <summary>空気抵抗の 0.5 × 面積 × 係数(UE の EffectiveDragConstant)と、ダウンフォースの同じ値。</summary>
    public LogicalScalar DragConstant { get; }
    public LogicalScalar DownforceConstant { get; }
    public VehicleTuning(VehicleSetup Setup,VehicleWheelMount[]Mounts){
        this.Setup=Setup;
        this.Mounts=Mounts;
        var Count=Math.Min(Mounts.Length,4);
        this.WheelCount=Count;
        this.Wheels=new VehicleWheelSetup[Count];
        this.EngineEnabled=new bool[Count];
        this.TorqueRatio=new LogicalScalar[Count];
        this.Inertia=new LogicalScalar[Count];
        Span<(LogicalScalar X,LogicalScalar Y)>Positions=stackalloc (LogicalScalar,LogicalScalar)[Count];
        LogicalScalar MaximumForward=LogicalScalar.Zero,MaximumSide=LogicalScalar.Zero;
        for(var Index=0;Index<Count;Index++){
            var Mount=Mounts[Index];
            var Wheel=Mount.Axle==VehicleAxle.Front?Setup.FrontWheel:Setup.RearWheel;
            this.Wheels[Index]=Wheel;
            this.EngineEnabled[Index]=Setup.Differential switch {
                VehicleDifferential.AllWheelDrive=>true,
                VehicleDifferential.FrontWheelDrive=>Mount.Axle==VehicleAxle.Front,
                VehicleDifferential.RearWheelDrive=>Mount.Axle==VehicleAxle.Rear,
                _=>Wheel.AffectedByEngine,
            };
            this.Inertia[Index]=VehicleChaos.Half*Wheel.Mass*Wheel.Radius*Wheel.Radius;
            // 水平の座標は x = 前、y = 右(UE の X と Y。エンジンの右は -R)。
            Positions[Index]=(Mount.Position.F,-Mount.Position.R);
            MaximumForward=VehicleChaos.Max(MaximumForward,VehicleChaos.Abs(Mount.Position.F));
            MaximumSide=VehicleChaos.Max(MaximumSide,VehicleChaos.Abs(Mount.Position.R));
        }
        var Driven=this.EngineEnabled.Count(static Enabled=>Enabled);
        for(var Index=0;Index<Count;Index++){
            if(!this.EngineEnabled[Index]){ continue; }
            // UE の GetTorqueRatioForWheel: 四輪駆動は前後の配分を車軸の車輪の数で割り、二輪駆動は駆動輪の数で割る。
            if(Setup.Differential==VehicleDifferential.AllWheelDrive){
                var Axle=this.Mounts[Index].Axle;
                var OnAxle=0;
                for(var Other=0;Other<Count;Other++){ if(this.Mounts[Other].Axle==Axle){ OnAxle++; } }
                var Split=Axle==VehicleAxle.Front?LogicalScalar.One-Setup.FrontRearSplit:Setup.FrontRearSplit;
                this.TorqueRatio[Index]=Split/LogicalScalar.FromInt64(OnAxle);
            }
            else {
                this.TorqueRatio[Index]=LogicalScalar.One/LogicalScalar.FromInt64(Driven);
            }
        }
        this.SprungMass=VehicleChaos.SprungMasses(Positions,Setup.Mass);
        this.Damping=new LogicalScalar[Count];
        this.RestingForce=new LogicalScalar[Count];
        for(var Index=0;Index<Count;Index++){
            var Mass=VehicleChaos.Max(this.SprungMass[Index],LogicalScalar.Zero);
            var Ratio=VehicleChaos.Clamp(this.Wheels[Index].SuspensionDampingRatio,LogicalScalar.Zero,LogicalScalar.One);
            this.Damping[Index]=Ratio*LogicalScalar.FromInt64(2)*VehicleChaos.Sqrt(this.Wheels[Index].SpringRate*Mass);
            this.RestingForce[Index]=Mass*VehicleChaos.Gravity;
        }
        List<(int,int)>Axles=[];
        foreach(var Axle in new[]{VehicleAxle.Front,VehicleAxle.Rear}){
            var First=-1;
            var Second=-1;
            var OnAxle=0;
            for(var Index=0;Index<Count;Index++){
                if(this.Mounts[Index].Axle!=Axle){ continue; }
                OnAxle++;
                if(First<0){ First=Index; } else if(Second<0){ Second=Index; }
            }
            // UE のロールバーは車輪が 2 つの車軸だけ。
            if(OnAxle==2){ Axles.Add((First,Second)); }
        }
        this.Axles=[.. Axles];
        this.HasEngine=Setup.Engine.TorqueCurve.Length>0;
        this.TorqueSamples=VehicleChaos.Sample(Setup.Engine.TorqueCurve,Setup.Engine.MaxRpm);
        var SteeringKeys=Setup.Steering.SteeringCurve;
        this.SteeringCurveMaximum=SteeringKeys.Length>0?SteeringKeys[^1].Time:LogicalScalar.Zero;
        this.SteeringSamples=SteeringKeys.Length>0?VehicleChaos.Sample(SteeringKeys,this.SteeringCurveMaximum):[LogicalScalar.One];
        var Hundred=LogicalScalar.FromInt64(100);
        this.Ackermann=new VehicleAckermann(MaximumSide*LogicalScalar.FromInt64(2)*Hundred,MaximumForward*LogicalScalar.FromInt64(2)*Hundred);
        var Area=Setup.ChassisWidth*Setup.ChassisHeight;
        this.DragConstant=VehicleChaos.Half*Area*Setup.DragCoefficient;
        this.DownforceConstant=VehicleChaos.Half*Area*Setup.DownforceCoefficient;
    }
    /// <summary>UE の FSimpleEngineSim::GetTorqueFromRPM(アイドルから最大の回転数に丸め、最大の回転数ちょうどなら 0)。</summary>
    public LogicalScalar EngineTorqueAt(LogicalScalar Rpm){
        var Engine=this.Setup.Engine;
        if(VehicleChaos.Abs(Rpm-Engine.MaxRpm)<LogicalScalar.One||Engine.MaxRpm.IsZero){ return LogicalScalar.Zero; }
        Rpm=VehicleChaos.Clamp(Rpm,Engine.IdleRpm,Engine.MaxRpm);
        return VehicleChaos.NormalisedGraphValue(this.TorqueSamples,Rpm,Engine.MaxRpm,Engine.MaxTorque);
    }
    /// <summary>UE の GetGearRatio: 前進は ギア比 × 最終比、後退は負、ニュートラルは 0。範囲外のギアは丸める。</summary>
    public LogicalScalar GearRatio(int Gear){
        var Transmission=this.Setup.Transmission;
        Gear=Math.Clamp(Gear,-Transmission.ReverseGearRatios.Length,Transmission.ForwardGearRatios.Length);
        if(Gear>0){ return Transmission.ForwardGearRatios[Gear-1]*Transmission.FinalRatio; }
        if(Gear<0){ return -(Transmission.ReverseGearRatios[-Gear-1]*Transmission.FinalRatio); }
        return LogicalScalar.Zero;
    }
    /// <summary>UE の速さによるハンドルの効き(横軸はマイル毎時)。</summary>
    public LogicalScalar SteeringScale(LogicalScalar ForwardSpeed)=>VehicleChaos.GraphValue(this.SteeringSamples,ForwardSpeed*VehicleChaos.MetersPerSecondToMph,this.SteeringCurveMaximum);
    /// <summary>
    /// UE の GetSteeringAngle: 入力(-1 から 1、速さの効きを掛けたもの)と車輪の側から、その車輪の舵角(度、右が正)。舵を切らない車輪は 0。
    /// </summary>
    public LogicalScalar WheelSteeringAngle(int Wheel,LogicalScalar Input){
        var Setup=this.Wheels[Wheel];
        if(!Setup.AffectedBySteering){ return LogicalScalar.Zero; }
        var Maximum=Setup.MaxSteerAngleDegrees;
        var WheelSide=-this.Mounts[Wheel].Position.R;
        switch(this.Setup.Steering.Type){
            case VehicleSteeringType.Ackermann:
                return this.Ackermann.SteeringAngle(Input,Maximum,WheelSide);
            case VehicleSteeringType.AngleRatio:
                var Outside=Input*WheelSide>LogicalScalar.Zero;
                return Input*(Outside?Maximum:Maximum*this.Setup.Steering.AngleRatio);
            default:
                return Maximum*Input;
        }
    }
}
