using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
namespace CSharpGameEngine.CodeAnalysis;
/// <summary>保存構造をビルド時に抽出する。返す値は文字列だけで、Compilationを保持しない。</summary>
internal static class SnapshotShapeBuilder {
    internal static INamedTypeSymbol?Resolve(Compilation Compilation,string Name){
        var MetadataName=Name.StartsWith("global::",StringComparison.Ordinal)?Name.Substring(8):Name;
        while(true){
            if(Compilation.GetTypeByMetadataName(MetadataName) is {}Type){ return Type; }
            var Separator=MetadataName.LastIndexOf('.');
            if(Separator<0){ return null; }
            MetadataName=MetadataName.Substring(0,Separator)+"+"+MetadataName.Substring(Separator+1);
        }
    }
    /// <summary>実数は数値設定によらない共通の識別子にする（ADR-0095）。数値設定の違う Snapshot も同じ形として読める。</summary>
    private const string RealTypeId="CSharpGameEngine.Numerics.NumericValue";
    private const string FixedTypeId="CSharpGameEngine.Numerics.LogicalScalar";
    internal static (string TypeId,bool Transient,string Shape)Build(INamedTypeSymbol Type){
        var Transient=Type.AllInterfaces.Any(static Interface=>TypeId(Interface)=="CSharpGameEngine.Entities.IRuntimeComponent")
            ||Type.GetAttributes().Any(static Attribute=>Attribute.AttributeClass is {}AttributeType&&TypeId(AttributeType)=="CSharpGameEngine.Entities.EcsSnapshotTransientAttribute");
        return(TypeId(Type),Transient,Transient?"transient":Describe(Type,new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default)));
    }
    private static string Describe(ITypeSymbol Type,HashSet<ITypeSymbol>Stack){
        var Id=TypeId(Type);
        if(IsReal(Type)){ return RealTypeId; }
        if(IsTerminal(Type,Id)){ return Id; }
        if(Type is INamedTypeSymbol {OriginalDefinition.SpecialType:SpecialType.System_Nullable_T}Nullable){ return "nullable("+Describe(Nullable.TypeArguments[0],Stack)+")"; }
        if(Type is INamedTypeSymbol {TypeKind:TypeKind.Enum}Enum){ return "enum("+Id+":"+TypeId(Enum.EnumUnderlyingType!)+")"; }
        if(Type is not INamedTypeSymbol {IsValueType:true}Value||!Stack.Add(Type)){ throw new NotSupportedException("Snapshot field must be a nonrecursive value or string: "+Id); }
        try{
            var Fields=Value.GetMembers().OfType<IFieldSymbol>().Where(static Field=>!Field.IsStatic&&Field.DeclaredAccessibility==Accessibility.Public).ToArray();
            var AllProperties=Value.GetMembers().OfType<IPropertySymbol>().Where(static Property=>!Property.IsStatic&&Property.DeclaredAccessibility==Accessibility.Public&&!Property.IsIndexer).ToArray();
            var Properties=AllProperties.Where(static Property=>Property.GetMethod?.DeclaredAccessibility==Accessibility.Public).ToArray();
            var Constructors=Value.InstanceConstructors.Where(static Constructor=>!Constructor.IsImplicitlyDeclared&&Constructor.DeclaredAccessibility==Accessibility.Public).ToArray();
            var Constructed=Fields.Length==0&&AllProperties.All(static Property=>Property.SetMethod is null)&&Constructors.Length==1&&Constructors[0].Parameters.Length>0;
            var Members=new List<(string Name,ITypeSymbol Type,ISymbol Declaration)>();
            if(Constructed){
                foreach(var Parameter in Constructors[0].Parameters){
                    var Property=Properties.FirstOrDefault(Candidate=>string.Equals(Candidate.Name,Parameter.Name,StringComparison.OrdinalIgnoreCase)&&SymbolEqualityComparer.Default.Equals(Candidate.Type,Parameter.Type));
                    if(Property is null){ Constructed=false;break; }
                    Members.Add((Property.Name,Property.Type,Property));
                }
            }
            if(!Constructed){
                Members.Clear();
                Members.AddRange(Fields.Select(static Field=>(Field.Name,Field.Type,(ISymbol)Field)));
                Members.AddRange(Properties.Select(static Property=>(Property.Name,Property.Type,(ISymbol)Property)));
            }
            return Id+"{"+string.Join(";",Members.OrderBy(static Member=>Member.Name,StringComparer.Ordinal).Select(Member=>Member.Name+":"+Describe(Member.Type,Stack)))+"}";
        }finally{ Stack.Remove(Type); }
    }
    private static bool IsReal(ITypeSymbol Type)=>Type.SpecialType is SpecialType.System_Single or SpecialType.System_Double||TypeId(Type)==FixedTypeId;
    private static bool IsTerminal(ITypeSymbol Type,string Id)=>Type.SpecialType is SpecialType.System_Boolean or SpecialType.System_Byte or SpecialType.System_SByte
        or SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64
        or SpecialType.System_Char or SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_IntPtr or SpecialType.System_UIntPtr or SpecialType.System_String
        ||Id is "System.Int128" or "System.UInt128" or "CSharpGameEngine.Numerics.LogicalScalar" or "CSharpGameEngine.Core.StableId"
        or "CSharpGameEngine.Contracts.ReferenceFrameId" or "CSharpGameEngine.Contracts.WorldCellId" or "CSharpGameEngine.Contracts.PersistentEntityId"
        or "CSharpGameEngine.Contracts.Hash256" or "CSharpGameEngine.Contracts.AssetId" or "CSharpGameEngine.Contracts.SubAssetId";
    private static string TypeId(ITypeSymbol Type){
        if(Type.ContainingType is {}Parent){ return TypeId(Parent)+"+"+Type.MetadataName; }
        var Namespace=Type.ContainingNamespace is {IsGlobalNamespace:false}Owner?Owner.ToDisplayString():null;
        return string.IsNullOrEmpty(Namespace)?Type.MetadataName:Namespace+"."+Type.MetadataName;
    }
}
