using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
namespace CSharpGameEngine.CodeAnalysis;
/// <summary>増分生成へ渡すのは式の文字列だけ。シンボルを保持しない。</summary>
internal readonly struct SnapshotSchemaMap(Dictionary<string,string>Expressions,Dictionary<string,string>Writers,Dictionary<string,string>Readers,HashSet<string>TransientTypes,bool SnapshotRequired):IEquatable<SnapshotSchemaMap> {
    private readonly Dictionary<string,string>Expressions=Expressions;
    private readonly Dictionary<string,string>Writers=Writers;
    private readonly Dictionary<string,string>Readers=Readers;
    private readonly HashSet<string>TransientTypes=TransientTypes;
    internal readonly bool SnapshotRequired=SnapshotRequired;
    private readonly string Key=SnapshotRequired+"\n"+string.Join("\n",Expressions.Concat(Writers).Concat(Readers).OrderBy(static Pair=>Pair.Key,StringComparer.Ordinal).Select(static Pair=>Pair.Key+"="+Pair.Value));
    internal string?Reader(string Type)=>this.Readers.TryGetValue(Type,out var Body)?Body:null;
    internal string?Writer(string Type)=>this.Writers.TryGetValue(Type,out var Body)?Body:null;
    internal string Get(string Type)=>this.Expressions.TryGetValue(Type,out var Expression)?Expression:
        "new global::CSharpGameEngine.Entities.EcsSnapshotComponentDescriptor("+SymbolDisplay.FormatLiteral(Type.Replace("global::",string.Empty),true)+", false, \"unsupported\")";
    internal bool IsTransient(string Type)=>this.TransientTypes.Contains(Type);
    internal string?Issue(string Type)=>!this.Expressions.ContainsKey(Type)?"its saved structure could not be resolved"
        :!this.IsTransient(Type)&&(!this.Writers.ContainsKey(Type)||!this.Readers.ContainsKey(Type))?"its members or JSON rules need an explicit static codec":null;
    public bool Equals(SnapshotSchemaMap Other)=>this.Key==Other.Key;
    public override bool Equals(object?Other)=>Other is SnapshotSchemaMap Map&&this.Equals(Map);
    public override int GetHashCode()=>StringComparer.Ordinal.GetHashCode(this.Key);
    internal static SnapshotSchemaMap Build(Compilation Compilation,ImmutableArray<GameSystemResult>Systems,ImmutableArray<SceneEmitModel>Scenes,ImmutableArray<SceneRuntimeComponentPair>Pairs){
        var Names=new HashSet<string>(StringComparer.Ordinal);
        foreach(var System in Systems){ foreach(var Method in System.Methods){ foreach(var Component in Method.Components){ Names.Add(Component.TypeFullyQualifiedName); }foreach(var Relation in Method.Relations){ Names.Add(Relation.ComponentTypeFullyQualifiedName); } } }
        Names.Add(GameSystemEntryPointEmitter.BootstrapComponentFullyQualifiedName);
        var RuntimeComponents=SceneRuntimeComponentMap.Build(Pairs,out _);
        foreach(var Scene in Scenes){
            foreach(var Set in SceneComponentSetCollector.Collect(Scene,RuntimeComponents)){ foreach(var Name in Set){ Names.Add(Name); } }
            if(Scene.CanEmit&&Scene.IsStartupScene){
                foreach(var Method in SceneStartupSystemEmitter.BuildSyntheticSystemResult(Scene).Methods){ foreach(var Component in Method.Components){ Names.Add(Component.TypeFullyQualifiedName); } }
            }
        }
        foreach(var Pair in Pairs){ Names.Add(Pair.AuthoredFullyQualifiedName);Names.Add(Pair.RuntimeFullyQualifiedName); }
        var Expressions=new Dictionary<string,string>(StringComparer.Ordinal);
        var Writers=new Dictionary<string,string>(StringComparer.Ordinal);
        var Readers=new Dictionary<string,string>(StringComparer.Ordinal);
        var TransientTypes=new HashSet<string>(StringComparer.Ordinal);
        foreach(var Name in Names){
            var Type=SnapshotShapeBuilder.Resolve(Compilation,Name);
            if(Type is null){ continue; }
            try{
                var Shape=SnapshotShapeBuilder.Build(Type);
                if(Shape.Transient){ TransientTypes.Add(Name); }
                Expressions[Name]="new global::CSharpGameEngine.Entities.EcsSnapshotComponentDescriptor("+SymbolDisplay.FormatLiteral(Shape.TypeId,true)+", "+(Shape.Transient?"true":"false")+", "+SymbolDisplay.FormatLiteral(Shape.Shape,true)+")";
                if(!Shape.Transient){
                    if(SnapshotGraphCodecBuilder.BuildPortable(Type) is {}Graph){ Writers[Name]=Graph.Writer;Readers[Name]=Graph.Reader; }
                }
            }catch(NotSupportedException){
                // 出力対象のComponentに限り、Emitterがビルド診断にする。
            }
        }
        var Required=Compilation.SyntaxTrees.Any(Tree=>RequiresSnapshot(Compilation,Tree));
        return new SnapshotSchemaMap(Expressions,Writers,Readers,TransientTypes,Required);
    }
    private static bool RequiresSnapshot(Compilation Compilation,SyntaxTree Tree){
        SemanticModel?Model=null;
        foreach(var Invocation in Tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()){
            var Name=Invocation.Expression switch{
                MemberAccessExpressionSyntax Access=>Access.Name.Identifier.ValueText,
                MemberBindingExpressionSyntax Binding=>Binding.Name.Identifier.ValueText,
                IdentifierNameSyntax Identifier=>Identifier.Identifier.ValueText,
                _=>string.Empty,
            };
            if(!IsSnapshotMethod(Name)){ continue; }
            Model??=Compilation.GetSemanticModel(Tree);
            ITypeSymbol?Receiver=Invocation.Expression switch{
                MemberAccessExpressionSyntax Access=>Model.GetTypeInfo(Access.Expression).Type,
                MemberBindingExpressionSyntax=>Invocation.Ancestors().OfType<ConditionalAccessExpressionSyntax>().FirstOrDefault() is {}Conditional?Model.GetTypeInfo(Conditional.Expression).Type:null,
                IdentifierNameSyntax=>Model.GetEnclosingSymbol(Invocation.SpanStart)?.ContainingType,
                _=>null,
            };
            if(Receiver is INamedTypeSymbol Nullable&&Nullable.OriginalDefinition.SpecialType==SpecialType.System_Nullable_T){ Receiver=Nullable.TypeArguments[0]; }
            if(Receiver?.ToDisplayString()=="CSharpGameEngine.Entities.World"){ return true; }
        }
        return false;
    }
    private static bool IsSnapshotMethod(string Name)=>Name is "CaptureSnapshot" or "UpdateSnapshot" or "RestoreSnapshot" or "ApplyPendingSnapshot";
}
