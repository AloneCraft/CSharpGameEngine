#if CGE_NUMERIC_FLOAT || CGE_NUMERIC_DOUBLE
#if CGE_NUMERIC_DOUBLE
using NativeMath=System.Math;
#else
using NativeMath=System.MathF;
#endif
namespace CSharpGameEngine.Numerics;
public static class LogicalMath {
    public static LogicalScalar Pi=>NativeMath.PI;
    public static LogicalScalar HalfPi=>NativeMath.PI/2;
    public static LogicalScalar TwoPi=>NativeMath.PI*2;
    public static LogicalScalar NormalizeRadians(LogicalScalar Angle){
        if(!LogicalScalar.IsFinite(Angle)){ throw new OverflowException(); }
        var Value=Angle%TwoPi;
        if(Value>Pi){ Value-=TwoPi; }else if(Value<-Pi){ Value+=TwoPi; }
        return Value;
    }
    public static bool TrySinCos(LogicalScalar Angle,out LogicalScalar Sin,out LogicalScalar Cos){
        Sin=default;Cos=default;
        if(!LogicalScalar.IsFinite(Angle)){ return false; }
        var Values=NativeMath.SinCos(Angle);Sin=Values.Sin;Cos=Values.Cos;return true;
    }
    public static bool TryAtan2(LogicalScalar Y,LogicalScalar X,out LogicalScalar Angle){
        Angle=default;
        if(!LogicalScalar.IsFinite(X)||!LogicalScalar.IsFinite(Y)){ return false; }
        if(X==0&&Y==0){ return true; }
        Angle=NativeMath.Atan2(Y,X);return true;
    }
    public static bool TryAdvanceRadians(LogicalScalar Phase,LogicalScalar RadiansPerTick,ulong TickCount,out LogicalScalar Result){
        Result=default;
        if(!LogicalScalar.IsFinite(Phase)||!LogicalScalar.IsFinite(RadiansPerTick)){ return false; }
        var Position=Positive(Phase);
        var Step=Positive(RadiansPerTick);
        LogicalScalar Increment=0;
        while(TickCount!=0){
            if((TickCount&1)!=0){ Increment=AddModulo(Increment,Step); }
            TickCount>>=1;
            if(TickCount!=0){ Step=AddModulo(Step,Step); }
        }
        Result=AddModulo(Position,Increment);return true;
    }
    private static LogicalScalar Positive(LogicalScalar Value){ var Remainder=Value%TwoPi;if(Remainder<0){ Remainder+=TwoPi; }return Remainder>=TwoPi?0:Remainder; }
    private static LogicalScalar AddModulo(LogicalScalar Left,LogicalScalar Right){ var Value=Left>=TwoPi-Right?Left-(TwoPi-Right):Left+Right;return Value>=TwoPi?Value-TwoPi:Value; }
}
#endif
