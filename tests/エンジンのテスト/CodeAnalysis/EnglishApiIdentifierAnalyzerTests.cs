using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;
namespace CSharpGameEngine.CodeAnalysis.Tests;
/// <summary>
/// NAME001（ADR-0099）：利用者が読む名前は英語、エンジンの内部とテストは日本語でよい。
/// 試験の中の名前は、この決まりのとおり日本語で書いている。
/// </summary>
public sealed class EnglishApiIdentifierAnalyzerTests {
    private const string 普通のファイル=@"C:\Engine\src\Engine\Core\Sample.cs";
    private const string ゲームへ入るファイル=@"C:\Engine\src\Runtime\Physics\Sample.cs";
    private const string プラグインの実行時のファイル=@"C:\Engine\plugins\Destruction\Runtime\Sample.cs";
    [Theory]
    [InlineData("public sealed class 速度 {}")]
    [InlineData("public sealed class Body { public int 質量; }")]
    [InlineData("public sealed class Body { public void Push(int 力){} }")]
    [InlineData("public sealed class Body { protected int 内部の値{ get; set; } }")]
    [InlineData("public enum Mode { 歩く, Run }")]
    [InlineData("public sealed class Box<型> {}")]
    [InlineData("public sealed class Body { public int this[int 番号]=>番号; }")]
    public void 公開された名前に日本語があると誤りになる(string ソース){
        var 診断=調べる(ソース,普通のファイル);
        Assert.Contains(診断,static 一件=>一件.Id=="NAME001");
    }
    [Theory]
    [InlineData("internal sealed class 内部の計算 { public int 値; }")]
    [InlineData("public sealed class Body { private int 内部の値; internal void 計算する(int 引数){} }")]
    [InlineData("public sealed class Body { public int Mass{ get; set; } public void Push(int Force){} }")]
    public void 内部の名前と英語の公開された名前は誤りにならない(string ソース){
        Assert.Empty(調べる(ソース,普通のファイル));
    }
    [Theory]
    [InlineData(ゲームへ入るファイル)]
    [InlineData(プラグインの実行時のファイル)]
    public void ゲームへソースのまま入るファイルでは内部の名前も英語にする(string ファイル){
        Assert.Contains(調べる("internal sealed class 内部の計算 {}",ファイル),static 一件=>一件.Id=="NAME001");
        Assert.Empty(調べる("public sealed class Body { private int 内部の値; }",ファイル));
    }
    [Fact]
    public void 印の無いコンパイルは調べない(){
        Assert.Empty(調べる("public sealed class 速度 {}",普通のファイル,調べる印:false));
    }
    private static ImmutableArray<Diagnostic>調べる(string ソース,string ファイル,bool 調べる印=true){
        var 木=CSharpSyntaxTree.ParseText(ソース,new CSharpParseOptions(LanguageVersion.Latest),ファイル);
        var 参照=new[]{MetadataReference.CreateFromFile(typeof(object).Assembly.Location)};
        var コンパイル=CSharpCompilation.Create("Sample",[木],参照,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return コンパイル.WithAnalyzers([new EnglishApiIdentifierAnalyzer()],new AnalyzerOptions([],new 設定(調べる印)))
            .GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
    }
    private sealed class 設定(bool 調べる印):AnalyzerConfigOptionsProvider {
        private sealed class 値(bool 調べる印):AnalyzerConfigOptions {
            public override bool TryGetValue(string Key,[System.Diagnostics.CodeAnalysis.NotNullWhen(true)]out string?Value){
                Value=Key=="build_property.CgeEnforceEnglishApi"&&調べる印?"true":null;
                return Value is not null;
            }
        }
        private readonly AnalyzerConfigOptions 全体=new 値(調べる印);
        public override AnalyzerConfigOptions GlobalOptions=>this.全体;
        public override AnalyzerConfigOptions GetOptions(SyntaxTree Tree)=>this.全体;
        public override AnalyzerConfigOptions GetOptions(AdditionalText Text)=>this.全体;
    }
}
