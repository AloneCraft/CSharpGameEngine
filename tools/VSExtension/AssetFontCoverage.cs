using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.VSExtension;
internal enum FontLanguageSupport { None, Partial, Full }
/// <summary>言語ごとの代表文字がどれだけ描けるか。</summary>
internal sealed record FontLanguageResult(string Id,int Supported,int Total){
    internal FontLanguageSupport Support=>this.Supported==0?FontLanguageSupport.None:this.Supported==this.Total?FontLanguageSupport.Full:FontLanguageSupport.Partial;
}
/// <summary>見本の文字列のうち描けない文字（文字単位の位置は <see cref="MissingIndexes"/>、空白は数えない）。</summary>
internal sealed record FontSample(string Text,IReadOnlyList<int>MissingIndexes);
/// <summary>フォントの対応状況の要約（言語・記号・既定の言語の例文）。</summary>
internal sealed record FontCoverageReport(
    IReadOnlyList<FontLanguageResult>Languages,
    IReadOnlyList<string>MissingSymbols,
    int SymbolTotal,
    FontSample Sample,
    FontSample SymbolSample);
/// <summary>
/// フォントが描ける文字の判定。<c>.cgefont</c> は字形の一覧、<c>.ttf/.otf</c> は <c>GlyphTypeface.CharacterToGlyphMap</c> を判定関数にする。
/// 実行時は描けない文字を <c>?</c> で描く（<c>?</c> も無ければ何も描かない）ので、ここでの「描けない」はその意味。
/// </summary>
internal static class AssetFontCoverage {
    /// <summary>言語の ID と代表文字。その言語の基本の字形（ラテン文字は基本の A-Z a-z に加えて固有の文字）を短く並べた固定の表。</summary>
    internal static readonly IReadOnlyList<(string Id,string Characters)>Languages=[
        ("en","ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789"),
        ("fr","àâçéèêëîïôùûüÿœÀÂÇÉÈÊËÎÏÔÙÛÜŸŒ«»"),
        ("de","äöüßÄÖÜ"),
        ("es","áéíóúñüÁÉÍÓÚÑ¿¡"),
        ("pt","ãõçáéíóúâêôÃÕÇ"),
        ("pl","ąćęłńóśźżĄĆĘŁŃÓŚŹŻ"),
        ("tr","çğıöşüÇĞİÖŞÜ"),
        ("vi","ăâđêôơưĂÂĐÊÔƠƯạảấầẩẫậắằẳẵặẹẻẽếềểễệ"),
        ("ru","абвгдеёжзийклмнопрстуфхцчшщъыьэюяАБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯ"),
        ("el","αβγδεζηθικλμνξοπρστυφχψωΑΒΓΔΕΖΗΘΙΚΛΜΝΞΟΠΡΣΤΥΦΧΨΩάέήίόύώ"),
        ("ja",VsixText.Get("FontChars_ja")),
        ("zh",VsixText.Get("FontChars_zh")),
        ("ko","가나다라마바사아자차카타파하한국어글자안녕하세요감사합니다"),
        ("ar","ابتثجحخدذرزسشصضطظعغفقكلمنهوي"),
        ("th","กขคงจฉชซญดตถทนบปผฝพฟภมยรลวศษสหอฮะาิีึืุูเแโใไ"),
    ];
    /// <summary>よく使う記号・特殊文字。無いと実行時に <c>?</c> になる。</summary>
    internal const string Symbols="←→↑↓↔★☆♪♥♦♣♠©®™€¥£¢°±×÷≠≤≥≈∞√∑πΩµ…•※〜～「」『』【】○●◎□■△▲▽▼◇◆✓✗✔✘☑☐";
    /// <summary>UI の言語に合わせた例文（リソース <c>Font_SampleText</c>。ja-JP は日本語、それ以外は英語の pangram）。</summary>
    internal static string SampleFor(CultureInfo Culture)=>VsixText.Get("Font_SampleText",Culture);
    internal static Func<int,bool>FromGlyphs(IEnumerable<AssetFontGlyph>Glyphs){
        // 矩形のない字形は空白だけを「描ける」とみなす（空でない文字が矩形なしで入っていても何も見えない）。
        var Drawable=new HashSet<int>(Glyphs.Where(static Glyph=>Glyph.HasQuad||IsBlank(Glyph.CodePoint)).Select(static Glyph=>Glyph.CodePoint));
        return Drawable.Contains;
    }
    internal static Func<int,bool>FromCharacterMap(IEnumerable<int>CodePoints){
        var Set=new HashSet<int>(CodePoints);
        return Set.Contains;
    }
    internal static bool IsBlank(int CodePoint)=>CodePoint is 0x20 or 0xA0 or 0x3000||(CodePoint<0x10000&&char.IsWhiteSpace((char)CodePoint));
    internal static IEnumerable<int>CodePoints(string Text){
        for(var Index=0;Index<Text.Length;Index++){
            if(char.IsHighSurrogate(Text[Index])&&Index+1<Text.Length&&char.IsLowSurrogate(Text[Index+1])){ yield return char.ConvertToUtf32(Text[Index],Text[Index+1]);Index++; }
            else { yield return Text[Index]; }
        }
    }
    internal static FontSample Sample(string Text,Func<int,bool>Has){
        var Missing=new List<int>();
        var Position=0;
        foreach(var CodePoint in CodePoints(Text)){
            if(!IsBlank(CodePoint)&&!Has(CodePoint)){ Missing.Add(Position); }
            Position++;
        }
        return new FontSample(Text,Missing);
    }
    internal static FontCoverageReport Analyze(Func<int,bool>Has,CultureInfo Culture){
        var Results=new List<FontLanguageResult>();
        foreach(var (Id,Characters) in Languages){
            var Distinct=CodePoints(Characters).Distinct().ToArray();
            Results.Add(new FontLanguageResult(Id,Distinct.Count(Has),Distinct.Length));
        }
        var SymbolPoints=CodePoints(Symbols).Distinct().ToArray();
        var MissingSymbols=SymbolPoints.Where(CodePoint=>!Has(CodePoint)).Select(static CodePoint=>char.ConvertFromUtf32(CodePoint)).ToArray();
        return new FontCoverageReport(Results,MissingSymbols,SymbolPoints.Length,Sample(SampleFor(Culture),Has),Sample(Symbols,Has));
    }
}
