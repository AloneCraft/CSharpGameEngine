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

    /// <summary>保存時にC#ソースへ書き込むobject creation式。</summary>
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

        var Defaults=ReadParameterlessConstructorDefaults(Type);
        StringBuilder Builder=new();
        var MemberBuilder=ImmutableArray.CreateBuilder<SceneComponentMember>(Members.Length);
        Builder.Append("new ").Append(FullyQualifiedName).Append(" { ");
        for(var Index=0;Index<Members.Length;Index++){
            if(Index>0){
                Builder.Append(", ");
            }

            var Member=Members[Index];
            var HasDefault=Defaults.TryGetValue(Member.Name,out string?Expression);
            var ValueText=HasDefault?Expression!:"default";
            Builder.Append(Member.Name).Append('=').Append(ValueText);
            MemberBuilder.Add(new SceneComponentMember(
                Member.Name,Member,
                CreateValue(Member,ValueText,HasDefault,Compilation),
                SceneSourceLocation.None));
        }

        Builder.Append(" }");
        return new SceneComponentInitializerPlan(Builder.ToString(),MemberBuilder.MoveToImmutable());
    }

    /// <summary>
    /// 再解析後に<see cref="SceneValueReader"/>が同じ式から作るであろう<see cref="SceneValue"/>を、
    /// 先回りして組み立てる。<b>種別を揃えるのが要点</b> — <see cref="SceneValue.IsEditable"/>は
    /// Literal/EnumMember/Defaultだけを真とするので、ここを外すとInspectorが読み取り専用になる。
    /// </summary>
    private static SceneValue CreateValue(
        ISymbol Member,string ValueText,bool HasDefault,Compilation?Compilation){
        var MemberType=Member switch {
            IFieldSymbol Field=>Field.Type,
            IPropertySymbol Property=>Property.Type,
            _=>null,
        };
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
    /// パラメータなしコンストラクタが設定している既定値を<c>メンバー名 → 式テキスト</c>で返す。
    /// 受理するのは<c>this.X=&lt;式&gt;;</c>だけが並ぶ本体に限る — 条件分岐やメソッド呼び出しが
    /// 混ざっていたら意味を再現できないので、<b>1件も返さず</b>呼び出し側を縮退させる。
    /// </summary>
    private static IReadOnlyDictionary<string,string>ReadParameterlessConstructorDefaults(INamedTypeSymbol Type){
        Dictionary<string,string>Empty=[];
        var Constructor=Type.InstanceConstructors.FirstOrDefault(static Candidate=>Candidate.Parameters.Length==0);
        if(Constructor is null||Constructor.IsImplicitlyDeclared){
            return Empty;
        }

        var Reference=Constructor.DeclaringSyntaxReferences.FirstOrDefault();
        if(Reference?.GetSyntax()is not ConstructorDeclarationSyntax Declaration){
            return Empty;
        }

        var Statements=Declaration.Body?.Statements;
        if(Statements is null||Declaration.Initializer is not null){
            return Empty;
        }

        Dictionary<string,string>Defaults=[];
        foreach(var Statement in Statements){
            if(Statement is not ExpressionStatementSyntax { Expression:AssignmentExpressionSyntax Assignment }
                ||!Assignment.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SimpleAssignmentExpression)
                ||TryGetAssignedMemberName(Assignment.Left)is not { }Name){
                // 単純代入の並びとして読めない。既定値を取り違えるくらいなら何も返さない。
                return Empty;
            }

            Defaults[Name]=Assignment.Right.ToString();
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
