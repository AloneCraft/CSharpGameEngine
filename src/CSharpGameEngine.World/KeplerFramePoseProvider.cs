using CSharpGameEngine.Contracts;
using CSharpGameEngine.Numerics;
namespace CSharpGameEngine.World;
public readonly record struct ReferenceFrameTime(ulong Tick,LogicalScalar TimeScale) {
    public static ReferenceFrameTime Fixed(ulong Tick)=>new(Tick,LogicalScalar.One);
}
public readonly record struct KeplerOrbit(
    LogicalScalar SemiMajorAxis,
    LogicalScalar Eccentricity,
    LogicalScalar Inclination,
    LogicalScalar LongitudeOfAscendingNode,
    LogicalScalar ArgumentOfPeriapsis,
    LogicalScalar MeanAnomalyAtEpoch,
    LogicalScalar MeanMotionPerTick,
    ulong EpochTick) {
    public bool TryValidate(out string Error){
        if(this.SemiMajorAxis<=LogicalScalar.Zero){ Error="Semi-major axis must be positive.";return false; }
        if(this.Eccentricity<LogicalScalar.Zero||this.Eccentricity>=LogicalScalar.One){ Error="Elliptic eccentricity must be in [0,1).";return false; }
        if(this.MeanMotionPerTick<=LogicalScalar.Zero){ Error="Mean motion per tick must be positive.";return false; }
        if(!LogicalScalar.TrySubtract(LogicalScalar.One,this.Eccentricity,out var PeriapsisFactor)
            ||!LogicalScalar.TryMultiply(this.SemiMajorAxis,PeriapsisFactor,out var Periapsis)
            ||Periapsis<=LogicalScalar.Zero){ Error="Periapsis must be representable and positive.";return false; }
        Error=string.Empty;
        return true;
    }
}
public readonly record struct KeplerFrameState(FramePose Pose,LogicalVector3 Velocity,LogicalScalar MeanAnomaly,LogicalScalar EccentricAnomaly);
public enum KeplerPropagationErrorCode { None,InvalidFrame,MissingFrame,ArithmeticOverflow,NonConvergent }
public readonly record struct KeplerPropagationError(KeplerPropagationErrorCode Code,string Message) {
    public static KeplerPropagationError None=>new(KeplerPropagationErrorCode.None,string.Empty);
}
public sealed class KeplerFramePoseProvider {
    private const int MaximumSolverIterations=24;
    private const long SolverToleranceRaw=64;
    private static readonly LogicalScalar HighEccentricityThreshold=LogicalScalar.ParseInvariant("0.8");
    public KeplerFramePoseProvider(ReferenceFrameId Frame,KeplerOrbit Orbit){
        if(!Frame.IsValid){ throw new ArgumentException("A valid target frame is required.",nameof(Frame)); }
        if(!Orbit.TryValidate(out var Error)){ throw new ArgumentException(Error,nameof(Orbit)); }
        this.Frame=Frame;
        this.Orbit=Orbit;
    }
    public ReferenceFrameId Frame{get;}
    public KeplerOrbit Orbit{get;}
    public bool TryEvaluate(ReferenceFrameTime Time,out KeplerFrameState State,out KeplerPropagationError Error){
        State=default;
        Error=KeplerPropagationError.None;
        if(!LogicalScalar.TryMultiply(this.Orbit.MeanMotionPerTick,Time.TimeScale,out var EffectiveMotion)){
            return Fail(KeplerPropagationErrorCode.ArithmeticOverflow,"Scaled mean motion is not representable.",out Error);
        }
        var Reverse=Time.Tick<this.Orbit.EpochTick;
        var Elapsed=Reverse?this.Orbit.EpochTick-Time.Tick:Time.Tick-this.Orbit.EpochTick;
        if(Reverse&&!LogicalScalar.TrySubtract(LogicalScalar.Zero,EffectiveMotion,out EffectiveMotion)){
            return Fail(KeplerPropagationErrorCode.ArithmeticOverflow,"Reverse mean motion is not representable.",out Error);
        }
        if(!LogicalMath.TryAdvanceRadians(this.Orbit.MeanAnomalyAtEpoch,EffectiveMotion,Elapsed,out var MeanAnomaly)){
            return Fail(KeplerPropagationErrorCode.ArithmeticOverflow,"Mean anomaly is not representable.",out Error);
        }
        if(!TrySolveEccentricAnomaly(MeanAnomaly,this.Orbit.Eccentricity,out var EccentricAnomaly,out var Converged)){
            return Fail(KeplerPropagationErrorCode.ArithmeticOverflow,"Kepler arithmetic overflowed.",out Error);
        }
        if(!Converged){ return Fail(KeplerPropagationErrorCode.NonConvergent,"Kepler equation did not converge within the fixed iteration budget.",out Error); }
        if(!LogicalMath.TrySinCos(EccentricAnomaly,out var SinE,out var CosE)
            ||!LogicalScalar.TryMultiply(this.Orbit.Eccentricity,this.Orbit.Eccentricity,out var EccentricitySquared)
            ||!LogicalScalar.TrySubtract(LogicalScalar.One,EccentricitySquared,out var OneMinusEccentricitySquared)
            ||!LogicalScalar.TrySquareRoot(OneMinusEccentricitySquared,out var MinorAxisFactor)
            ||!LogicalScalar.TrySubtract(CosE,this.Orbit.Eccentricity,out var RadialR)
            ||!LogicalScalar.TryMultiply(this.Orbit.SemiMajorAxis,RadialR,out RadialR)
            ||!LogicalScalar.TryMultiply(MinorAxisFactor,SinE,out var RadialF)
            ||!LogicalScalar.TryMultiply(this.Orbit.SemiMajorAxis,RadialF,out RadialF)
            ||!TryCreateOrbitalRotation(this.Orbit,out var OrbitalRotation)
            ||!OrbitalRotation.TryRotate(new LogicalVector3(RadialR,LogicalScalar.Zero,RadialF),out var Position)
            ||!LogicalScalar.TryMultiply(this.Orbit.Eccentricity,CosE,out var EccentricCosine)
            ||!LogicalScalar.TrySubtract(LogicalScalar.One,EccentricCosine,out var VelocityDenominator)
            ||!LogicalScalar.TryDivide(EffectiveMotion,VelocityDenominator,out var EccentricAnomalyRate)
            ||!LogicalScalar.TryMultiply(this.Orbit.SemiMajorAxis,SinE,out var VelocityR)
            ||!LogicalScalar.TrySubtract(LogicalScalar.Zero,VelocityR,out VelocityR)
            ||!LogicalScalar.TryMultiply(VelocityR,EccentricAnomalyRate,out VelocityR)
            ||!LogicalScalar.TryMultiply(this.Orbit.SemiMajorAxis,MinorAxisFactor,out var ScaledMinorAxis)
            ||!LogicalScalar.TryMultiply(ScaledMinorAxis,CosE,out var VelocityF)
            ||!LogicalScalar.TryMultiply(VelocityF,EccentricAnomalyRate,out VelocityF)
            ||!OrbitalRotation.TryRotate(new LogicalVector3(VelocityR,LogicalScalar.Zero,VelocityF),out var Velocity)){
            return Fail(KeplerPropagationErrorCode.ArithmeticOverflow,"Kepler state is not representable in Q64.64.",out Error);
        }
        State=new KeplerFrameState(new FramePose(Position,OrbitalRotation,LogicalVector3.One),Velocity,MeanAnomaly,EccentricAnomaly);
        return true;
    }
    public bool TryEnqueue(ReferenceFrameGraph Graph,ReferenceFrameTime Time,out KeplerFrameState State,out KeplerPropagationError Error){
        ArgumentNullException.ThrowIfNull(Graph);
        State=default;
        if(!Graph.Current.Contains(this.Frame)){ return Fail(KeplerPropagationErrorCode.MissingFrame,"The target frame is not present in the current snapshot.",out Error); }
        if(!this.TryEvaluate(Time,out State,out Error)){ return false; }
        Graph.SetPose(this.Frame,State.Pose);
        return true;
    }
    private static bool TrySolveEccentricAnomaly(LogicalScalar MeanAnomaly,LogicalScalar Eccentricity,out LogicalScalar EccentricAnomaly,out bool Converged){
        EccentricAnomaly=Eccentricity<HighEccentricityThreshold?MeanAnomaly:LogicalMath.Pi;
        Converged=false;
        for(var Iteration=0;Iteration<MaximumSolverIterations;Iteration++){
            if(!LogicalMath.TrySinCos(EccentricAnomaly,out var Sin,out var Cos)
                ||!LogicalScalar.TryMultiply(Eccentricity,Sin,out var EccentricSin)
                ||!LogicalScalar.TrySubtract(EccentricAnomaly,EccentricSin,out var Function)
                ||!LogicalScalar.TrySubtract(Function,MeanAnomaly,out Function)
                ||!LogicalScalar.TryMultiply(Eccentricity,Cos,out var EccentricCosine)
                ||!LogicalScalar.TrySubtract(LogicalScalar.One,EccentricCosine,out var Derivative)
                ||!LogicalScalar.TryDivide(Function,Derivative,out var Delta)
                ||!LogicalScalar.TrySubtract(EccentricAnomaly,Delta,out EccentricAnomaly)){ return false; }
            if(Delta.Raw>=-SolverToleranceRaw&&Delta.Raw<=SolverToleranceRaw){ Converged=true;return true; }
        }
        return true;
    }
    private static bool TryCreateOrbitalRotation(KeplerOrbit Orbit,out LogicalQuaternion Rotation){
        Rotation=default;
        return LogicalQuaternion.TryCreateAxisAngle(LogicalVector3.UnitU,Orbit.LongitudeOfAscendingNode,out var AscendingNode)
            &&LogicalQuaternion.TryCreateAxisAngle(LogicalVector3.UnitR,Orbit.Inclination,out var Inclination)
            &&LogicalQuaternion.TryCreateAxisAngle(LogicalVector3.UnitU,Orbit.ArgumentOfPeriapsis,out var Periapsis)
            &&LogicalQuaternion.TryMultiply(AscendingNode,Inclination,out var Plane)
            &&LogicalQuaternion.TryMultiply(Plane,Periapsis,out var Candidate)
            &&Candidate.TryNormalize(out Rotation);
    }
    private static bool Fail(KeplerPropagationErrorCode Code,string Message,out KeplerPropagationError Error){ Error=new KeplerPropagationError(Code,Message);return false; }
}
