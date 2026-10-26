namespace CSharpGameEngine.Contracts;
/// <summary>C#の角度メンバーが保持する単位。</summary>
public enum AngleUnit:byte {
    Radians,
    Degrees,
}
/// <summary>
/// ADR-0074：コンポーネントのメンバーまたはコンストラクターパラメーターを角度として指定し、その値に含まれるすべての数値にも適用する
///（例：オイラー回転のR、U、F）。シーンJSONでは回転数で保存し（1 = 1回転）、エディターでは度数で表示する。C#の値は<see cref="Unit"/>を保持する。
/// </summary>
[AttributeUsage(AttributeTargets.Field|AttributeTargets.Property|AttributeTargets.Parameter,Inherited=false)]
public sealed class AngleAttribute:Attribute {
    public AngleAttribute(AngleUnit Unit){
        this.Unit=Unit;
    }
    public AngleUnit Unit { get; }
}
