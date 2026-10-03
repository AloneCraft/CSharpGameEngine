using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
namespace CSharpGameEngine.SceneAnalysis;
/// <summary>One whitelist for physical-value authoring, validation, labels and constructor argument order.</summary>
internal sealed class LightValueContract {
    private LightValueContract(string Name,string Unit,string[]Parameters,bool Positive){
        this.Name=Name;this.Unit=Unit;this.Parameters=Parameters;this.Positive=Positive;
    }
    internal string Name{get;}
    internal string Unit{get;}
    internal string[]Parameters{get;}
    private bool Positive{get;}
    internal string TypeName=>"global::CSharpGameEngine.Lighting."+this.Name;
    /// <summary>What <see cref="Validate"/> requires, for a diagnostic.</summary>
    internal string Requirement=>this.Name=="SpotConeAngles"
        ?"positive angles with the inner one at most the outer one and the outer one at most 180 degrees"
        :this.Positive?"finite positive values":"finite non-negative values";
    private static readonly LightValueContract[]All={
        new("LuminousFlux","lm",new[]{"Lumens"},false),
        new("Illuminance","lux",new[]{"Lux"},false),
        new("Luminance","nit",new[]{"Nits"},false),
        new("LuminousIntensity","cd",new[]{"Candela"},false),
        new("LightRange","m",new[]{"Meters"},true),
        new("ShadowBias","m",new[]{"Meters"},false),
        new("LinearLightColor","Linear RGB",new[]{"R","G","B"},false),
        new("RectangleSize","m",new[]{"WidthMeters","HeightMeters"},true),
        new("SpotConeAngles","degree",new[]{"InnerFullAngleDegrees","OuterFullAngleDegrees"},true),
    };
    internal static LightValueContract?Find(ITypeSymbol?Type)=>Find(Type?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
    internal static LightValueContract?Find(string?Type)=>All.FirstOrDefault(Item=>Item.TypeName==Type);
    internal bool Validate(IReadOnlyList<float>Values){
        if(Values.Count!=this.Parameters.Length){return false;}
        foreach(var Value in Values){
            if(float.IsNaN(Value)||float.IsInfinity(Value)||(this.Positive?Value<=0f:Value<0f)){return false;}
        }
        return this.Name!="SpotConeAngles"||(Values[0]<=Values[1]&&Values[1]<=180f);
    }
    internal string Format(IReadOnlyList<float>Values)=>"new "+this.TypeName+"("+string.Join(", ",Values.Select(Value=>Value.ToString("R",CultureInfo.InvariantCulture)+"f"))+")";
    internal bool TryFormatInput(string Text,out string Expression){
        Expression=string.Empty;
        var Parts=Text.Split(',');
        var Values=new float[Parts.Length];
        for(var Index=0;Index<Parts.Length;Index++){
            if(!float.TryParse(Parts[Index].Trim(),NumberStyles.Float,CultureInfo.InvariantCulture,out Values[Index])){return false;}
        }
        if(!this.Validate(Values)){return false;}
        Expression=this.Format(Values);
        return true;
    }
    internal bool MatchesConstructor(IMethodSymbol Constructor)=>
        Constructor.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)==this.TypeName
        &&Constructor.Parameters.Select(Parameter=>Parameter.Name).SequenceEqual(this.Parameters,StringComparer.Ordinal)
        &&Constructor.Parameters.All(Parameter=>Parameter.Type.SpecialType==SpecialType.System_Single&&Parameter.RefKind==RefKind.None);
    internal bool TryRead(SceneValue Value,out float[]Values,out bool Editable){
        Values=new float[this.Parameters.Length];Editable=false;
        if(Value.Kind==SceneValueKind.Default){return true;}
        if(Value.Kind==SceneValueKind.ObjectCreation&&Value.ReferencedSymbol is IMethodSymbol { IsImplicitlyDeclared:true,Parameters.Length:0 } DefaultConstructor&&DefaultConstructor.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)==this.TypeName){return true;}
        if(Value.Kind!=SceneValueKind.ObjectCreation||Value.Arguments.Length!=this.Parameters.Length){return false;}
        if(SyntaxFactory.ParseExpression(Value.SourceExpressionText) is not BaseObjectCreationExpressionSyntax Creation||Creation.ArgumentList is null){return false;}
        // The semantic constructor is authoritative; same-arity overloads never select a schema.
        if(Value.ReferencedSymbol is not IMethodSymbol Constructor||!this.MatchesConstructor(Constructor)){return false;}
        var Seen=new bool[Values.Length];
        for(var Index=0;Index<Value.Arguments.Length;Index++){
            var Name=Creation.ArgumentList.Arguments[Index].NameColon?.Name.Identifier.ValueText;
            var Target=Name is null?Index:Array.IndexOf(this.Parameters,Name);
            if(Target<0||Target>=Values.Length||Seen[Target]){return false;}
            var Constant=Value.Arguments[Index].ConstantValue;
            if(Constant is null){return false;}
            try{Values[Target]=Convert.ToSingle(Constant,CultureInfo.InvariantCulture);}catch(Exception Error)when(Error is FormatException||Error is InvalidCastException||Error is OverflowException){return false;}
            Seen[Target]=true;
        }
        Editable=Value.Arguments.All(Argument=>Argument.Kind==SceneValueKind.Literal);
        return true;
    }
    internal bool TryReadExpression(string Expression,out float[]Values){
        Values=new float[this.Parameters.Length];
        if(SyntaxFactory.ParseExpression(Expression) is not BaseObjectCreationExpressionSyntax Creation||Creation.ArgumentList?.Arguments.Count!=Values.Length){return false;}
        var Seen=new bool[Values.Length];
        for(var Index=0;Index<Values.Length;Index++){
            var Argument=Creation.ArgumentList.Arguments[Index];
            var Name=Argument.NameColon?.Name.Identifier.ValueText;
            var Target=Name is null?Index:Array.IndexOf(this.Parameters,Name);
            if(Target<0||Target>=Values.Length||Seen[Target]){return false;}
            var Text=Argument.Expression.ToString().Trim().Replace("_",string.Empty);
            if(Text.EndsWith("f",StringComparison.OrdinalIgnoreCase)){Text=Text.Substring(0,Text.Length-1);}
            if(!float.TryParse(Text,NumberStyles.Float,CultureInfo.InvariantCulture,out Values[Target])){return false;}
            Seen[Target]=true;
        }
        return this.Validate(Values);
    }
}
