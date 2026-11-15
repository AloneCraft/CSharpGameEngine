using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Componentパレットが書き込むobject creation式を組み立てる。
///
/// <para>
/// <b>解決する不具合</b>: 以前は常に<c>new T()</c>を書いていたため、<c>SceneComponent.Members</c>が
/// 「ソースに実際に書かれた代入」しか列挙しない仕様と噛み合わず、追加したComponentはInspectorで
/// 永久に「解析待ち…」のまま値を1つも編集できなかった。書き込み可能メンバーを明示的に列挙して
/// <c>new T { A=default, B=default }</c>を書けば、追加した直後から全項目が編集面に並ぶ。
/// </para>
///
/// <para>
/// <b>コンストラクタ既定値を潰さないのが最重要</b>: <c>Transform</c>はパラメータなしコンストラクタで
/// <c>RotationW=1f,ScaleX/Y/Z=1f</c>を設定しており(「<c>new Transform()</c>がUnityと同じidentity
/// transformになるための既定値」)、素朴に全フィールドへ<c>default</c>を撒くと初期化子がこれを
/// 上書きして回転・スケールが0になる。よってコンストラクタ本体の<c>this.X=&lt;定数&gt;;</c>の並びを
/// 構文で読み、そのメンバーだけはコンストラクタの式をそのまま書く。単純代入の並びとして読めない
/// コンストラクタは解釈せず<c>new T()</c>へ安全に縮退する(既定値を壊すくらいなら何も書かない)。
/// </para>
/// </summary>
/// <summary>
/// パレットがComponentを1つ追加するときに要る2つの成果物。<b>必ず同じ列挙から作る</b>ので、
/// ソースへ書く式とメモリ内Componentのメンバーが食い違うことがない。
/// </summary>
internal sealed class SceneComponentInitializerPlan {
    internal SceneComponentInitializerPlan(string Expression,ImmutableArray<SceneComponentMember>Members){
        this.Expression=Expression;
        this.Members=Members;
    }

    /// <summary>追加するComponentのobject creation式(モデル上の式。シーン JSON へは<see cref="Members"/>から書く)。</summary>
    internal string Expression { get; }

    /// <summary>
    /// メモリ内<see cref="SceneComponent"/>へ載せるメンバー。これを空にすると、Roslynの再解析が
    /// 着地するまでInspectorが「解析待ち…」のまま何も編集できない。
    /// </summary>
    internal ImmutableArray<SceneComponentMember>Members { get; }
}

internal static class SceneComponentInitializerPolicy {
    /// <summary>
    /// <paramref name="Type"/>を「全ての書き込み可能メンバーが明示された」object creation式にする。
    /// 書き込み可能メンバーが1つも無ければ<c>new T()</c>のまま(マーカー型など)。
    /// </summary>
    internal static string CreateExpression(INamedTypeSymbol Type,string FullyQualifiedName)=>
        CreatePlan(Type,FullyQualifiedName,Compilation:null).Expression;

    /// <summary>
    /// 書き込む式と、メモリ内Componentへ載せるメンバーを<b>同じ列挙から</b>組み立てる。
    ///
    /// <para>
    /// <paramref name="Compilation"/>はコンストラクタ既定値の定数畳み込み(<c>bool</c>/<c>string</c>の
    /// <see cref="SceneValue.ConstantValue"/>)にだけ使う。渡さなくても式とメンバーは作れるが、
    /// チェックボックスやテキスト欄の初期表示が既定値に落ちる。
    /// </para>
    /// </summary>
    internal static SceneComponentInitializerPlan CreatePlan(
        INamedTypeSymbol Type,string FullyQualifiedName,Compilation?Compilation){
        var Members=EnumerateWritableMembers(Type).ToArray();
        if(Members.Length==0){
            return new SceneComponentInitializerPlan(
                "new "+FullyQualifiedName+"()",ImmutableArray<SceneComponentMember>.Empty);
        }

        if(TryCreateRufTransformPlan(Members,FullyQualifiedName,out SceneComponentInitializerPlan RufTransformPlan)){
            return RufTransformPlan;
        }

        var Defaults=ReadParameterlessConstructorDefaults(Type);
        List<string>Assignments=[];
        var MemberBuilder=ImmutableArray.CreateBuilder<SceneComponentMember>(Members.Length);
        foreach(var Member in Members){
            var HasDefault=Defaults.TryGetValue(Member.Name,out ExpressionSyntax?Initializer);
            var ValueText=HasDefault?Initializer!.ToString():"default";
            SceneValue Value;
            if(HasDefault&&TryResolveDefault(Member,Initializer!,Compilation,out var Resolved)){
                // シーンのドキュメントで保持できない既定値（static member や factory call など）は除外し、
                // シーンをビルドしたときに同じ宣言済み既定値が使われるようにします。
                if(Resolved is null){ continue; }
                Value=Resolved;
            } else {
                Value=CreateValue(Member,ValueText,HasDefault,Compilation);
            }
            Assignments.Add(Member.Name+"="+ValueText);
            MemberBuilder.Add(new SceneComponentMember(Member.Name,Member,Value,SceneSourceLocation.None));
        }

        var Expression=Assignments.Count==0
            ?"new "+FullyQualifiedName+"()"
            :"new "+FullyQualifiedName+" { "+string.Join(", ",Assignments)+" }";
        return new SceneComponentInitializerPlan(Expression,MemberBuilder.ToImmutable());
    }

    /// <summary>
    /// 宣言の semantic model を使って、宣言された既定値を読み取ります。名前で指定された enum メンバー（値で照合するため cast でも指定可能）、定数（<c>6360f</c>、<c>MathF.PI/4f</c>）、または <c>LogicalScalar.FromSingle(constant)</c>（シーン JSON は LogicalScalar を float 経由で読み取る）です。既定値がこれらの形式でない場合、<paramref name="Value"/> は null となり、シーンでは保持できません。宣言が <paramref name="Compilation"/> 内にない場合（参照アセンブリ、またはコンパイルなし）は false を返し、その場合はテキストのみの plan を使います。
    /// </summary>
    private static bool TryResolveDefault(ISymbol Member,ExpressionSyntax Initializer,Compilation?Compilation,out SceneValue?Value){
        Value=null;
        if(Compilation is null||!Compilation.ContainsSyntaxTree(Initializer.SyntaxTree)){
            return false;
        }
        var MemberType=Member switch {
            IFieldSymbol Field=>Field.Type,
            IPropertySymbol Property=>Property.Type,
            _=>null,
        };
        var Text=Initializer.ToString();
        if(LightValueContract.Find(MemberType) is not null){
            var Physical=CreateValue(Member,Text,true,Compilation);
            Value=Physical.Kind==SceneValueKind.ObjectCreation?Physical:null;
            return true;
        }
        var Model=Compilation.GetSemanticModel(Initializer.SyntaxTree);
        if(Model.GetSymbolInfo(Initializer).Symbol is IFieldSymbol { HasConstantValue:true,ContainingType.TypeKind:TypeKind.Enum }EnumField){
            Value=new SceneValue(SceneValueKind.EnumMember,MemberType,EnumField.ConstantValue,EnumField,
                ImmutableArray<SceneComponentMember>.Empty,ImmutableArray<SceneValue>.Empty,ImmutableArray<SceneValue>.Empty,
                Text,Text,SceneSourceLocation.None);
            return true;
        }
        var Constant=Model.GetConstantValue(Initializer);
        if(MemberType is INamedTypeSymbol { TypeKind:TypeKind.Enum }Enum){
            // シーン JSON は1つのメンバーを名前で指定します（(ShapeKind)1 は "Sphere"）。flags の組み合わせなど、
            // 1つのメンバーで表せない値は JSON 形式がないため除外します。
            var Named=Constant.HasValue
                ?Enum.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(Field=>Field.HasConstantValue&&Equals(Field.ConstantValue,Constant.Value))
                :null;
            if(Named is not null){
                var Name=SceneTypePredicates.ToFullyQualifiedName(Enum)+"."+Named.Name;
                Value=new SceneValue(SceneValueKind.EnumMember,MemberType,Named.ConstantValue,Named,
                    ImmutableArray<SceneComponentMember>.Empty,ImmutableArray<SceneValue>.Empty,ImmutableArray<SceneValue>.Empty,
                    Name,Name,SceneSourceLocation.None);
            }
            return true;
        }
        if(Constant.HasValue&&(Constant.Value is not null||MemberType is not { IsValueType:true })){
            // char や NaN など、定数のシーン JSON で保持できない値は、保持できない既定値と同様に除外します。
            if(CSharpGameEngine.SceneAnalysis.Json.SceneJsonConverter.HasJsonForm(Constant.Value)){ Value=Literal(MemberType,Constant.Value,Text); }
            return true;
        }
        if(Initializer is InvocationExpressionSyntax { ArgumentList.Arguments.Count:1 }Invocation
            &&Model.GetSymbolInfo(Invocation).Symbol is IMethodSymbol { IsStatic:true,Name:"FromSingle" }Factory
            &&SceneTypePredicates.ToFullyQualifiedName(Factory.ContainingType)=="global::CSharpGameEngine.Numerics.LogicalScalar"
            &&Model.GetConstantValue(Invocation.ArgumentList.Arguments[0].Expression) is { HasValue:true,Value:float Single }){
            Value=Literal(MemberType,Single,SceneEditValues.SingleLiteral(Single));
            return true;
        }
        return true;
    }

    private static SceneValue Literal(ITypeSymbol?Type,object?ConstantValue,string Text)=>
        new(SceneValueKind.Literal,Type,ConstantValue,ReferencedSymbol:null,
            ImmutableArray<SceneComponentMember>.Empty,ImmutableArray<SceneValue>.Empty,ImmutableArray<SceneValue>.Empty,
            Text,Text,SceneSourceLocation.None);

    /// <summary>
    /// 再解析後に<see cref="SceneValueReader"/>が同じ式から作るであろう<see cref="SceneValue"/>を、
    /// 先回りして組み立てる。<b>種別を揃えるのが要点</b> — <see cref="SceneValue.IsEditable"/>は
    /// Literal/EnumMember/Defaultだけを真とするので、ここを外すとInspectorが読み取り専用になる。
    /// </summary>
    internal static SceneValue CreateValue(
        ISymbol Member,string ValueText,bool HasDefault,Compilation?Compilation){
        var MemberType=Member switch {
            IFieldSymbol Field=>Field.Type,
            IPropertySymbol Property=>Property.Type,
            _=>null,
        };
        var Physical=LightValueContract.Find(MemberType);
        if(Physical is not null&&Physical.TryReadExpression(ValueText,out var PhysicalValues)){
            var Canonical=Physical.Format(PhysicalValues);
            var Arguments=PhysicalValues.Select(Value=>new SceneValue(
                SceneValueKind.Literal,Compilation?.GetSpecialType(SpecialType.System_Single),Value,null,
                ImmutableArray<SceneComponentMember>.Empty,ImmutableArray<SceneValue>.Empty,ImmutableArray<SceneValue>.Empty,
                SceneEditValues.SingleLiteral(Value),SceneEditValues.SingleLiteral(Value),SceneSourceLocation.None)).ToImmutableArray();
            var Constructor=(MemberType as INamedTypeSymbol)?.InstanceConstructors.FirstOrDefault(Physical.MatchesConstructor);
            return new SceneValue(SceneValueKind.ObjectCreation,MemberType,null,Constructor,
                ImmutableArray<SceneComponentMember>.Empty,Arguments,ImmutableArray<SceneValue>.Empty,
                Canonical,Canonical,SceneSourceLocation.None);
        }
        var Kind=!HasDefault
                     ?SceneValueKind.Default
                     :MemberType is { TypeKind:TypeKind.Enum }
                         ?SceneValueKind.EnumMember
                         :SceneValueKind.Literal;
        return new SceneValue(
            Kind,
            MemberType,
            Kind==SceneValueKind.Default?null:TryFoldConstant(Member,ValueText,Compilation),
            ReferencedSymbol:null,
            ImmutableArray<SceneComponentMember>.Empty,
            ImmutableArray<SceneValue>.Empty,
            ImmutableArray<SceneValue>.Empty,
            EmitExpressionText:ValueText,
            SourceExpressionText:ValueText,
            SceneSourceLocation.None);
    }

    /// <summary>
    /// Transform が共通の3引数 R/U/F ベクトル型を公開する場合、Position、Rotation、Scale の各 R/U/F ベクトルを明示的に作成します。これにより、生成直後からコンポーネントを編集できます。
    /// </summary>
    private static bool TryCreateRufTransformPlan(
        IReadOnlyList<ISymbol> Members,string FullyQualifiedName,out SceneComponentInitializerPlan Plan){
        Plan=null!;
        string[]Names=["Position","Rotation","Scale"];
        ISymbol?[]TransformMembers=Names.Select(Name=>Members.FirstOrDefault(Member=>
            string.Equals(Member.Name,Name,System.StringComparison.Ordinal))).ToArray();
        if(TransformMembers.Any(static Member=>Member is null)
            ||GetMemberType(TransformMembers[0]!) is not INamedTypeSymbol VectorType
            ||!TransformMembers.All(Member=>SymbolEqualityComparer.Default.Equals(GetMemberType(Member!),VectorType))
            ||!HasRufConstructor(VectorType,out IMethodSymbol? Constructor)){
            return false;
        }

        int[][]Values=[[0,0,0],[0,0,0],[1,1,1]];
        StringBuilder Builder=new();
        var MemberBuilder=ImmutableArray.CreateBuilder<SceneComponentMember>(Names.Length);
        Builder.Append("new ").Append(FullyQualifiedName).Append(" { ");
        for(var Index=0;Index<Names.Length;Index++){
            if(Index>0){
                Builder.Append(", ");
            }

            SceneValue Value=CreateRufVectorValue(VectorType,Constructor!,Values[Index]);
            Builder.Append(Names[Index]).Append('=').Append(Value.EmitExpressionText);
            MemberBuilder.Add(new SceneComponentMember(
                Names[Index],TransformMembers[Index],Value,SceneSourceLocation.None));
        }

        Builder.Append(" }");
        Plan=new SceneComponentInitializerPlan(Builder.ToString(),MemberBuilder.MoveToImmutable());
        return true;
    }

    private static ITypeSymbol?GetMemberType(ISymbol Member)=>Member switch {
        IFieldSymbol Field=>Field.Type,
        IPropertySymbol Property=>Property.Type,
        _=>null,
    };

    private static bool HasRufConstructor(INamedTypeSymbol VectorType,out IMethodSymbol? Constructor){
        Constructor=VectorType.InstanceConstructors.FirstOrDefault(Candidate=>
            Candidate.Parameters.Length==3
            &&string.Equals(Candidate.Parameters[0].Name,"R",System.StringComparison.Ordinal)
            &&string.Equals(Candidate.Parameters[1].Name,"U",System.StringComparison.Ordinal)
            &&string.Equals(Candidate.Parameters[2].Name,"F",System.StringComparison.Ordinal));
        return Constructor is not null;
    }

    private static SceneValue CreateRufVectorValue(
        INamedTypeSymbol VectorType,IMethodSymbol Constructor,IReadOnlyList<int> Values){
        ImmutableArray<SceneValue>.Builder Arguments=ImmutableArray.CreateBuilder<SceneValue>(3);
        for(var Index=0;Index<3;Index++){
            string Text=Values[Index].ToString(System.Globalization.CultureInfo.InvariantCulture);
            Arguments.Add(new SceneValue(
                SceneValueKind.Literal,Constructor.Parameters[Index].Type,Values[Index],ReferencedSymbol:null,
                ImmutableArray<SceneComponentMember>.Empty,ImmutableArray<SceneValue>.Empty,
                ImmutableArray<SceneValue>.Empty,Text,Text,SceneSourceLocation.None));
        }

        string TypeName=VectorType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        string Expression="new "+TypeName+"("+string.Join(", ",Arguments.Select(Argument=>Argument.EmitExpressionText))+")";
        return new SceneValue(
            SceneValueKind.ObjectCreation,VectorType,ConstantValue:null,ReferencedSymbol:null,
            ImmutableArray<SceneComponentMember>.Empty,Arguments.MoveToImmutable(),
            ImmutableArray<SceneValue>.Empty,Expression,Expression,SceneSourceLocation.None);
    }
    /// <summary>
    /// コンストラクタが書いた式の定数値。<c>bool</c>のチェック状態と<c>string</c>の初期表示に要る
    /// (数値とenumは<see cref="SceneValue.SourceExpressionText"/>から描くので無くても困らない)。
    /// 畳めなければ<see langword="null"/>を返して初期表示だけ既定値へ落とす。
    /// </summary>
    private static object?TryFoldConstant(ISymbol Member,string ValueText,Compilation?Compilation){
        if(Compilation is null){
            return null;
        }

        var Reference=Member.ContainingType.InstanceConstructors
                            .FirstOrDefault(static Candidate=>Candidate.Parameters.Length==0)
                            ?.DeclaringSyntaxReferences.FirstOrDefault();
        if(Reference?.GetSyntax()is not ConstructorDeclarationSyntax Declaration
            ||!Compilation.ContainsSyntaxTree(Declaration.SyntaxTree)){
            return null;
        }

        var Model=Compilation.GetSemanticModel(Declaration.SyntaxTree);
        foreach(var Statement in Declaration.Body?.Statements??default){
            if(Statement is ExpressionStatementSyntax { Expression:AssignmentExpressionSyntax Assignment }
                &&string.Equals(Assignment.Right.ToString(),ValueText,System.StringComparison.Ordinal)){
                var Constant=Model.GetConstantValue(Assignment.Right);
                return Constant.HasValue?Constant.Value:null;
            }
        }

        return null;
    }

    /// <summary>
    /// object initializerで代入できるインスタンスメンバーを宣言順で返す。<c>readonly</c>フィールド・
    /// 定数・staticは書けないので除く。プロパティは<c>init</c>アクセサも代入できるので含める。
    /// </summary>
    internal static IEnumerable<ISymbol>EnumerateWritableMembers(INamedTypeSymbol Type){
        foreach(var Member in Type.GetMembers()){
            if(Member.IsStatic||Member.IsImplicitlyDeclared
                ||Member.DeclaredAccessibility!=Accessibility.Public){
                continue;
            }

            switch(Member){
                case IFieldSymbol { IsConst:false,IsReadOnly:false }:
                case IPropertySymbol { SetMethod.DeclaredAccessibility:Accessibility.Public }:
                    yield return Member;
                    break;
            }
        }
    }

    /// <summary>
    /// パラメータなしコンストラクタが設定している既定値を<c>メンバー名 → 式</c>で返す。
    /// 受理するのは<c>this.X=&lt;式&gt;;</c>だけが並ぶ本体に限る — 条件分岐やメソッド呼び出しが
    /// 混ざっていたら意味を再現できないので、<b>1件も返さず</b>呼び出し側を縮退させる。
    /// </summary>
    private static IReadOnlyDictionary<string,ExpressionSyntax>ReadParameterlessConstructorDefaults(INamedTypeSymbol Type){
        Dictionary<string,ExpressionSyntax>Empty=[];
        Dictionary<string,ExpressionSyntax>Defaults=[];
        foreach(var Member in EnumerateWritableMembers(Type)){
            var DeclarationSyntax=Member.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
            var Initializer=DeclarationSyntax switch {
                VariableDeclaratorSyntax Variable=>Variable.Initializer?.Value,
                PropertyDeclarationSyntax Property=>Property.Initializer?.Value,
                _=>null,
            };
            if(Initializer is not null){ Defaults[Member.Name]=Initializer; }
        }
        var Constructor=Type.InstanceConstructors.FirstOrDefault(static Candidate=>Candidate.Parameters.Length==0);
        if(Constructor is null||Constructor.IsImplicitlyDeclared){
            return Defaults;
        }

        var Reference=Constructor.DeclaringSyntaxReferences.FirstOrDefault();
        if(Reference?.GetSyntax()is not ConstructorDeclarationSyntax Declaration){
            return Defaults;
        }

        var Statements=Declaration.Body?.Statements;
        if(Statements is null||Declaration.Initializer is not null){
            return Empty;
        }

        foreach(var Statement in Statements){
            if(Statement is not ExpressionStatementSyntax { Expression:AssignmentExpressionSyntax Assignment }
                ||!Assignment.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SimpleAssignmentExpression)
                ||TryGetAssignedMemberName(Assignment.Left)is not { }Name){
                // 単純代入の並びとして読めない。既定値を取り違えるくらいなら何も返さない。
                return Empty;
            }

            Defaults[Name]=Assignment.Right;
        }

        return Defaults;
    }

    /// <summary><c>this.X</c>と<c>X</c>のどちらの書き方も受ける。</summary>
    private static string?TryGetAssignedMemberName(ExpressionSyntax Left)=>Left switch {
        MemberAccessExpressionSyntax { Expression:ThisExpressionSyntax,Name:IdentifierNameSyntax Identifier }=>
            Identifier.Identifier.ValueText,
        IdentifierNameSyntax Identifier=>Identifier.Identifier.ValueText,
        _=>null,
    };
}
