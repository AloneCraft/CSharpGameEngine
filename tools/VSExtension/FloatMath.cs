using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.VSExtension;
internal static class FloatMath {
    internal const float Pi=3.14159265358979323846f;
    [ExternalNumericAdapter]
    internal static float Abs(float Value)=>FromExternal(Math.Abs(Value));
    [ExternalNumericAdapter]
    internal static float Acos(float Value)=>FromExternal(Math.Acos(Value));
    [ExternalNumericAdapter]
    internal static float Asin(float Value)=>FromExternal(Math.Asin(Value));
    [ExternalNumericAdapter]
    internal static float Atan(float Value)=>FromExternal(Math.Atan(Value));
    [ExternalNumericAdapter]
    internal static float Atan2(float Y,float X)=>FromExternal(Math.Atan2(Y,X));
    [ExternalNumericAdapter]
    internal static float Cos(float Value)=>FromExternal(Math.Cos(Value));
    [ExternalNumericAdapter]
    internal static float Pow(float Value,float Power)=>FromExternal(Math.Pow(Value,Power));
    [ExternalNumericAdapter]
    internal static float Round(float Value)=>FromExternal(Math.Round(Value));
    [ExternalNumericAdapter]
    internal static float Round(float Value,MidpointRounding Mode)=>FromExternal(Math.Round(Value,Mode));
    [ExternalNumericAdapter]
    internal static float Sin(float Value)=>FromExternal(Math.Sin(Value));
    [ExternalNumericAdapter]
    internal static float Sqrt(float Value)=>FromExternal(Math.Sqrt(Value));
    [ExternalNumericAdapter]
    internal static float Tan(float Value)=>FromExternal(Math.Tan(Value));
    [ExternalNumericAdapter]
    internal static float FromExternal(double Value){
        if(!TryFromExternal(Value,out var Result)){ throw new ArithmeticException("The framework numeric result cannot be represented as float."); }
        return Result;
    }
    [ExternalNumericAdapter]
    internal static bool TryFromExternal(double Value,out float Result){
        Result=default;
        if(double.IsNaN(Value)||double.IsInfinity(Value)||Value<float.MinValue||Value>float.MaxValue){ return false; }
        Result=(float)Value;
        return true;
    }
}
