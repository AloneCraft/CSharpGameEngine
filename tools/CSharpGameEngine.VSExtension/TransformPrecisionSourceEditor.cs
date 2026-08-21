using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Immutable precision request expressed by the source-of-truth constants in a Scene project.
/// </summary>
internal readonly struct TransformPrecisionConfiguration {
    internal TransformPrecisionConfiguration(int TotalBits,int FractionalBits){
        this.TotalBits=TotalBits;
        this.FractionalBits=FractionalBits;
    }

    internal int TotalBits { get; }

    internal int FractionalBits { get; }
}

/// <summary>
/// Keeps the precision contract shared by the generator, source editor and VS Options preview.
/// The allowed layouts are deliberately finite: generated limbs must have a predictable layout.
/// </summary>
internal static class TransformPrecisionPolicy {
    internal const int DefaultTotalBits=384;
    internal const int DefaultFractionalBits=173;

    private static readonly int[] SupportedTotalBitValues=[64,128,192,256,320,384,448,512];

    internal static IReadOnlyList<int> SupportedTotalBits=>SupportedTotalBitValues;

    internal static TransformPrecisionValidation Validate(int TotalBits,int FractionalBits){
        if(Array.IndexOf(SupportedTotalBitValues,TotalBits)<0){
            return TransformPrecisionValidation.Invalid(
                "総ビット数は 64, 128, 192, 256, 320, 384, 448, 512 のいずれかにしてください。");
        }

        if(FractionalBits<1||FractionalBits>TotalBits-3){
            return TransformPrecisionValidation.Invalid(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "小数部ビット数は 1 から {0} の範囲にしてください。",
                    TotalBits-3));
        }

        return TransformPrecisionValidation.Valid();
    }

    internal static TransformPrecisionPreview CreatePreview(int TotalBits,int FractionalBits){
        TransformPrecisionValidation Validation=Validate(TotalBits,FractionalBits);
        if(!Validation.IsValid){
            return TransformPrecisionPreview.Invalid(Validation.ErrorMessage!);
        }

        int IntegerBits=TotalBits-FractionalBits;
        BigInteger Minimum=-(BigInteger.One<<(IntegerBits-1));
        BigInteger Maximum=(BigInteger.One<<(IntegerBits-1))-BigInteger.One;
        double Resolution=Math.Pow(2d,-FractionalBits);
        return TransformPrecisionPreview.Valid(
            IntegerBits,
            string.Format(
                CultureInfo.InvariantCulture,
                "Q{0}.{1} (符号を含む整数部 {0} bit)",
                IntegerBits,FractionalBits),
            string.Format(
                CultureInfo.InvariantCulture,
                "2^-{0} m ≈ {1:E3} m",
                FractionalBits,Resolution),
            string.Format(
                CultureInfo.InvariantCulture,
                "{0} m ～ {1} m",
                Minimum,Maximum));
    }
}

/// <summary>Result of validating a requested fixed-point layout.</summary>
internal readonly struct TransformPrecisionValidation {
    private TransformPrecisionValidation(bool IsValid,string?ErrorMessage){
        this.IsValid=IsValid;
        this.ErrorMessage=ErrorMessage;
    }

    internal bool IsValid { get; }

    internal string?ErrorMessage { get; }

    internal static TransformPrecisionValidation Valid()=>new(true,null);

    internal static TransformPrecisionValidation Invalid(string ErrorMessage)=>new(false,ErrorMessage);
}

/// <summary>Human-readable, culture-invariant precision information shown by Tools&gt;Options.</summary>
internal sealed class TransformPrecisionPreview {
    private TransformPrecisionPreview(
        bool IsValid,int IntegerBits,string?Format,string?Resolution,string?Range,string?ErrorMessage){
        this.IsValid=IsValid;
        this.IntegerBits=IntegerBits;
        this.Format=Format;
        this.Resolution=Resolution;
        this.Range=Range;
        this.ErrorMessage=ErrorMessage;
    }

    internal bool IsValid { get; }

    internal int IntegerBits { get; }

    internal string?Format { get; }

    internal string?Resolution { get; }

    internal string?Range { get; }

    internal string?ErrorMessage { get; }

    internal static TransformPrecisionPreview Valid(
        int IntegerBits,string Format,string Resolution,string Range)=>
        new(true,IntegerBits,Format,Resolution,Range,null);

    internal static TransformPrecisionPreview Invalid(string ErrorMessage)=>
        new(false,0,null,null,null,ErrorMessage);
}

/// <summary>
/// Parsed ownership of the two source constants.  Editing is intentionally refused when the
/// source is ambiguous rather than guessing between partial classes or generated declarations.
/// </summary>
internal sealed class TransformPrecisionSourceAnalysis {
    private TransformPrecisionSourceAnalysis(
        bool IsEditable,TransformPrecisionConfiguration?Configuration,
        VariableDeclaratorSyntax?TotalBitsVariable,VariableDeclaratorSyntax?FractionalBitsVariable,
        string?ErrorMessage){
        this.IsEditable=IsEditable;
        this.Configuration=Configuration;
        this.TotalBitsVariable=TotalBitsVariable;
        this.FractionalBitsVariable=FractionalBitsVariable;
        this.ErrorMessage=ErrorMessage;
    }

    internal bool IsEditable { get; }

    internal TransformPrecisionConfiguration?Configuration { get; }

    internal VariableDeclaratorSyntax?TotalBitsVariable { get; }

    internal VariableDeclaratorSyntax?FractionalBitsVariable { get; }

    internal string?ErrorMessage { get; }

    internal static TransformPrecisionSourceAnalysis Editable(
        TransformPrecisionConfiguration Configuration,
        VariableDeclaratorSyntax TotalBitsVariable,VariableDeclaratorSyntax FractionalBitsVariable)=>
        new(true,Configuration,TotalBitsVariable,FractionalBitsVariable,null);

    internal static TransformPrecisionSourceAnalysis NotEditable(string ErrorMessage)=>
        new(false,null,null,null,ErrorMessage);
}

/// <summary>Result of replacing the two canonical constants in a Roslyn syntax root.</summary>
internal sealed class TransformPrecisionSourceEditResult {
    private TransformPrecisionSourceEditResult(bool Succeeded,SyntaxNode?Root,string?ErrorMessage){
        this.Succeeded=Succeeded;
        this.Root=Root;
        this.ErrorMessage=ErrorMessage;
    }

    internal bool Succeeded { get; }

    internal SyntaxNode?Root { get; }

    internal string?ErrorMessage { get; }

    internal static TransformPrecisionSourceEditResult Success(SyntaxNode Root)=>new(true,Root,null);

    internal static TransformPrecisionSourceEditResult Failure(string ErrorMessage)=>new(false,null,ErrorMessage);
}

/// <summary>
/// Roslyn-only editor for the Scene project's <c>GameEnginePrecision</c> source file.  It edits
/// exactly <c>TotalBits</c> and <c>FractionalBits</c>; the assembly attribute continues to reference
/// those constants, so it remains the single source of truth and generator input.
/// </summary>
internal static class TransformPrecisionSourceEditor {
    internal const string PrecisionClassName="GameEnginePrecision";
    internal const string TotalBitsName="TotalBits";
    internal const string FractionalBitsName="FractionalBits";

    internal static TransformPrecisionSourceAnalysis Analyze(SyntaxNode Root){
        if(Root is null){
            throw new ArgumentNullException(nameof(Root));
        }

        ClassDeclarationSyntax[] Classes=Root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .Where(Declaration=>string.Equals(Declaration.Identifier.ValueText,PrecisionClassName,StringComparison.Ordinal))
            .ToArray();
        if(Classes.Length==0){
            return TransformPrecisionSourceAnalysis.NotEditable(
                "GameEnginePrecision クラスがこのソースにありません。");
        }

        if(Classes.Length!=1){
            return TransformPrecisionSourceAnalysis.NotEditable(
                "GameEnginePrecision クラスがこのソースで一意に解決できません。");
        }

        if(!TryGetIntConstant(Classes[0],TotalBitsName,out VariableDeclaratorSyntax?TotalBits,out int TotalValue,out string?Error)
            ||TotalBits is null){
            return TransformPrecisionSourceAnalysis.NotEditable(Error!);
        }

        if(!TryGetIntConstant(Classes[0],FractionalBitsName,out VariableDeclaratorSyntax?FractionalBits,out int FractionalValue,out Error)
            ||FractionalBits is null){
            return TransformPrecisionSourceAnalysis.NotEditable(Error!);
        }

        TransformPrecisionValidation Validation=TransformPrecisionPolicy.Validate(TotalValue,FractionalValue);
        if(!Validation.IsValid){
            return TransformPrecisionSourceAnalysis.NotEditable(
                "現在の GameEnginePrecision 定数が無効です: "+Validation.ErrorMessage);
        }

        return TransformPrecisionSourceAnalysis.Editable(
            new TransformPrecisionConfiguration(TotalValue,FractionalValue),TotalBits,FractionalBits);
    }

    internal static TransformPrecisionSourceEditResult Apply(
        SyntaxNode Root,int TotalBits,int FractionalBits){
        TransformPrecisionValidation Validation=TransformPrecisionPolicy.Validate(TotalBits,FractionalBits);
        if(!Validation.IsValid){
            return TransformPrecisionSourceEditResult.Failure(Validation.ErrorMessage!);
        }

        TransformPrecisionSourceAnalysis Analysis=Analyze(Root);
        if(!Analysis.IsEditable
            ||Analysis.TotalBitsVariable is null
            ||Analysis.FractionalBitsVariable is null){
            return TransformPrecisionSourceEditResult.Failure(
                Analysis.ErrorMessage??"GameEnginePrecision を編集できません。");
        }

        VariableDeclaratorSyntax[] Variables=[Analysis.TotalBitsVariable,Analysis.FractionalBitsVariable];
        SyntaxNode Updated=Root.ReplaceNodes(
            Variables,
            (Original,Rewritten)=>string.Equals(Original.Identifier.ValueText,TotalBitsName,StringComparison.Ordinal)
                ?WithIntegerValue(Rewritten,TotalBits)
                :WithIntegerValue(Rewritten,FractionalBits));
        return TransformPrecisionSourceEditResult.Success(Updated);
    }

    private static bool TryGetIntConstant(
        ClassDeclarationSyntax ClassDeclaration,string Name,
        out VariableDeclaratorSyntax?Variable,out int Value,out string?ErrorMessage){
        Variable=null;
        Value=0;
        ErrorMessage=null;
        VariableDeclaratorSyntax[] Matches=ClassDeclaration.Members
            .OfType<FieldDeclarationSyntax>()
            .Where(Field=>Field.Modifiers.Any(Modifier=>Modifier.IsKind(SyntaxKind.ConstKeyword)))
            .Where(Field=>Field.Declaration.Type is PredefinedTypeSyntax Type
                &&Type.Keyword.IsKind(SyntaxKind.IntKeyword))
            .SelectMany(Field=>Field.Declaration.Variables)
            .Where(Candidate=>string.Equals(Candidate.Identifier.ValueText,Name,StringComparison.Ordinal))
            .ToArray();
        if(Matches.Length!=1){
            ErrorMessage=string.Format(
                CultureInfo.InvariantCulture,
                "GameEnginePrecision.{0} は int const として一意に宣言されている必要があります。",
                Name);
            return false;
        }

        if(Matches[0].Initializer?.Value is not LiteralExpressionSyntax Literal
            ||!Literal.IsKind(SyntaxKind.NumericLiteralExpression)
            ||Literal.Token.Value is not int IntegerValue){
            ErrorMessage=string.Format(
                CultureInfo.InvariantCulture,
                "GameEnginePrecision.{0} は整数リテラルで指定してください。",
                Name);
            return false;
        }

        Variable=Matches[0];
        Value=IntegerValue;
        return true;
    }

    private static VariableDeclaratorSyntax WithIntegerValue(VariableDeclaratorSyntax Variable,int Value){
        ExpressionSyntax Existing=Variable.Initializer!.Value;
        ExpressionSyntax Literal=SyntaxFactory.LiteralExpression(
            SyntaxKind.NumericLiteralExpression,SyntaxFactory.Literal(Value)).WithTriviaFrom(Existing);
        return Variable.WithInitializer(Variable.Initializer.WithValue(Literal));
    }
}
