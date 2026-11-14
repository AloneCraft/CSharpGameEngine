#if !CGE_NUMERIC_FLOAT && !CGE_NUMERIC_DOUBLE
#if CGE_FIXED_EXTENDED_MATH
using MathValue=CSharpGameEngine.Numerics.WideAngleScalar;
using MathCore=CSharpGameEngine.Numerics.FixedAngleMath<CSharpGameEngine.Numerics.WideAngleScalar>;
#else
using MathValue=CSharpGameEngine.Numerics.LogicalScalar;
using MathCore=CSharpGameEngine.Numerics.FixedAngleMath<CSharpGameEngine.Numerics.LogicalScalar>;
#endif
namespace CSharpGameEngine.Numerics;
public static class LogicalMath {
    public static LogicalScalar Pi=>Export(MathCore.Pi);
    public static LogicalScalar HalfPi=>Export(MathCore.HalfPi);
    public static LogicalScalar TwoPi=>Export(MathCore.TwoPi);
    public static LogicalScalar NormalizeRadians(LogicalScalar Angle)=>Export(MathCore.NormalizeRadians(Import(Angle)));
    public static bool TrySinCos(LogicalScalar Angle,out LogicalScalar Sin,out LogicalScalar Cos){
        Sin=default;Cos=default;
        if(!MathCore.TrySinCos(Import(Angle),out var Sine,out var Cosine)||!TryExport(Sine,out var SavedSin)||!TryExport(Cosine,out var SavedCos)){ return false; }
        Sin=SavedSin;Cos=SavedCos;return true;
    }
    public static bool TryAtan2(LogicalScalar Y,LogicalScalar X,out LogicalScalar Angle){
        Angle=default;
        return MathCore.TryAtan2(Import(Y),Import(X),out var Value)&&TryExport(Value,out Angle);
    }
    public static bool TryAdvanceRadians(LogicalScalar Phase,LogicalScalar RadiansPerTick,ulong TickCount,out LogicalScalar Result){
        Result=default;
        return MathCore.TryAdvanceRadians(Import(Phase),Import(RadiansPerTick),TickCount,out var Value)&&TryExport(Value,out Result);
    }
#if CGE_FIXED_EXTENDED_MATH
    private static MathValue Import(LogicalScalar Value)=>MathValue.FromLogical(Value);
    private static bool TryExport(MathValue Value,out LogicalScalar Result)=>Value.TryToLogical(out Result);
#else
    private static MathValue Import(LogicalScalar Value)=>Value;
    private static bool TryExport(MathValue Value,out LogicalScalar Result){ Result=Value;return true; }
#endif
    private static LogicalScalar Export(MathValue Value)=>TryExport(Value,out var Result)?Result:throw new OverflowException("The mathematical constant or result does not fit the selected numeric format.");
}
#endif
