using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
namespace CSharpGameEngine.CodeAnalysis;
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NumericProfileCompatibilityAnalyzer:DiagnosticAnalyzer {
    private static readonly DiagnosticDescriptor Mismatch=new("NUM002","Numeric dependency profiles must match","Assembly '{0}' uses numeric profile '{1}', but this compilation selects '{2}'","CSharpGameEngine.Numerics",DiagnosticSeverity.Error,true,customTags:[WellKnownDiagnosticTags.CompilationEnd]);
    public override ImmutableArray<DiagnosticDescriptor>SupportedDiagnostics=>[Mismatch];
    public override void Initialize(AnalysisContext Context){
        Context.EnableConcurrentExecution();Context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        Context.RegisterCompilationAction(static Analysis=>{
            if(!Analysis.Options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue("build_property.CgeNumericProfileId",out var Expected)||string.IsNullOrEmpty(Expected)){ return; }
            foreach(var Reference in Analysis.Compilation.SourceModule.ReferencedAssemblySymbols){
                var Profile=Reference.GetAttributes().FirstOrDefault(static Attribute=>Attribute.AttributeClass?.ToDisplayString()=="System.Reflection.AssemblyMetadataAttribute"&&Attribute.ConstructorArguments.Length==2&&Attribute.ConstructorArguments[0].Value as string=="CgeNumericProfile")?.ConstructorArguments[1].Value as string;
                if(Profile is null&&Reference.Name is not ("CSharpGameEngine.Numerics" or "CSharpGameEngine.World" or "CSharpGameEngine.Entities")){ continue; }
                if(!string.Equals(Profile,Expected,StringComparison.Ordinal)){
                    Analysis.ReportDiagnostic(Diagnostic.Create(Mismatch,Location.None,Reference.Name,Profile??"unspecified",Expected));
                }
            }
        });
    }
}
