using System.Globalization;
using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.VSExtension.Tests;
public sealed class AssetFontCoverageTests {
    /// <summary>FontBaker の既定の字形（ASCII、Latin-1、句読点と €）。同梱の Roboto の .cgefont と同じ 200 字。</summary>
    private static AssetFontGlyph[]DefaultBakedGlyphs(){
        var CodePoints=new List<int>();
        CodePoints.AddRange(Enumerable.Range(0x20,0x7F-0x20));
        CodePoints.AddRange(Enumerable.Range(0xA0,0x100-0xA0));
        CodePoints.AddRange(new[]{ 0x2013,0x2014,0x2018,0x2019,0x201C,0x201D,0x2022,0x2026,0x20AC });
        return [..CodePoints.Select(static CodePoint=>new AssetFontGlyph { CodePoint=CodePoint,Advance=0.5f,PlaneRight=CodePoint==0x20||CodePoint==0xA0?0f:0.4f,PlaneTop=CodePoint==0x20||CodePoint==0xA0?0f:0.7f })];
    }
    private static FontLanguageResult Language(FontCoverageReport Report,string Id)=>Report.Languages.Single(Result=>Result.Id==Id);
    [Fact]
    public void TheBundledLatinFontSupportsEnglishAndMostWesternLanguagesButNotCjkOrCyrillic(){
        var Report=AssetFontCoverage.Analyze(AssetFontCoverage.FromGlyphs(DefaultBakedGlyphs()),new CultureInfo("en-US"));
        foreach(var Id in new[]{ "en","de","es","pt" }){ Assert.Equal(FontLanguageSupport.Full,Language(Report,Id).Support); }
        // フランス語の œ Œ Ÿ、ポーランド語・トルコ語・ベトナム語の固有の文字は Latin-1 にない。
        foreach(var Id in new[]{ "fr","pl","tr","vi" }){
            var Result=Language(Report,Id);
            Assert.Equal(FontLanguageSupport.Partial,Result.Support);
            Assert.InRange(Result.Supported,1,Result.Total-1);
        }
        foreach(var Id in new[]{ "ru","el","ja","zh","ko","ar","th" }){ Assert.Equal(FontLanguageSupport.None,Language(Report,Id).Support); }
    }
    [Fact]
    public void SymbolsTheFontLacksAreListedAndTheOnesItHasAreNot(){
        var Report=AssetFontCoverage.Analyze(AssetFontCoverage.FromGlyphs(DefaultBakedGlyphs()),new CultureInfo("en-US"));
        Assert.Contains("←",Report.MissingSymbols);
        Assert.Contains("★",Report.MissingSymbols);
        Assert.Contains("™",Report.MissingSymbols);
        Assert.Contains("〜",Report.MissingSymbols);
        foreach(var Present in new[]{ "©","®","€","¥","£","°","±","×","÷","…","•" }){ Assert.DoesNotContain(Present,Report.MissingSymbols); }
        Assert.True(Report.SymbolTotal>=Report.MissingSymbols.Count&&Report.MissingSymbols.Count>0);
        Assert.Equal(Report.MissingSymbols.Count,Report.SymbolSample.MissingIndexes.Count);
    }
    [Fact]
    public void TheSampleFollowsTheUiLanguageAndMarksOnlyWhatCannotBeDrawn(){
        var Has=AssetFontCoverage.FromGlyphs(DefaultBakedGlyphs());
        var English=AssetFontCoverage.Analyze(Has,new CultureInfo("en-US")).Sample;
        Assert.Contains("quick brown fox",English.Text);
        Assert.Empty(English.MissingIndexes);
        var Japanese=AssetFontCoverage.Analyze(Has,new CultureInfo("ja-JP")).Sample;
        Assert.Contains("日本語",Japanese.Text);
        // ひらがな・カタカナ・漢字・「」。は描けず、英数字と空白は描ける。
        Assert.NotEmpty(Japanese.MissingIndexes);
        var Elements=AssetFontCoverage.CodePoints(Japanese.Text).ToArray();
        Assert.All(Japanese.MissingIndexes,Index=>Assert.False(Has(Elements[Index])));
        Assert.DoesNotContain(Japanese.MissingIndexes,Index=>Elements[Index]==' ');
        Assert.DoesNotContain(Japanese.MissingIndexes,Index=>Elements[Index] is >= 'A' and <= 'Z');
    }
    [Fact]
    public void AGlyphWithoutAnOutlineCountsAsDrawableOnlyForWhitespace(){
        var Glyphs=new[]{
            new AssetFontGlyph { CodePoint=' ',Advance=0.3f },
            new AssetFontGlyph { CodePoint='A',Advance=0.6f },
            new AssetFontGlyph { CodePoint='B',Advance=0.6f,PlaneRight=0.5f,PlaneTop=0.7f },
        };
        var Has=AssetFontCoverage.FromGlyphs(Glyphs);
        Assert.True(Has(' '));
        Assert.False(Has('A'));
        Assert.True(Has('B'));
        Assert.False(Has('C'));
    }
    [Fact]
    public void ACharacterMapFromATrueTypeFontGivesTheSameKindOfAnswer(){
        var Has=AssetFontCoverage.FromCharacterMap(Enumerable.Range(0x20,0x5F).Concat(new[]{ 0x3042 }));
        var Report=AssetFontCoverage.Analyze(Has,new CultureInfo("ja-JP"));
        Assert.Equal(FontLanguageSupport.Full,Language(Report,"en").Support);
        Assert.Equal(FontLanguageSupport.Partial,Language(Report,"ja").Support);
        Assert.Equal(1,Language(Report,"ja").Supported);
    }
    [Fact]
    public void SurrogatePairsAreOneCharacterAndAnEmptyFontIsHandled(){
        Assert.Equal(new[]{ 'a',0x1F600,'b' },AssetFontCoverage.CodePoints("a\U0001F600b"));
        var Nothing=AssetFontCoverage.Analyze(static _=>false,new CultureInfo("en-US"));
        Assert.All(Nothing.Languages,static Result=>Assert.Equal(FontLanguageSupport.None,Result.Support));
        Assert.Equal(Nothing.SymbolTotal,Nothing.MissingSymbols.Count);
        Assert.Equal(AssetFontCoverage.CodePoints(Nothing.Sample.Text).Count(static CodePoint=>!AssetFontCoverage.IsBlank(CodePoint)),Nothing.Sample.MissingIndexes.Count);
    }
    [Fact]
    public void EveryLanguageHasAtLeastOneCharacterAndAName(){
        foreach(var (Id,Characters) in AssetFontCoverage.Languages){
            Assert.NotEmpty(Characters);
            Assert.NotEqual("FontLang_"+Id,VsixText.Get("FontLang_"+Id));
        }
    }
}
