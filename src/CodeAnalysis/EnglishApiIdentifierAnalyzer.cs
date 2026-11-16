using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
namespace CSharpGameEngine.CodeAnalysis;
/// <summary>
/// NAME001（ADR-0099）：利用者が使うエンジンの API の名前は英語（ASCII の英数字と <c>_</c>）だけで書く。
/// 対象は、外から見える宣言（public・protected の型、メンバー、引数、型引数、列挙値）。ソースのまま利用者のゲームへ入るファイル
/// （<c>src/Runtime</c>、<c>src/Entities</c>、<c>plugins/*/Runtime</c>）では、利用者のコードから見える <c>internal</c> も対象にする。
/// それ以外の内部の名前とテストは日本語でよい。MSBuild の <c>CgeEnforceEnglishApi</c> が <c>true</c> のコンパイル（エンジン自身の
/// プロジェクト）だけで働き、利用者のゲームの名前の付け方は縛らない。
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EnglishApiIdentifierAnalyzer:DiagnosticAnalyzer {
    internal static readonly DiagnosticDescriptor NonEnglishApiName=new(
        "NAME001",
        "Engine API names are written in English",
        "'{0}' is part of the engine API that users see, so its name must use only ASCII letters, digits and '_'",
        "CSharpGameEngine.Naming",
        DiagnosticSeverity.Error,
        true,
        "Names that users of the engine read (public and protected declarations, and internal declarations in source that games compile) are written in English. Names inside the engine and in tests may be Japanese (ADR-0099).");
    public override ImmutableArray<DiagnosticDescriptor>SupportedDiagnostics=>[NonEnglishApiName];
    public override void Initialize(AnalysisContext Context){
        Context.EnableConcurrentExecution();
        Context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        Context.RegisterCompilationStartAction(static StartContext=>{
            if(!StartContext.Options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue("build_property.CgeEnforceEnglishApi",out var Value)
                ||!string.Equals(Value,"true",StringComparison.OrdinalIgnoreCase)){ return; }
            StartContext.RegisterSymbolAction(AnalyzeSymbol,SymbolKind.NamedType,SymbolKind.Method,SymbolKind.Property,SymbolKind.Field,SymbolKind.Event);
        });
    }
    private static void AnalyzeSymbol(SymbolAnalysisContext Context){
        var Symbol=Context.Symbol;
        if(Symbol.IsImplicitlyDeclared){ return; }
        foreach(var Location in Symbol.Locations){
            if(!Location.IsInSource||!IsUserVisible(Symbol,Location)){ continue; }
            // インデクサーの名前（this[]）はコンパイラーが決めるので、引数の名前だけを確かめる。
            if(Symbol is not IPropertySymbol { IsIndexer: true }){ Report(Context,Symbol,Symbol.Name,Location); }
            if(Symbol is IMethodSymbol Method){
                foreach(var Parameter in Method.Parameters){ Report(Context,Parameter,Parameter.Name,Parameter.Locations.FirstOrDefault(static Item=>Item.IsInSource)??Location); }
                foreach(var TypeParameter in Method.TypeParameters){ Report(Context,TypeParameter,TypeParameter.Name,TypeParameter.Locations.FirstOrDefault(static Item=>Item.IsInSource)??Location); }
            }
            if(Symbol is IPropertySymbol Indexer){
                foreach(var Parameter in Indexer.Parameters){ Report(Context,Parameter,Parameter.Name,Parameter.Locations.FirstOrDefault(static Item=>Item.IsInSource)??Location); }
            }
            if(Symbol is INamedTypeSymbol Type){
                foreach(var TypeParameter in Type.TypeParameters){ Report(Context,TypeParameter,TypeParameter.Name,TypeParameter.Locations.FirstOrDefault(static Item=>Item.IsInSource)??Location); }
            }
            return;
        }
    }
    private static void Report(SymbolAnalysisContext Context,ISymbol Symbol,string Name,Location Location){
        if(Name.Length==0||IsEnglish(Name)){ return; }
        // 演算子・コンストラクター・アクセサーなどは、名前をコンパイラーが決める。
        if(Symbol is IMethodSymbol { MethodKind: not (MethodKind.Ordinary or MethodKind.DelegateInvoke or MethodKind.LocalFunction) }){ return; }
        Context.ReportDiagnostic(Diagnostic.Create(NonEnglishApiName,Location,Name));
    }
    internal static bool IsEnglish(string Name){
        foreach(var Character in Name){
            if(!(Character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_')){ return false; }
        }
        return true;
    }
    /// <summary>利用者から見えるか。外へ公開された宣言か、ソースのまま利用者のゲームへ入るファイルの private でない宣言。</summary>
    private static bool IsUserVisible(ISymbol Symbol,Location Location){
        if(IsExternallyVisible(Symbol)){ return true; }
        return IsCompiledIntoGames(Location.SourceTree?.FilePath)&&!IsPrivateAnywhere(Symbol);
    }
    private static bool IsExternallyVisible(ISymbol Symbol){
        for(var Current=Symbol;Current is not null and not INamespaceSymbol;Current=Current.ContainingSymbol){
            if(Current.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal)){ return false; }
        }
        return true;
    }
    private static bool IsPrivateAnywhere(ISymbol Symbol){
        for(var Current=Symbol;Current is not null and not INamespaceSymbol;Current=Current.ContainingSymbol){
            if(Current.DeclaredAccessibility is Accessibility.Private or Accessibility.ProtectedAndInternal){ return true; }
        }
        return false;
    }
    /// <summary>ソースのまま利用者のゲームへ取り込まれるファイルか（Game.props と SDK の包みが取り込む場所）。</summary>
    internal static bool IsCompiledIntoGames(string?FilePath){
        if(string.IsNullOrEmpty(FilePath)){ return false; }
        var Path=FilePath!.Replace('\\','/');
        if(Path.Contains("/src/Runtime/")||Path.Contains("/src/Entities/")){ return true; }
        var Plugins=Path.IndexOf("/plugins/",StringComparison.Ordinal);
        return Plugins>=0&&Path.IndexOf("/Runtime/",Plugins,StringComparison.Ordinal)>=0;
    }
}
