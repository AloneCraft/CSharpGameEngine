using System;
using System.Globalization;
namespace CSharpGameEngine.SceneAnalysis.Json;
/// <summary>角度メンバーの C# 側の単位（値は <c>CSharpGameEngine.Contracts.AngleUnit</c>）。</summary>
internal enum SceneAngleUnit:byte {
    Radians,
    Degrees,
}
/// <summary>
/// ADR-0074：<see cref="TurnsSchemaVersion"/> 以降の Scene JSON では角度を回転数（1 = 1回転）で保存する。エディターでは度で表示し、C# メンバー
/// はそれぞれの単位を保つ。テキスト値は double 演算でメンバーの float に変換する。回転数を float で扱うと、すべてのラジアン値や度数値を表せるとは限らない（500度に対応する回転数の
///  float が存在しない）。値を同じ float に戻せる最小桁数で書き込むため、変更されていない角度のテキストを編集時も維持し、バージョン1のドキュメントを値を変えずに更新できる。
/// </summary>
internal static class SceneJsonAngles {
    /// <summary>角度を回転数で表す最初のスキーマバージョン。バージョン1ではメンバーごとの単位を使う。</summary>
    internal const int TurnsSchemaVersion=2;
    /// <summary>角度のキーから C# の単位名を省略する最初のスキーマバージョン（<see cref="KeyFor"/>）。</summary>
    internal const int UnitFreeKeysSchemaVersion=3;
    private static readonly string[]UnitWords=["Radians","Degrees"];
    /// <summary>角度メンバーを宣言する属性の完全修飾名。</summary>
    internal const string AttributeFullName="CSharpGameEngine.Contracts.AngleAttribute";
    private enum Factor:byte {
        One,
        TurnsToRadians,
        TurnsToDegrees,
        DegreesToRadians,
    }
    /// <summary>回転数で書き込まれた数値に対するメンバー値。</summary>
    internal static float FromTurns(string Text,SceneAngleUnit Unit)=>Multiply(Text,Unit==SceneAngleUnit.Radians?Factor.TurnsToRadians:Factor.TurnsToDegrees);
    /// <summary>度数で書き込まれた数値に対するメンバー値（エディターで使用する単位）。</summary>
    internal static float FromDegrees(string Text,SceneAngleUnit Unit)=>Multiply(Text,Unit==SceneAngleUnit.Radians?Factor.DegreesToRadians:Factor.One);
    internal static string FormatTurns(float Value,SceneAngleUnit Unit)=>Shortest(Value,Unit==SceneAngleUnit.Radians?Factor.TurnsToRadians:Factor.TurnsToDegrees);
    internal static string FormatDegrees(float Value,SceneAngleUnit Unit)=>Shortest(Value,Unit==SceneAngleUnit.Radians?Factor.DegreesToRadians:Factor.One);
    /// <summary>
    /// <paramref name="Name"/> という名前の角度メンバーまたはコンストラクターパラメーターに対する JSON キー。ドキュメントでは回転数を保持するため、末尾の単位名を除いた名前にする
    /// （<c>FovYRadians</c> は <c>FovY</c> になる。2026-10-01のユーザー判断で C# 側の名前は維持）。単位名を除くと空になる名前や、型内の別メンバー／パラメーターと
    /// キーが重複する名前は、元の名前を維持する。
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
    /// <summary>テキストが、このクラスで変換可能な有限の JSON 数値かどうか。</summary>
    internal static bool IsNumber(string?Text)=>
        Text is { Length:>0 }&&float.TryParse(Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var Value)&&!float.IsInfinity(Value)&&!float.IsNaN(Value);
    /// <summary>属性のコンストラクター引数が示す単位（列挙値の基底値）。</summary>
    internal static SceneAngleUnit?UnitFromArgument(object?Argument)=>
        Argument is null?null:System.Convert.ToInt32(Argument,CultureInfo.InvariantCulture)==1?SceneAngleUnit.Degrees:SceneAngleUnit.Radians;
    // 数値ポリシー（NUM001）で double の境界となるのはこの2つのメソッド。ポリシー適用位置に属性を置く。
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
    /// <summary><see cref="Multiply"/> で <paramref name="Value"/> に戻せる最短の十進表記。</summary>
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
        // "G1" 形式では90が "9E+01" になる。通常の大きさの値は桁位置を保った表記になる。数値と解析結果は正確に
        // 扱う（SceneJsonDecimal）。VSIX とツールで同じテキストを書き込める。値の積は Multiply で求める。
        var Positional=Math.Abs(Quotient)>=1e-4&&Math.Abs(Quotient)<1e15;
        var Text=SceneJsonDecimal.Shortest(Quotient,1,17,Positional,Parsed=>(float)(Parsed*Scale)==Value);
        if(Text is not null){ return Text=="-0"?"0":Text; }
        // Quotient の値へ戻せるテキストはない。Quotient 自身の最短表記は、"R" が出力する形式になる。
        return SceneJsonDecimal.Shortest(Quotient,1,17,false,Parsed=>Parsed==Quotient,NotationDigits:15)!;
    }
}
