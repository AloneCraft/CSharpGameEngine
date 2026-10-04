namespace CSharpGameEngine.Contracts;
/// <summary>The unit an angle member holds in C#.</summary>
public enum AngleUnit:byte {
    Radians,
    Degrees,
}
/// <summary>
/// ADR-0074: marks a component member or constructor parameter as an angle, and every number inside its value
/// (for example the R, U and F of an Euler rotation). Scene JSON stores it in turns (1 = one full rotation) and
/// the editor shows degrees; the C# value keeps <see cref="Unit"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Field|AttributeTargets.Property|AttributeTargets.Parameter,Inherited=false)]
public sealed class AngleAttribute:Attribute {
    public AngleAttribute(AngleUnit Unit){
        this.Unit=Unit;
    }
    public AngleUnit Unit { get; }
}
