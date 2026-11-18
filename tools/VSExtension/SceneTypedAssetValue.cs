using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
namespace CSharpGameEngine.VSExtension;
/// <summary>Resolves generated image catalog references without evaluating arbitrary C#.</summary>
internal static class SceneTypedAssetValue {
    internal static bool IsTypedAsset(ITypeSymbol?Type,Compilation Compilation){
        return Type is not null&&(SymbolEqualityComparer.Default.Equals(Type,Compilation.GetTypeByMetadataName("CSharpGameEngine.Authoring.TextureAsset"))
            ||SymbolEqualityComparer.Default.Equals(Type,Compilation.GetTypeByMetadataName("CSharpGameEngine.Authoring.ImageAsset")));
    }
    internal static bool TryGetCatalogPath(Compilation Compilation,string ClassFullyQualifiedName,string AssetTypeName,string Expression,out string Path){
        Path=string.Empty;
        var AssetType=Compilation.GetTypeByMetadataName(AssetTypeName);
        var SceneType=Compilation.GetTypeByMetadataName(ClassFullyQualifiedName.Replace("global::",string.Empty));
        if(SceneType is null||!IsTypedAsset(AssetType,Compilation)){ return false; }
        foreach(var Root in Compilation.Assembly.GlobalNamespace.GetTypeMembers("Assets")){
            if(TryFind(Root,Expression,AssetType!,Compilation,SceneType,out _,out Path)){ return true; }
        }
        return false;
    }
    internal static bool TryCreate(Compilation Compilation,SceneModel Model,SceneComponent Component,string MemberName,string Expression,out SceneValue Value){
        Value=null!;
        var ComponentType=Compilation.GetTypeByMetadataName(Component.TypeFullyQualifiedName.Replace("global::",string.Empty));
        var SceneType=Compilation.GetTypeByMetadataName(Model.ClassFullyQualifiedName);
        if(ComponentType is null||SceneType is null){ return false; }
        var Member=SceneJsonSceneAnalyzer.FindSettableMemberSymbol(ComponentType,MemberName);
        var AssetType=Member switch { IFieldSymbol Field=>Field.Type,IPropertySymbol Property=>Property.Type,_=>null };
        if(!IsTypedAsset(AssetType,Compilation)){ return false; }
        foreach(var Root in Compilation.Assembly.GlobalNamespace.GetTypeMembers("Assets")){
            if(TryFind(Root,Expression,AssetType!,Compilation,SceneType,out var Constructor,out var Path)){
                var Literal=SceneEmitExpressionWriter.WriteConstant(Path);
                var Argument=new SceneValue(SceneValueKind.Literal,Compilation.GetSpecialType(SpecialType.System_String),Path,null,
                    ImmutableArray<SceneComponentMember>.Empty,ImmutableArray<SceneValue>.Empty,ImmutableArray<SceneValue>.Empty,
                    Literal,Literal,SceneSourceLocation.None);
                var Emit=SceneEmitExpressionWriter.WriteObjectCreation(SceneEmitExpressionWriter.WriteTypeReference(AssetType!),new[]{Literal},Array.Empty<string>());
                Value=new SceneValue(SceneValueKind.ObjectCreation,AssetType,null,Constructor,
                    ImmutableArray<SceneComponentMember>.Empty,ImmutableArray.Create(Argument),ImmutableArray<SceneValue>.Empty,
                    Emit,Expression,SceneSourceLocation.None);
                return true;
            }
        }
        return false;
    }
    private static bool TryFind(INamedTypeSymbol Type,string Expression,ITypeSymbol AssetType,Compilation Compilation,INamedTypeSymbol SceneType,out IMethodSymbol Constructor,out string Path){
        foreach(var Member in Type.GetMembers()){
            if(string.Equals(SceneEmitExpressionWriter.WriteSymbolReference(Member),Expression,StringComparison.Ordinal)
                &&TryResolve(Member,AssetType,Compilation,SceneType,new HashSet<ISymbol>(SymbolEqualityComparer.Default),0,out Constructor,out Path)){ return true; }
        }
        foreach(var Child in Type.GetTypeMembers()){
            if(TryFind(Child,Expression,AssetType,Compilation,SceneType,out Constructor,out Path)){ return true; }
        }
        Constructor=null!;Path=string.Empty;return false;
    }
    private static bool TryResolve(ISymbol Member,ITypeSymbol AssetType,Compilation Compilation,INamedTypeSymbol SceneType,HashSet<ISymbol> Seen,int Depth,out IMethodSymbol Constructor,out string Path){
        Constructor=null!;Path=string.Empty;
        if(Depth>=32||!Seen.Add(Member)||!IsCatalogMember(Member,AssetType,Compilation,SceneType)){ return false; }
        if(Member is IFieldSymbol Field){
            if(Field.DeclaringSyntaxReferences.Length!=1||Field.DeclaringSyntaxReferences[0].GetSyntax() is not VariableDeclaratorSyntax { Initializer.Value: { } Initializer }){ return false; }
            var Semantic=Compilation.GetSemanticModel(Initializer.SyntaxTree);
            if(Semantic.GetOperation(Initializer) is not IObjectCreationOperation Creation||Creation.Initializer is not null
                ||Creation.Constructor is not { } Candidate||!SymbolEqualityComparer.Default.Equals(Candidate.ContainingType,AssetType)
                ||Candidate.DeclaredAccessibility!=Accessibility.Public||Candidate.Parameters.Length!=1
                ||Candidate.Parameters[0].Name!="Path"||Candidate.Parameters[0].Type.SpecialType!=SpecialType.System_String
                ||Creation.Arguments.Length!=1||Creation.Arguments[0].Value.ConstantValue is not { HasValue:true,Value:string Text }){ return false; }
            Constructor=Candidate;Path=Text;return true;
        }
        if(Member is not IPropertySymbol Property||Property.DeclaringSyntaxReferences.Length!=1
            ||Property.DeclaringSyntaxReferences[0].GetSyntax() is not PropertyDeclarationSyntax { ExpressionBody.Expression: { } Alias }
            ||Alias is not IdentifierNameSyntax and not MemberAccessExpressionSyntax){ return false; }
        var AliasSemantic=Compilation.GetSemanticModel(Alias.SyntaxTree);
        var Target=AliasSemantic.GetSymbolInfo(Alias).Symbol;
        return Target is not null&&TryResolve(Target,AssetType,Compilation,SceneType,Seen,Depth+1,out Constructor,out Path);
    }
    private static bool IsCatalogMember(ISymbol Member,ITypeSymbol AssetType,Compilation Compilation,INamedTypeSymbol SceneType){
        if(Member.DeclaredAccessibility!=Accessibility.Public||!Member.IsStatic||!Compilation.IsSymbolAccessibleWithin(Member,SceneType)
            ||!SymbolEqualityComparer.Default.Equals(Member.ContainingAssembly,Compilation.Assembly)){ return false; }
        var Type=Member switch {
            IFieldSymbol { IsReadOnly:true,IsConst:false } Field=>Field.Type,
            IPropertySymbol { GetMethod.DeclaredAccessibility:Accessibility.Public,SetMethod:null } Property=>Property.Type,
            _=>null,
        };
        if(!SymbolEqualityComparer.Default.Equals(Type,AssetType)){ return false; }
        var Container=Member.ContainingType;
        if(Container is null){ return false; }
        for(var Current=Container;Current is not null;Current=Current.ContainingType){
            if(!Current.IsStatic){ return false; }
            foreach(var Constructor in Current.StaticConstructors){ if(!Constructor.IsImplicitlyDeclared){ return false; } }
        }
        while(Container.ContainingType is not null){ Container=Container.ContainingType; }
        return Container.Name=="Assets"&&Container.ContainingNamespace.IsGlobalNamespace;
    }
}
