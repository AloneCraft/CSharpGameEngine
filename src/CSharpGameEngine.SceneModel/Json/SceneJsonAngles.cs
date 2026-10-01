using System;
using System.Globalization;
namespace CSharpGameEngine.SceneAnalysis.Json;
/// <summary>The C# unit of an angle member (<c>CSharpGameEngine.Contracts.AngleUnit</c>, by value).</summary>
internal enum SceneAngleUnit:byte {
    Radians,
    Degrees,
}
/// <summary>
/// ADR-0074: scene JSON stores angles in turns (1 = one full rotation) from <see cref="TurnsSchemaVersion"/> on, the
/// editor shows degrees, and the C# member keeps its own unit. A text converts to the member's float through double
/// arithmetic: a float in turns is too coarse to reach every float in radians or degrees (500 degrees has no float
/// preimage in turns). A value is written with the fewest digits that convert back to the same float, so an
/// unchanged angle keeps its text through an edit and a version 1 document upgrades without changing a value.
/// </summary>
internal static class SceneJsonAngles {
    /// <summary>The first schema version whose angles are turns; version 1 holds each member's own unit.</summary>
    internal const int TurnsSchemaVersion=2;
    /// <summary>The first schema version whose angle keys leave out the C# unit word (<see cref="KeyFor"/>).</summary>
    internal const int UnitFreeKeysSchemaVersion=3;
    private static readonly string[]UnitWords=["Radians","Degrees"];
    /// <summary>The full name of the attribute that declares an angle member.</summary>
    internal const string AttributeFullName="CSharpGameEngine.Contracts.AngleAttribute";
    private enum Factor:byte {
        One,
        TurnsToRadians,
        TurnsToDegrees,
        DegreesToRadians,
    }
    /// <summary>The member's value for a number written in turns.</summary>
    internal static float FromTurns(string Text,SceneAngleUnit Unit)=>Multiply(Text,Unit==SceneAngleUnit.Radians?Factor.TurnsToRadians:Factor.TurnsToDegrees);
    /// <summary>The member's value for a number written in degrees (the editor's unit).</summary>
    internal static float FromDegrees(string Text,SceneAngleUnit Unit)=>Multiply(Text,Unit==SceneAngleUnit.Radians?Factor.DegreesToRadians:Factor.One);
    internal static string FormatTurns(float Value,SceneAngleUnit Unit)=>Shortest(Value,Unit==SceneAngleUnit.Radians?Factor.TurnsToRadians:Factor.TurnsToDegrees);
    internal static string FormatDegrees(float Value,SceneAngleUnit Unit)=>Shortest(Value,Unit==SceneAngleUnit.Radians?Factor.DegreesToRadians:Factor.One);
    /// <summary>
    /// The JSON key of an angle member or constructor parameter named <paramref name="Name"/>: the name without its
    /// trailing unit word, because the document holds turns (<c>FovYRadians</c> is <c>FovY</c>; user choice,
    /// 2026-10-01: the C# names stay). A name that would become empty, or whose key the type already uses for another
    /// member or parameter, keeps its name.
    /// </summary>
    internal static string KeyFor(string Name,Func<string,bool>IsTaken){
        foreach(var Word in UnitWords){
            if(Name.Length>Word.Length&&Name.EndsWith(Word,StringComparison.Ordinal)){
                var Key=Name.Substring(0,Name.Length-Word.Length);
                return IsTaken(Key)?Name:Key;
            }
        }
        return Name;
    }
    /// <summary>Whether a text is a finite JSON number this class can convert.</summary>
    internal static bool IsNumber(string?Text)=>
        Text is { Length:>0 }&&float.TryParse(Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var Value)&&!float.IsInfinity(Value)&&!float.IsNaN(Value);
    /// <summary>The unit an attribute's constructor argument names (the enum's underlying value).</summary>
    internal static SceneAngleUnit?UnitFromArgument(object?Argument)=>
        Argument is null?null:System.Convert.ToInt32(Argument,CultureInfo.InvariantCulture)==1?SceneAngleUnit.Degrees:SceneAngleUnit.Radians;
    // The double boundary of the numeric policy (NUM001) is these two methods; the attribute exists where the policy runs.
#if CGE_NUMERIC_ADAPTER
    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
#endif
    private static float Multiply(string Text,Factor Kind){
        var Scale=Kind switch {
            Factor.TurnsToRadians=>2d*Math.PI,
            Factor.TurnsToDegrees=>360d,
            Factor.DegreesToRadians=>Math.PI/180d,
            _=>1d,
        };
        return (float)(double.Parse(Text,NumberStyles.Float,CultureInfo.InvariantCulture)*Scale);
    }
    /// <summary>The shortest decimal text that <see cref="Multiply"/> turns back into <paramref name="Value"/>.</summary>
#if CGE_NUMERIC_ADAPTER
    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
#endif
    private static string Shortest(float Value,Factor Kind){
        if(float.IsNaN(Value)||float.IsInfinity(Value)){ throw new ArgumentOutOfRangeException(nameof(Value),"JSON numbers must be finite."); }
        var Scale=Kind switch {
            Factor.TurnsToRadians=>2d*Math.PI,
            Factor.TurnsToDegrees=>360d,
            Factor.DegreesToRadians=>Math.PI/180d,
            _=>1d,
        };
        var Quotient=Value/Scale;
        // "G1" writes 90 as "9E+01"; an ordinary magnitude keeps positional digits.
        var Positional=Math.Abs(Quotient)>=1e-4&&Math.Abs(Quotient)<1e15;
        for(var Digits=1;Digits<=17;Digits++){
            var Text=Quotient.ToString("G"+Digits.ToString(CultureInfo.InvariantCulture),CultureInfo.InvariantCulture);
            if(Positional&&Text.IndexOf('E')>=0){ continue; }
            if(Multiply(Text,Kind)==Value){ return Text=="-0"?"0":Text; }
        }
        return Quotient.ToString("R",CultureInfo.InvariantCulture);
    }
}
