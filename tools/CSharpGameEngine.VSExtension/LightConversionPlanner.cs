using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using CSharpGameEngine.Lighting;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.CodeAnalysis;
namespace CSharpGameEngine.VSExtension;
/// <summary>Source-backed conversion; owns no runtime identity or mutable authoring model.</summary>
internal static class LightConversionPlanner {
    internal static string ContractFingerprint(SceneComponent Source,INamedTypeSymbol Target)=>
        SceneComponentInitializerPolicy.CreatePlan(Source.TypeSymbol!,Source.TypeFullyQualifiedName,null).Expression+"|"+
        SceneComponentInitializerPolicy.CreatePlan(Target,SceneTypePredicates.ToFullyQualifiedName(Target),null).Expression;
    internal static bool MatchesLatest(SceneComponent Source,ConvertLightOperation Operation){
        var Target=Source.TypeSymbol?.ContainingNamespace.GetTypeMembers(Operation.Replacement.TypeSymbol!.Name).SingleOrDefault();
        return Target is not null&&ContractFingerprint(Source,Target)==Operation.DefaultsFingerprint;
    }
    internal static Dictionary<string,string>Suggest(SceneComponent Source,INamedTypeSymbol Target,Compilation Compilation){
        var SourceDefaults=SceneComponentInitializerPolicy.CreatePlan(Source.TypeSymbol!,Source.TypeFullyQualifiedName,Compilation);
        var TargetPlan=SceneComponentInitializerPolicy.CreatePlan(Target,SceneTypePredicates.ToFullyQualifiedName(Target),Compilation);
        var Result=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var Member in TargetPlan.Members){
            var Contract=LightValueContract.Find(Member.Value.Type);
            if(Contract is not null&&Contract.TryRead(Member.Value,out var Values,out _)){
                Result[Member.MemberName]=Display(Values);
            }
        }
        bool Read(string Name,out float[]Values){
            var Member=Source.FindMember(Name)??SourceDefaults.Members.FirstOrDefault(Item=>Item.MemberName==Name);
            var Contract=LightValueContract.Find(Member?.Value.Type);
            Values=Array.Empty<float>();
            return Member is not null&&Contract is not null&&Contract.TryRead(Member.Value,out Values,out _)&&Contract.Validate(Values);
        }
        Result["Color"]=Read("Color",out var Color)?Display(Color):string.Empty;
        if(Target.Name!="DirectionalLight"){
            Result["Range"]=Source.TypeSymbol?.Name=="DirectionalLight"?"10":Read("Range",out var Range)?Display(Range):string.Empty;
        }
        var Strength=Target.Name=="DirectionalLight"?"Illuminance":Target.Name=="RectAreaLight"?"Luminance":"Flux";
        if(Source.TypeSymbol?.Name=="DirectionalLight"||Target.Name=="DirectionalLight"){
            Result[Strength]=string.Empty; // dimension-changing conversion requires explicit input
        }else{
            try{
                LuminousFlux Flux;
                if(Source.TypeSymbol?.Name=="RectAreaLight"){
                    if(!Read("Luminance",out var L)||!Read("Size",out var Size)){Result[Strength]=string.Empty;return Result;}
                    Flux=LightPhotometry.RectangleFlux(new Luminance(L[0]),new RectangleSize(Size[0],Size[1]));
                }else{
                    if(!Read("Flux",out var F)){Result[Strength]=string.Empty;return Result;}
                    Flux=new LuminousFlux(F[0]);
                }
                Result[Strength]=(Target.Name=="RectAreaLight"?LightPhotometry.RectangleLuminance(Flux,new RectangleSize(1f,1f)).Nits:Flux.Lumens).ToString("R",CultureInfo.InvariantCulture);
            }catch(ArgumentException){Result[Strength]=string.Empty;}
        }
        return Result;
    }
    internal static bool TryBuild(SceneComponent Source,INamedTypeSymbol Target,Compilation Compilation,
        IReadOnlyDictionary<string,string>Inputs,out SceneComponent Replacement){
        Replacement=null!;
        var TargetName=SceneTypePredicates.ToFullyQualifiedName(Target);
        if(!LightComponentContract.IsLight(Source.TypeFullyQualifiedName)||!LightComponentContract.IsLight(TargetName)||Source.TypeFullyQualifiedName==TargetName){return false;}
        var Plan=SceneComponentInitializerPolicy.CreatePlan(Target,TargetName,Compilation);
        var Members=ImmutableArray.CreateBuilder<SceneComponentMember>();
        foreach(var Member in Plan.Members){
            var Contract=LightValueContract.Find(Member.Value.Type);
            if(Contract is null||!Inputs.TryGetValue(Member.MemberName,out var Text)||!Contract.TryFormatInput(Text,out var Expression)){return false;}
            var Value=SceneComponentInitializerPolicy.CreateValue(Member.MemberSymbol!,Expression,true,Compilation);
            Members.Add(new SceneComponentMember(Member.MemberName,Member.MemberSymbol,Value,SceneSourceLocation.None));
        }
        var Final=Members.ToImmutable();
        var SourceExpression="new "+TargetName+" { "+string.Join(", ",Final.Select(Member=>Member.MemberName+"="+Member.Value.EmitExpressionText))+" }";
        Replacement=new SceneComponent(Target,TargetName,ImmutableArray<SceneValue>.Empty,Final,SourceExpression,SceneSourceLocation.None,SceneComponentOrdering.GetSortKey(Target,TargetName));
        return true;
    }
    private static string Display(IEnumerable<float>Values)=>string.Join(", ",Values.Select(Value=>Value.ToString("R",CultureInfo.InvariantCulture)));
}
