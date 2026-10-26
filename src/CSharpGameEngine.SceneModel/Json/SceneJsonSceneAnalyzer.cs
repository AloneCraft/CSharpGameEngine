using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
namespace CSharpGameEngine.SceneAnalysis.Json;
/// <summary>Scene ドキュメント内で見つかった問題1つ。テキスト上の文字オフセットで位置を示す。</summary>
internal readonly struct SceneJsonProblem {
    internal SceneJsonProblem(DiagnosticDescriptor Descriptor,int Start,int End,params object[]Arguments){
        this.Descriptor=Descriptor;
        this.Start=Start;
        this.End=End;
        this.Arguments=Arguments;
    }
    internal DiagnosticDescriptor Descriptor { get; }
    internal int Start { get; }
    internal int End { get; }
    internal object[]Arguments { get; }
}
/// <summary>ノードのコンポーネント。<c>global::</c> 形式の型名と、C# で生成する式を持つ。</summary>
internal readonly struct SceneJsonComponent {
    internal SceneJsonComponent(string TypeFullyQualifiedName,string EmitExpressionText){
        this.TypeFullyQualifiedName=TypeFullyQualifiedName;
        this.EmitExpressionText=EmitExpressionText;
    }
    internal string TypeFullyQualifiedName { get; }
    internal string EmitExpressionText { get; }
}
internal sealed class SceneJsonNode {
    internal SceneJsonNode(string?Id,string?Name,ImmutableArray<int>Path,ImmutableArray<SceneJsonComponent>Components,ImmutableArray<SceneJsonNode>Children){
        this.Id=Id;
        this.Name=Name;
        this.Path=Path;
        this.Components=Components;
        this.Children=Children;
    }
    internal string?Id { get; }
    internal string?Name { get; }
    /// <summary><see cref="NodePath"/> と同様に、ルートからの子インデックスを並べる。</summary>
    internal ImmutableArray<int>Path { get; }
    internal ImmutableArray<SceneJsonComponent>Components { get; }
    internal ImmutableArray<SceneJsonNode>Children { get; }
}
/// <summary>現在のスキーマで <see cref="Key"/> が使われている角度キーを、C# 側の名前で表したもの（ADR-0074）。</summary>
internal readonly struct SceneJsonKeyRename {
    internal SceneJsonKeyRename(int Start,int End,string Name,string Key){
        this.Start=Start;
        this.End=End;
        this.Name=Name;
        this.Key=Key;
    }
    /// <summary>角度メンバーが保持する、ドキュメント内の数値テキストの範囲。JSON には C# 側の単位を含めない。</summary>
    internal int Start { get; }
    internal int End { get; }
    internal string Name { get; }
    internal string Key { get; }
}
/// <summary>角度メンバーが持つドキュメント内の数値（ADR-0074）。単位は C# メンバーのもの。</summary>
internal readonly struct SceneJsonAngleValue {
    internal SceneJsonAngleValue(int Start,int End,string Text,SceneAngleUnit Unit){
        this.Start=Start;
        this.End=End;
        this.Text=Text;
        this.Unit=Unit;
    }
    internal int Start { get; }
    internal int End { get; }
    internal string Text { get; }
    internal SceneAngleUnit Unit { get; }
}
internal sealed class SceneJsonScene {
    internal SceneJsonScene(string?Namespace,string ClassName,int?StartupOrder,SceneJsonNode?Root,ImmutableArray<SceneJsonProblem>Problems,
        int SchemaVersion=SceneJsonSceneAnalyzer.SchemaVersion,ImmutableArray<SceneJsonAngleValue>AngleValues=default,
        ImmutableArray<SceneJsonKeyRename>KeyRenames=default){
        this.Namespace=Namespace;
        this.ClassName=ClassName;
        this.StartupOrder=StartupOrder;
        this.Root=Root;
        this.Problems=Problems;
        this.SchemaVersion=SchemaVersion;
        this.AngleValues=AngleValues.IsDefault?ImmutableArray<SceneJsonAngleValue>.Empty:AngleValues;
        this.KeyRenames=KeyRenames.IsDefault?ImmutableArray<SceneJsonKeyRename>.Empty:KeyRenames;
    }
    /// <summary>ドキュメントの <c>schemaVersion</c>。3では C# の単位名を省いた角度キーを使い、2と3では角度を回転数で格納し、1では各メンバー自身の単位を格納する。</summary>
    internal int SchemaVersion { get; }
    /// <summary>現行スキーマで単位なしキーを使う位置にある、C# の名前を使った角度キーすべて。</summary>
    internal ImmutableArray<SceneJsonKeyRename>KeyRenames { get; }
    /// <summary>ドキュメント順に並べた、角度メンバーが持つ数値すべて。</summary>
    internal ImmutableArray<SceneJsonAngleValue>AngleValues { get; }
    internal string?Namespace { get; }
    internal string ClassName { get; }
    internal int?StartupOrder { get; }
    internal SceneJsonNode?Root { get; }
    internal ImmutableArray<SceneJsonProblem>Problems { get; }
    internal bool HasErrors=>!this.Problems.IsEmpty||this.Root is null;
    /// <summary>エディターが JSON ツリーだけから同じ情報を読み取って書き戻せるよう、パスごとに各ノードの ID を記録する。</summary>
    internal Dictionary<NodePath,string>NodeIdsByPath(){
        var Result=new Dictionary<NodePath,string>();
        void Walk(SceneJsonNode Node){
            if(Node.Id is { Length:>0 }Id){ Result[new NodePath(Node.Path)]=Id; }
            foreach(var Child in Node.Children){ Walk(Child); }
        }
        if(this.Root is not null){ Walk(this.Root); }
        return Result;
    }
}
/// <summary>
/// <see cref="Compilation"/> に照らして Scene JSON ドキュメントを読み込む（ADR-0070）。値はメンバーの宣言された C# 型に従う。JSON にタグは付けない：
/// <list type="bullet"><item>列挙型はメンバー名の文字列。bool、string、数値メンバーは JSON リテラルを使い、リテラルの型接尾辞は変更せず出力する。</item>
/// <item>構造体またはクラスの値は、コンストラクターパラメーター名をキーにしたオブジェクトに続けて、設定可能なメンバーを記載する。最も多くのキーを受け取る public コンストラクターを使う。
/// </item><item><c>LogicalScalar</c> のように、<c>float</c> からの暗黙的変換を持つ型には数値を指定できる。</item><item>
/// <c>[Angle]</c> が付いたメンバーまたはコンストラクターパラメーター内の数値は、スキーマバージョン2以降では回転数で記述し、出力するリテラル内ではメンバーの単位に変換する（ADR-0074
/// ）。バージョン1ではメンバーの単位をそのまま使う。キーから C# の単位名を省略することもでき（<see cref="KeyOf"/>）、これはバージョン3で出力する。C# の名前でも引き続き読み込め
/// る。</item></list>拒否した項目はすべて <see cref="SceneJsonProblem"/> に変換し、ドキュメント全体の問題をまとめて報告する。
/// </summary>
internal static class SceneJsonSceneAnalyzer {
    /// <summary>このコードが書き込むバージョン（ADR-0074：単位名のないキーで角度を回転数として保存する）。バージョン1と2も引き続き読み込む。</summary>
    internal const int SchemaVersion=SceneJsonAngles.UnitFreeKeysSchemaVersion;
    /// <summary>更新用に収集した、角度メンバーの数値を読み取ったときの単位（回転数またはメンバー自身の単位）。</summary>
    private sealed class AngleReading {
        internal AngleReading(bool Turns){
            this.Turns=Turns;
        }
        internal bool Turns { get; }
        internal List<SceneJsonAngleValue>Values { get; }=[];
        internal List<SceneJsonKeyRename>KeyRenames { get; }=[];
    }
    private static readonly string[]DocumentKeys=["$schema","schemaVersion","namespace","className","startupOrder","root"];
    private static readonly string[]NodeKeys=["id","name","components","children"];
    private static readonly string[]ComponentKeys=["type","members"];
    internal static SceneJsonScene Analyze(string Text,Compilation Compilation){
        List<SceneJsonProblem>Problems=[];
        SceneJsonValue Document;
        try {
            Document=SceneJsonReader.Parse(Text);
        } catch(SceneJsonException Error){
            Problems.Add(new SceneJsonProblem(SceneJsonDiagnostics.InvalidDocument,Error.Position,Error.Position+1,Error.Message));
            return new SceneJsonScene(null,string.Empty,null,null,Problems.ToImmutableArray());
        }
        if(Document.Kind!=SceneJsonKind.Object){
            Problems.Add(Invalid(Document,"A scene document must be a JSON object."));
            return new SceneJsonScene(null,string.Empty,null,null,Problems.ToImmutableArray());
        }
        RejectUnknownKeys(Document,DocumentKeys,"document",Problems);
        var Version=Document.Get("schemaVersion");
        var DocumentVersion=SchemaVersion;
        if(Version is { Kind:SceneJsonKind.Number,Text:"1" or "2" or "3" }){
            DocumentVersion=int.Parse(Version.Text!,CultureInfo.InvariantCulture);
        } else {
            Problems.Add(Invalid(Version??Document,$"'schemaVersion' must be 1, 2 or {SchemaVersion}."));
        }
        var Angles=new AngleReading(DocumentVersion>=SceneJsonAngles.TurnsSchemaVersion);
        var Namespace=OptionalString(Document,"namespace",Problems);
        if(Namespace is not null&&!Namespace.Split('.').All(IsIdentifier)){
            Problems.Add(Invalid(Document.Get("namespace")!,$"'{Namespace}' is not a namespace name."));
        }
        var ClassName=OptionalString(Document,"className",Problems);
        if(ClassName is null){
            Problems.Add(Invalid(Document,"'className' is required."));
            ClassName=string.Empty;
        } else if(!IsIdentifier(ClassName)){
            Problems.Add(Invalid(Document.Get("className")!,$"'{ClassName}' is not a C# identifier."));
        } else if(SyntaxFacts.GetContextualKeywordKind(ClassName)!=SyntaxKind.None){
            // 文脈依存キーワードと同じ名前のクラスがあると、生成コードが壊れるか意味が変わる。「var」は暗黙的に
            // 型が決まるローカル変数を Scene 型へ変え、「file」と「required」は修飾子として解析され、「scoped」はエラー、「record」は警告になる。
            Problems.Add(Invalid(Document.Get("className")!,$"'{ClassName}' is a C# contextual keyword; give the scene another className."));
        } else if(ClassNameConflict(Namespace,ClassName,Compilation) is { }Conflict){
            Problems.Add(Invalid(Document.Get("className")!,Conflict));
        }
        int?StartupOrder=null;
        if(Document.Get("startupOrder") is { }Order){
            if(Order.Kind==SceneJsonKind.Number&&int.TryParse(Order.Text,NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out var Parsed)){
                StartupOrder=Parsed;
            } else {
                Problems.Add(Invalid(Order,"'startupOrder' must be an integer."));
            }
        }
        SceneJsonNode?Root=null;
        if(Document.Get("root") is { }RootValue){
            Root=AnalyzeNode(RootValue,ImmutableArray<int>.Empty,Compilation,Problems,Angles);
        } else {
            Problems.Add(Invalid(Document,"'root' is required."));
        }
        return new SceneJsonScene(Namespace,ClassName,StartupOrder,Root,Problems.ToImmutableArray(),DocumentVersion,Angles.Values.ToImmutableArray(),
            Angles.KeyRenames.ToImmutableArray());
    }
    internal const string DataAssetAttributeMetadataName="CSharpGameEngine.Authoring.DataAssetAttribute";
    private static readonly string[]DataAssetKeys=["$schema","schemaVersion","type","members"];
    /// <summary>
    /// データアセットドキュメント（<c>*.cgeasset</c>、ADR-0079）を読み込む。<c>[DataAsset]</c> 型の値を1つ、Scene コンポーネントと同じ形式（
    /// <c>{ "schemaVersion": 3, "type": "Full.TypeName", "members": { … } }</c>）で記述し、同じ値規則（角度は回転数）を適用する。型名と
    /// 、値を作る C# 式を返す。問題がある場合は null を返す。
    /// </summary>
    internal static SceneJsonComponent?AnalyzeDataAsset(string Text,Compilation Compilation,out ImmutableArray<SceneJsonProblem>Problems){
        List<SceneJsonProblem>Found=[];
        SceneJsonComponent?Result=null;
        try {
            var Document=SceneJsonReader.Parse(Text);
            Result=AnalyzeDataAssetDocument(Document,Compilation,Found);
        } catch(SceneJsonException Error){
            Found.Add(new SceneJsonProblem(SceneJsonDiagnostics.InvalidDocument,Error.Position,Error.Position+1,Error.Message));
        }
        Problems=Found.ToImmutableArray();
        return Problems.IsEmpty?Result:null;
    }
    private static SceneJsonComponent?AnalyzeDataAssetDocument(SceneJsonValue Document,Compilation Compilation,List<SceneJsonProblem>Problems){
        if(Document.Kind!=SceneJsonKind.Object){
            Problems.Add(Invalid(Document,"A data asset must be a JSON object."));
            return null;
        }
        RejectUnknownKeys(Document,DataAssetKeys,"data asset",Problems);
        var Version=Document.Get("schemaVersion");
        if(Version is not { Kind:SceneJsonKind.Number,Text:"3" }){
            Problems.Add(Invalid(Version??Document,$"'schemaVersion' must be {SchemaVersion}."));
        }
        var TypeValue=Document.Get("type");
        if(TypeValue is null||TypeValue.Kind!=SceneJsonKind.String||string.IsNullOrWhiteSpace(TypeValue.Text)){
            Problems.Add(Invalid(TypeValue??Document,"A data asset needs a 'type' string with the full name of a [DataAsset] type."));
            return null;
        }
        var TypeName=TypeValue.Text!;
        var Type=Compilation.GetTypeByMetadataName(TypeName)??ResolveNestedType(TypeName,Compilation);
        if(Type is null||Type.IsGenericType||Type.TypeKind is not (TypeKind.Struct or TypeKind.Class)
            ||!Type.GetAttributes().Any(static Attribute=>Attribute.AttributeClass?.ToDisplayString()==DataAssetAttributeMetadataName)){
            Problems.Add(new SceneJsonProblem(SceneJsonDiagnostics.UnknownComponentType,TypeValue.Start,TypeValue.End,TypeName,
                Type is null?"no type with this full name is visible to the project":"the type is not a non-generic struct or class marked [DataAsset]"));
            return null;
        }
        var Members=Document.Get("members");
        if(Members is not null&&Members.Kind!=SceneJsonKind.Object){
            Problems.Add(Invalid(Members,"'members' must be a JSON object."));
            return null;
        }
        var Expression=ObjectExpression(Type,Members,TypeName,Compilation,Problems,new AngleReading(Turns:true),null);
        return new SceneJsonComponent(SceneTypePredicates.ToFullyQualifiedName(Type),Expression);
    }
    private static SceneJsonNode?AnalyzeNode(SceneJsonValue Node,ImmutableArray<int>Path,Compilation Compilation,List<SceneJsonProblem>Problems,AngleReading Angles){
        if(Node.Kind!=SceneJsonKind.Object){
            Problems.Add(Invalid(Node,"A node must be a JSON object."));
            return null;
        }
        RejectUnknownKeys(Node,NodeKeys,"node",Problems);
        var Id=OptionalString(Node,"id",Problems);
        var Name=OptionalString(Node,"name",Problems);
        var Components=ImmutableArray.CreateBuilder<SceneJsonComponent>();
        if(Node.Get("components") is { }ComponentList){
            if(ComponentList.Kind!=SceneJsonKind.Array){
                Problems.Add(Invalid(ComponentList,"'components' must be an array."));
            } else {
                HashSet<string>Seen=new(StringComparer.Ordinal);
                foreach(var Item in ComponentList.Items){
                    if(AnalyzeComponent(Item,Compilation,Problems,Angles) is not { }Component){ continue; }
                    if(!Seen.Add(Component.TypeFullyQualifiedName)){
                        var Type=Item.Get("type")!;
                        Problems.Add(new SceneJsonProblem(SceneJsonDiagnostics.DuplicateComponent,Type.Start,Type.End,Type.Text!));
                        continue;
                    }
                    Components.Add(Component);
                }
            }
        }
        var Children=ImmutableArray.CreateBuilder<SceneJsonNode>();
        if(Node.Get("children") is { }ChildList){
            if(ChildList.Kind!=SceneJsonKind.Array){
                Problems.Add(Invalid(ChildList,"'children' must be an array."));
            } else {
                for(var Index=0;Index<ChildList.Items.Count;Index++){
                    if(AnalyzeNode(ChildList.Items[Index],Path.Add(Index),Compilation,Problems,Angles) is { }Child){ Children.Add(Child); }
                }
            }
        }
        return new SceneJsonNode(Id,Name,Path,Components.ToImmutable(),Children.ToImmutable());
    }
    private static SceneJsonComponent?AnalyzeComponent(SceneJsonValue Component,Compilation Compilation,List<SceneJsonProblem>Problems,AngleReading Angles){
        if(Component.Kind!=SceneJsonKind.Object){
            Problems.Add(Invalid(Component,"A component must be a JSON object."));
            return null;
        }
        RejectUnknownKeys(Component,ComponentKeys,"component",Problems);
        var TypeValue=Component.Get("type");
        if(TypeValue is null||TypeValue.Kind!=SceneJsonKind.String||string.IsNullOrWhiteSpace(TypeValue.Text)){
            Problems.Add(Invalid(TypeValue??Component,"A component needs a 'type' string with the component's full type name."));
            return null;
        }
        var TypeName=TypeValue.Text!;
        if(!TryResolveComponentType(TypeName,Compilation,out var Type,out var Reason)){
            Problems.Add(new SceneJsonProblem(SceneJsonDiagnostics.UnknownComponentType,TypeValue.Start,TypeValue.End,TypeName,Reason));
            return null;
        }
        var Members=Component.Get("members");
        if(Members is not null&&Members.Kind!=SceneJsonKind.Object){
            Problems.Add(Invalid(Members,"'members' must be a JSON object."));
            return null;
        }
        var ProblemCount=Problems.Count;
        var Expression=ObjectExpression(Type!,Members,TypeName,Compilation,Problems,Angles,null);
        if(Problems.Count!=ProblemCount){ return null; }
        return new SceneJsonComponent(SceneTypePredicates.ToFullyQualifiedName(Type!),Expression);
    }
    /// <summary>
    /// C# と Scene ドキュメントで使う、入れ子型の名前（<c>Ns.Outer.Inner</c>。コンバーター、スキーマ、パレットが出力する名前）。メタデータ名では入れ子型を '+' でつなぐため
    /// 、右側からドットを1つずつ置き換えて解決を試す。以前はメタデータ名だけで解決していたため、入れ子コンポーネントのある Scene は書き戻すたびに編集エラーになっていた。
    /// </summary>
    private static INamedTypeSymbol?ResolveNestedType(string TypeName,Compilation Compilation){
        var Name=TypeName;
        for(var Dot=Name.LastIndexOf('.');Dot>0;Dot=Name.LastIndexOf('.',Dot-1)){
            Name=Name.Substring(0,Dot)+"+"+Name.Substring(Dot+1);
            if(Compilation.GetTypeByMetadataName(Name) is { }Nested){ return Nested; }
        }
        return null;
    }
    /// <summary>コンポーネント型は、非ジェネリックかつ予約型ではない構造体で、実行時コンパニオンではないもの。</summary>
    internal static bool TryResolveComponentType(string TypeName,Compilation Compilation,out INamedTypeSymbol?Type,out string Reason){
        Type=Compilation.GetTypeByMetadataName(TypeName)??ResolveNestedType(TypeName,Compilation);
        if(Type is null){
            Reason="no type with this full name is visible to the project";
            return false;
        }
        if(!SceneTypePredicates.IsNonNullableStruct(Type)){
            Reason="components must be structs";
            return false;
        }
        if(Type.IsGenericType||Type.IsRefLikeType){
            Reason="generic and ref struct types cannot be components";
            return false;
        }
        if(SceneTypePredicates.IsReservedComponent(SceneTypePredicates.ToFullyQualifiedName(Type))){
            Reason="the engine reserves this type";
            return false;
        }
        if(SceneTypePredicates.ImplementsRuntimeComponentInterface(Type,Compilation)){
            Reason="runtime component types are attached automatically; declare the authored component instead";
            return false;
        }
        if(!SceneTypePredicates.IsAddableComponent(Type,Compilation)){
            Reason="this type is not a scene component (it is engine infrastructure, a system, or has required members)";
            return false;
        }
        Reason=string.Empty;
        return true;
    }
    private static string ObjectExpression(INamedTypeSymbol Type,SceneJsonValue?Value,string DisplayName,Compilation Compilation,List<SceneJsonProblem>Problems,
        AngleReading Angles,SceneAngleUnit?Unit){
        var TypeText=SceneTypePredicates.ToFullyQualifiedName(Type);
        var Properties=Value?.Properties??Array.Empty<SceneJsonProperty>();
        var Keys=new HashSet<string>(Properties.Select(static Property=>Property.Name),StringComparer.Ordinal);
        var Constructor=ChooseConstructor(Type,Keys);
        var ProblemsBefore=Problems.Count;
        var Arguments=new List<string>();
        var Consumed=new HashSet<string>(StringComparer.Ordinal);
        // 同じ角度が C# の名前と単位のないキーの両方で指定された場合は、両方の値を診断して、先に現れた値を設定する。
        void ReportSameTarget(string Earlier,SceneJsonProperty Later,string Target)=>
            Problems.Add(new SceneJsonProblem(SceneJsonDiagnostics.InvalidDocument,Later.NameStart,Later.NameStart+Later.Name.Length+2,
                $"'{Earlier}' and '{Later.Name}' name the same member '{Target}' of {TypeText.Replace("global::",string.Empty)}; keep one."));
        void RecordKey(SceneJsonProperty Property,string Key){
            if(Property.Name!=Key){
                Angles.KeyRenames.Add(new SceneJsonKeyRename(Property.NameStart,Property.NameStart+Property.Name.Length+2,Property.Name,Key));
            }
        }
        if(Constructor is not null){
            foreach(var Parameter in Constructor.Parameters){
                var Key=KeyOf(Parameter);
                var Property=Properties.FirstOrDefault(Candidate=>Candidate.Name==Parameter.Name||Candidate.Name==Key);
                if(Property is null){ continue; }
                if(Properties.FirstOrDefault(Candidate=>Candidate!=Property&&(Candidate.Name==Parameter.Name||Candidate.Name==Key)) is { }Other){
                    Consumed.Add(Other.Name);
                    ReportSameTarget(Property.Name,Other,Parameter.Name);
                }
                Consumed.Add(Property.Name);
                RecordKey(Property,Key);
                Arguments.Add(Identifier(Parameter.Name)+": "+ValueExpression(Parameter.Type,Property.Value,DisplayName+"."+Property.Name,Compilation,Problems,
                    Angles,Unit??AngleUnitOf(Parameter)));
            }
            // 物理ライトの値は Scene のインスタンス化時にコンストラクターが範囲外の値を拒否するため、
            // エディターのモデルを扱う Scene DSL アナライザーと同様に、ビルド時にここで報告する。
            if(Value is not null&&LightValueContract.Find(Type) is { }Physical&&Physical.MatchesConstructor(Constructor)){
                var Numbers=new float[Constructor.Parameters.Length];
                var Complete=true;
                for(var Index=0;Index<Numbers.Length&&Complete;Index++){
                    var Parameter=Constructor.Parameters[Index];
                    var Key=KeyOf(Parameter);
                    var Property=Properties.FirstOrDefault(Candidate=>Candidate.Name==Parameter.Name||Candidate.Name==Key);
                    if(Property is not null&&NumberIn(Property.Value,Angles,Unit??AngleUnitOf(Parameter)) is { }Number){
                        Numbers[Index]=Number;
                    } else {
                        Complete=false;
                    }
                }
                if(Complete&&!Physical.Validate(Numbers)){
                    Problems.Add(new SceneJsonProblem(SceneJsonDiagnostics.ValueMismatch,Value.Start,Value.End,DisplayName,Type.ToDisplayString(),
                        $"a {Physical.Name} needs {Physical.Requirement}"));
                }
            }
        }
        var Assignments=new List<string>();
        var IncompleteConstructorReported=false;
        var SeenMembers=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var Property in Properties){
            if(Consumed.Contains(Property.Name)){ continue; }
            var MemberSymbol=FindSettableMemberSymbol(Type,Property.Name)??FindSettableAngleByKey(Type,Property.Name);
            if(TypeOf(MemberSymbol) is not { }MemberType){
                // 使用可能なコンストラクターが受け取らないコンストラクターパラメーター名があると、他の
                // 必須引数も不足している。「そのようなメンバーはない」ではなく、不足している引数を一度示す。
                if(IncompleteConstructorFor(Type,Keys,Property.Name) is { }Incomplete){
                    if(!IncompleteConstructorReported&&Value is not null){
                        IncompleteConstructorReported=true;
                        var Missing=Incomplete.Parameters.Where(Parameter=>!NamesParameter(Keys,Parameter)&&!Parameter.HasExplicitDefaultValue).Select(static Parameter=>"'"+KeyOf(Parameter)+"'");
                        var Signature=Type.Name+"("+string.Join(", ",Incomplete.Parameters.Select(static Parameter=>Parameter.Name))+")";
                        Problems.Add(new SceneJsonProblem(SceneJsonDiagnostics.ValueMismatch,Value.Start,Value.End,DisplayName,Type.ToDisplayString(),
                            $"the constructor {Signature} also needs {string.Join(", ",Missing)}"));
                    }
                    continue;
                }
                var NameStart=Property.NameStart;
                Problems.Add(new SceneJsonProblem(SceneJsonDiagnostics.UnknownMember,NameStart,NameStart+Property.Name.Length+2,SceneTypePredicates.ToFullyQualifiedName(Type).Replace("global::",string.Empty),Property.Name));
                continue;
            }
            if(SeenMembers.TryGetValue(MemberSymbol!.Name,out var Earlier)){
                ReportSameTarget(Earlier,Property,MemberSymbol.Name);
                continue;
            }
            if(IsObsoleteError(MemberSymbol)){
                Problems.Add(new SceneJsonProblem(SceneJsonDiagnostics.ValueMismatch,Property.Value.Start,Property.Value.End,DisplayName+"."+Property.Name,
                    MemberType.ToDisplayString(),$"'{MemberSymbol.Name}' is obsolete and cannot be set"));
                continue;
            }
            SeenMembers.Add(MemberSymbol.Name,Property.Name);
            RecordKey(Property,KeyOf(MemberSymbol));
            Assignments.Add(Identifier(MemberSymbol.Name)+" = "+ValueExpression(MemberType,Property.Value,DisplayName+"."+Property.Name,Compilation,Problems,
                Angles,Unit??AngleUnitOf(MemberSymbol)));
        }
        // new T(...) { ... } は必ずコンパイルできなければならない。エラーを生成ファイルへ持ち込まず、他に報告済みの問題がない場合にのみ診断する。
        if(Value is not null&&Problems.Count==ProblemsBefore&&CreationProblem(Type,Constructor,SeenMembers) is { }Reason){
            Problems.Add(new SceneJsonProblem(SceneJsonDiagnostics.ValueMismatch,Value.Start,Value.End,DisplayName,Type.ToDisplayString(),Reason));
        }
        var Builder=new StringBuilder("new ").Append(TypeText).Append('(').Append(string.Join(", ",Arguments)).Append(')');
        if(Assignments.Count>0){ Builder.Append(" { ").Append(string.Join(", ",Assignments)).Append(" }"); }
        return Builder.ToString();
    }
    /// <summary>
    /// <c>new T(...) { ... }</c> がコンパイルできない理由、または理由がないことを示す。エラーとして扱う obsolete 型、コンストラクターを選択していない場合に引数なしで呼べる
    /// コンストラクターがない（CS7036、CS0122）、または初期化子で必須メンバーを設定していない（CS9035）。
    /// </summary>
    private static string?CreationProblem(INamedTypeSymbol Type,IMethodSymbol?Constructor,Dictionary<string,string>SetMembers){
        if(IsObsoleteError(Type)){ return "the type is obsolete and cannot be created"; }
        if(Constructor is null&&!Type.InstanceConstructors.Any(static Candidate=>Candidate.DeclaredAccessibility==Accessibility.Public
            &&IsCallableWithValues(Candidate)&&Candidate.Parameters.All(static Parameter=>Parameter.IsOptional))){
            return "the type has no public constructor without parameters; name the parameters of one of its public constructors";
        }
        if(Constructor?.GetAttributes().Any(static Attribute=>
            Attribute.AttributeClass?.ToDisplayString()=="System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute")==true){
            return null;
        }
        for(ITypeSymbol?Current=Type;Current is not null;Current=Current.BaseType){
            foreach(var Member in Current.GetMembers()){
                if(Member is IFieldSymbol{ IsRequired:true } or IPropertySymbol{ IsRequired:true }&&!SetMembers.ContainsKey(Member.Name)){
                    return $"the required member '{KeyOf(Member)}' needs a value";
                }
            }
        }
        return null;
    }
    private static bool IsCallableWithValues(IMethodSymbol Constructor)=>
        !IsObsoleteError(Constructor)&&Constructor.Parameters.All(static Parameter=>Parameter.RefKind is RefKind.None or RefKind.In);
    /// <summary>シンボルの使用がコンパイルエラーになるか（<c>[Obsolete(message, true)]</c>）。</summary>
    private static bool IsObsoleteError(ISymbol Symbol)=>Symbol.GetAttributes().Any(static Attribute=>
        Attribute.AttributeClass?.ToDisplayString()=="System.ObsoleteAttribute"&&Attribute.ConstructorArguments.Length==2
        &&Attribute.ConstructorArguments[1].Value is true);
    /// <summary>すべてのパラメーターに対応するキーがあり、既定値のあるパラメーターは省略可能な public コンストラクターのうち、最も多くのキーを受け取るもの。引数なしのコンストラクター（構造体に暗黙に用意されるものを含む）はキーを受け取らない。</summary>
    private static IMethodSymbol?ChooseConstructor(INamedTypeSymbol Type,HashSet<string>Keys){
        IMethodSymbol?Best=null;
        var BestCount=-1;
        foreach(var Candidate in Type.InstanceConstructors){
            // ref または out パラメーターには、名前付き値引数を渡せない（CS1620）。エラーとして扱う obsolete メンバーも呼び出せない。
            if(Candidate.DeclaredAccessibility!=Accessibility.Public||!IsCallableWithValues(Candidate)){ continue; }
            var Count=0;
            var Usable=true;
            foreach(var Parameter in Candidate.Parameters){
                if(NamesParameter(Keys,Parameter)){
                    Count++;
                } else if(!Parameter.HasExplicitDefaultValue){
                    Usable=false;
                    break;
                }
            }
            if(Usable&&Count>BestCount){
                Best=Candidate;
                BestCount=Count;
            }
        }
        return BestCount>0?Best:null;
    }
    /// <summary><paramref name="Name"/> を含むキーが指定された public コンストラクターのうち、名前が一致するパラメーターを最も多く持つもの。</summary>
    private static IMethodSymbol?IncompleteConstructorFor(INamedTypeSymbol Type,HashSet<string>Keys,string Name)=>
        Type.InstanceConstructors
            .Where(static Candidate=>Candidate.DeclaredAccessibility==Accessibility.Public)
            .Where(Candidate=>Candidate.Parameters.Any(Parameter=>Parameter.Name==Name||KeyOf(Parameter)==Name))
            .OrderByDescending(Candidate=>Candidate.Parameters.Count(Parameter=>NamesParameter(Keys,Parameter)))
            .FirstOrDefault();
    private static bool NamesParameter(HashSet<string>Keys,IParameterSymbol Parameter)=>
        Keys.Contains(Parameter.Name)||Keys.Contains(KeyOf(Parameter));
    /// <summary>
    /// メンバーまたはコンストラクターパラメーターがドキュメントで使うキー。<c>[Angle]</c> の場合は C# の単位名を省いた名前（ADR-0074、スキーマバージョン3）。それ以外は名前をそのま
    /// ま使う。角度メンバーは C# 側の名前でも引き続き読み込む。
    /// </summary>
    internal static string KeyOf(ISymbol Symbol){
        if(AngleUnitOf(Symbol) is null){ return Symbol.Name; }
        var Type=Symbol is IParameterSymbol{ ContainingSymbol:IMethodSymbol Method }?Method.ContainingType:Symbol.ContainingType;
        return SceneJsonAngles.KeyFor(Symbol.Name,Key=>IsNameTaken(Type,Key));
    }
    /// <summary>型に同名のメンバーがあるか、public コンストラクターに同名のパラメーターがあるか。</summary>
    private static bool IsNameTaken(INamedTypeSymbol?Type,string Name){
        for(ITypeSymbol?Current=Type;Current is not null;Current=Current.BaseType){
            if(!Current.GetMembers(Name).IsEmpty){ return true; }
        }
        return Type is not null&&Type.InstanceConstructors.Any(Constructor=>
            Constructor.DeclaredAccessibility==Accessibility.Public&&Constructor.Parameters.Any(Parameter=>Parameter.Name==Name));
    }
    /// <summary>単位なしのキーが <paramref name="Key"/> である、設定可能な角度メンバー。</summary>
    private static ISymbol?FindSettableAngleByKey(INamedTypeSymbol Type,string Key){
        for(ITypeSymbol?Current=Type;Current is not null;Current=Current.BaseType){
            foreach(var Member in Current.GetMembers()){
                if(Member.Name!=Key&&KeyOf(Member)==Key&&FindSettableMemberSymbol(Type,Member.Name) is { }Settable){ return Settable; }
            }
        }
        return null;
    }
    internal static ITypeSymbol?FindSettableMember(INamedTypeSymbol Type,string Name)=>TypeOf(FindSettableMemberSymbol(Type,Name));
    /// <summary>JSON キーが代入する、public インスタンスフィールドまたは設定可能なプロパティ。</summary>
    internal static ISymbol?FindSettableMemberSymbol(INamedTypeSymbol Type,string Name){
        for(ITypeSymbol?Current=Type;Current is not null;Current=Current.BaseType){
            foreach(var Member in Current.GetMembers(Name)){
                if(Member.DeclaredAccessibility!=Accessibility.Public||Member.IsStatic){ continue; }
                if(Member is IFieldSymbol{ IsReadOnly:false,IsConst:false }){ return Member; }
                if(Member is IPropertySymbol{ SetMethod:{ DeclaredAccessibility:Accessibility.Public } }){ return Member; }
            }
        }
        return null;
    }
    private static ITypeSymbol?TypeOf(ISymbol?Member)=>Member switch {
        IFieldSymbol Field=>Field.Type,
        IPropertySymbol Property=>Property.Type,
        _=>null,
    };
    /// <summary>フィールド、プロパティ、パラメーター上の <c>[Angle]</c> 属性が宣言する単位（ADR-0074）。</summary>
    internal static SceneAngleUnit?AngleUnitOf(ISymbol?Symbol){
        if(Symbol is null){ return null; }
        foreach(var Attribute in Symbol.GetAttributes()){
            if(Attribute.AttributeClass?.ToDisplayString()==SceneJsonAngles.AttributeFullName&&Attribute.ConstructorArguments.Length==1){
                return SceneJsonAngles.UnitFromArgument(Attribute.ConstructorArguments[0].Value);
            }
        }
        return null;
    }
    private static string ValueExpression(ITypeSymbol Type,SceneJsonValue Value,string MemberName,Compilation Compilation,List<SceneJsonProblem>Problems,
        AngleReading Angles,SceneAngleUnit?Unit){
        string Mismatch(string Reason){
            Problems.Add(new SceneJsonProblem(SceneJsonDiagnostics.ValueMismatch,Value.Start,Value.End,MemberName,Type.ToDisplayString(),Reason));
            return "default";
        }
        if(Value.Kind==SceneJsonKind.Null){
            // 参照型メンバーの null は null! として出力する。生成ファイルは #nullable enable のため、ゲームが null 不可として宣言した
            // メンバーを null にすると CS8625 警告になり、警告をエラー扱いする場所ではエラーになる。未設定の string や配列に対して
            // パレットが出力する値も同じ null とする。
            if(Type.IsReferenceType){ return "null!"; }
            return Type.OriginalDefinition.SpecialType==SpecialType.System_Nullable_T?"null":Mismatch("null is only valid for reference and nullable types");
        }
        if(Type is INamedTypeSymbol{ OriginalDefinition.SpecialType:SpecialType.System_Nullable_T }Nullable){
            return ValueExpression(Nullable.TypeArguments[0],Value,MemberName,Compilation,Problems,Angles,Unit);
        }
        if(Type.TypeKind==TypeKind.Enum){
            if(Value.Kind!=SceneJsonKind.String){ return Mismatch("an enum value is written as the member name string"); }
            var Member=Type.GetMembers(Value.Text!).OfType<IFieldSymbol>().FirstOrDefault(static Field=>Field.HasConstantValue);
            if(Member is null){ return Mismatch($"'{Value.Text}' is not a member of the enum"); }
            if(IsObsoleteError(Member)){ return Mismatch($"'{Value.Text}' is obsolete and cannot be used"); }
            return SceneTypePredicates.ToFullyQualifiedName(Type)+"."+Identifier(Member.Name);
        }
        // 回転数の角度をメンバーの単位に変換すると、float で表せる値でもその単位では表せないことがある（度数への変換で1e37回転など）。
        // 無限大をリテラルとして書式化すると例外になり、アナライザー全体とジェネレーターが失敗していた（CS8785）。
        if(Unit is not null&&Angles.Turns&&NumberIn(Value,Angles,Unit) is { }Converted&&(float.IsInfinity(Converted)||float.IsNaN(Converted))){
            return Mismatch($"the angle is beyond the range of float in {(Unit==SceneAngleUnit.Radians?"radians":"degrees")}");
        }
        switch(Type.SpecialType){
            case SpecialType.System_Boolean:
                return Value.Kind==SceneJsonKind.True?"true":Value.Kind==SceneJsonKind.False?"false":Mismatch("expected true or false");
            case SpecialType.System_String:
                return Value.Kind==SceneJsonKind.String?SymbolDisplay.FormatLiteral(Value.Text!,quote:true):Mismatch("expected a string");
            case SpecialType.System_Single:return FloatRange(Value,Mismatch)??RealLiteral(AngleText(Value,Angles,Unit),Value,"f",Mismatch);
            case SpecialType.System_Double:return RealLiteral(AngleText(Value,Angles,Unit),Value,"d",Mismatch);
            case SpecialType.System_Decimal:return RealLiteral(AngleText(Value,Angles,Unit),Value,"m",Mismatch);
            case SpecialType.System_SByte:return IntegerLiteral(Value,sbyte.MinValue,(ulong)sbyte.MaxValue,"(sbyte)",Mismatch);
            case SpecialType.System_Byte:return IntegerLiteral(Value,byte.MinValue,byte.MaxValue,"(byte)",Mismatch);
            case SpecialType.System_Int16:return IntegerLiteral(Value,short.MinValue,(ulong)short.MaxValue,"(short)",Mismatch);
            case SpecialType.System_UInt16:return IntegerLiteral(Value,ushort.MinValue,ushort.MaxValue,"(ushort)",Mismatch);
            case SpecialType.System_Int32:return IntegerLiteral(Value,int.MinValue,(ulong)int.MaxValue,string.Empty,Mismatch);
            case SpecialType.System_UInt32:return IntegerLiteral(Value,uint.MinValue,uint.MaxValue,"(uint)",Mismatch);
            case SpecialType.System_Int64:return IntegerLiteral(Value,long.MinValue,(ulong)long.MaxValue,"(long)",Mismatch);
            case SpecialType.System_UInt64:return IntegerLiteral(Value,0,ulong.MaxValue,"(ulong)",Mismatch);
        }
        if(Value.Kind==SceneJsonKind.Number){
            var Single=Compilation.GetSpecialType(SpecialType.System_Single);
            // 出力する各位置（名前付き引数、初期化子、配列またはコレクション要素）は
            // 目標型が決まっているため、キャストなしで暗黙変換が適用される。キャストを付けると
            // エディターがこのテキストに対して実行する Scene DSL アナライザーが値を読み取れなくなる。
            if(!Compilation.ClassifyConversion(Single,Type).IsImplicit){
                return Mismatch("a number needs a numeric member or a type convertible from float");
            }
            if(FloatRange(Value,Mismatch) is { }OutOfFloat){ return OutOfFloat; }
            // Scene のインスタンス化時、LogicalScalar の float からの変換は Q64.64 の範囲を超えると例外をスローする。
            // この値に含まれるのは、float には収まるが実行環境の解析処理で拒否される数値（.NET Framework では、
            // float.MaxValue を少し超え、C# がその値に丸めるような数値）ではない。Q64.64 の範囲を大きく超えている。
            if(SceneTypePredicates.ToFullyQualifiedName(Type)=="global::CSharpGameEngine.Numerics.LogicalScalar"
                &&(NumberIn(Value,Angles,Unit) is not { }Scalar||!(Math.Abs(Scalar)<LogicalScalarLimit))){
                return Mismatch("the number is beyond the Q64.64 range of about ±9.2e18");
            }
            return AngleText(Value,Angles,Unit)+"f";
        }
        if(Value.Kind==SceneJsonKind.Array){
            var Element=Type is IArrayTypeSymbol Array?Array.ElementType:ElementTypeOf(Type,Compilation);
            if(Element is null){ return Mismatch("an array needs an array or collection member"); }
            // 出力する配列生成式またはコレクション式はコンパイル必須。エラーを生成ファイルへ持ち込まない。
            if(Type is IArrayTypeSymbol{ Rank:>1 }){ return Mismatch("a multidimensional array cannot be written in scene JSON; use an array of arrays"); }
            if(Type is not IArrayTypeSymbol&&!IsCollectionExpressionTarget(Type,Element,Compilation)){
                return Mismatch("this collection type cannot be created from a list of elements (it needs a public parameterless constructor and a public Add that takes one element)");
            }
            var Items=Value.Items.Select((Item,Index)=>ValueExpression(Element,Item,MemberName+"["+Index.ToString(CultureInfo.InvariantCulture)+"]",Compilation,Problems,Angles,Unit));
            return Type is IArrayTypeSymbol
                ?"new "+SceneTypePredicates.ToFullyQualifiedName(Element)+"[] { "+string.Join(", ",Items)+" }"
                :"["+string.Join(", ",Items)+"]";
        }
        if(Value.Kind==SceneJsonKind.Object&&Type is INamedTypeSymbol Named&&Named.TypeKind is TypeKind.Struct or TypeKind.Class){
            // オブジェクトは new T(...) { ... } として出力するが、C# ではタプル型にこの構文を使えない。
            if(Named.IsTupleType){ return Mismatch("a tuple cannot be written in scene JSON; use a struct with named members"); }
            if(Named.IsAbstract){ return Mismatch("an abstract type cannot be created; use a concrete type"); }
            return ObjectExpression(Named,Value,MemberName,Compilation,Problems,Angles,Unit);
        }
        return Mismatch("this JSON value cannot be converted to the member's type");
    }
    /// <summary>
    /// 数値のリテラルテキスト。角度メンバー内では記録しておき、回転数形式のドキュメント値はメンバーの単位に変換する（ADR-0074）。どちらのバージョンでも、その float を表す最短テキストを使うため
    /// 、バージョン1のドキュメントと更新後のドキュメントで出力が一致する。
    /// </summary>
    private static string AngleText(SceneJsonValue Value,AngleReading Angles,SceneAngleUnit?Unit){
        if(Unit is not { }AngleUnit||Value.Kind!=SceneJsonKind.Number){ return Value.Text??string.Empty; }
        Angles.Values.Add(new SceneJsonAngleValue(Value.Start,Value.End,Value.Text!,AngleUnit));
        if(!SceneJsonAngles.IsNumber(Value.Text)){ return Value.Text!; }
        return SceneJsonNumbers.FormatSingle(Angles.Turns
            ?SceneJsonAngles.FromTurns(Value.Text!,AngleUnit)
            :float.Parse(Value.Text!,NumberStyles.Float,CultureInfo.InvariantCulture));
    }
    /// <summary>
    /// コレクション式でこの型のインスタンスを作成できるか（C# 12）。対象はリスト系インターフェース、<c>[CollectionBuilder]</c> を持つ型（
    /// <c>ImmutableArray&lt;T&gt;</c>）、または public の引数なしコンストラクターと要素を1つ取る public <c>Add</c> を持つ具象型（
    /// <c>List&lt;T&gt;</c>、<c>HashSet&lt;T&gt;</c>）。<c>Queue&lt;T&gt;</c>、<c>Stack&lt;T&gt;</c>、
    /// <c>Dictionary&lt;K,V&gt;</c> は対象外。
    /// </summary>
    internal static bool IsCollectionExpressionTarget(ITypeSymbol Type,ITypeSymbol Element,Compilation Compilation){
        if(Type is not INamedTypeSymbol Named){ return false; }
        if(Named.TypeKind==TypeKind.Interface){
            return Named.OriginalDefinition.SpecialType is SpecialType.System_Collections_Generic_IEnumerable_T
                or SpecialType.System_Collections_Generic_IReadOnlyCollection_T or SpecialType.System_Collections_Generic_IReadOnlyList_T
                or SpecialType.System_Collections_Generic_ICollection_T or SpecialType.System_Collections_Generic_IList_T;
        }
        if(Named.OriginalDefinition.GetAttributes().Any(static Attribute=>
            Attribute.AttributeClass?.ToDisplayString()=="System.Runtime.CompilerServices.CollectionBuilderAttribute")){
            return true;
        }
        if(Named.IsAbstract||Named.TypeKind==TypeKind.Class&&!Named.InstanceConstructors.Any(static Constructor=>
            Constructor.DeclaredAccessibility==Accessibility.Public&&Constructor.Parameters.All(static Parameter=>Parameter.IsOptional))){
            return false;
        }
        for(ITypeSymbol?Current=Named;Current is not null;Current=Current.BaseType){
            foreach(var Add in Current.GetMembers("Add").OfType<IMethodSymbol>()){
                if(!Add.IsStatic&&Add.DeclaredAccessibility==Accessibility.Public&&Add.Parameters.Length>=1
                    &&Add.Parameters.Skip(1).All(static Parameter=>Parameter.IsOptional)
                    &&Compilation.ClassifyConversion(Element,Add.Parameters[0].Type).IsImplicit){
                    return true;
                }
            }
        }
        return false;
    }
    private static ITypeSymbol?ElementTypeOf(ITypeSymbol Type,Compilation Compilation){
        var Enumerable=Compilation.GetSpecialType(SpecialType.System_Collections_Generic_IEnumerable_T);
        foreach(var Interface in Type.AllInterfaces.Prepend(Type as INamedTypeSymbol).OfType<INamedTypeSymbol>()){
            if(SymbolEqualityComparer.Default.Equals(Interface.OriginalDefinition,Enumerable)){ return Interface.TypeArguments[0]; }
        }
        return null;
    }
    /// <summary>メンバー、パラメーター、列挙メンバーの名前を C# ソースとして出力する形式。予約キーワード（<c>@class</c> というフィールドなど）には逐語的識別子の接頭辞が必要。</summary>
    private static string Identifier(string Name)=>SyntaxFacts.GetKeywordKind(Name)==SyntaxKind.None?Name:"@"+Name;
    /// <summary>2^63。float が Q64.64 の整数部に収まらなくなる境界（<c>LogicalScalar.TryFromSingle</c>）。</summary>
    private const float LogicalScalarLimit=9.223372e18f;
    /// <summary>
    /// float で保持できず、リテラルがコンパイルされない数値の不一致、または不一致がないことを示す。<see cref="RealLiteral"/> と同様にコンパイラーの字句解析を使う。
    /// <c>float.TryParse</c> は .NET では無限大を返すが、.NET Framework（VSIX と Visual Studio の MSBuild でのビルド）では失敗する。
    /// 無限大だけを確認すると、そのような数値を受け入れてしまっていた。
    /// </summary>
    private static string?FloatRange(SceneJsonValue Value,Func<string,string>Mismatch)=>
        Value.Kind==SceneJsonKind.Number&&SyntaxFactory.ParseToken(Value.Text!.TrimStart('-')+"f").ContainsDiagnostics
            ?Mismatch("the number is beyond the range of float")
            :null;
    /// <summary>メンバーの単位に変換した数値（回転数形式のドキュメントでは、出力リテラルと同様に変換した角度）、または null。</summary>
    private static float?NumberIn(SceneJsonValue Value,AngleReading Angles,SceneAngleUnit?Unit){
        if(Value.Kind!=SceneJsonKind.Number||!SceneJsonAngles.IsNumber(Value.Text)){ return null; }
        return Unit is { }AngleUnit&&Angles.Turns
            ?SceneJsonAngles.FromTurns(Value.Text!,AngleUnit)
            :float.Parse(Value.Text!,NumberStyles.Float,CultureInfo.InvariantCulture);
    }
    /// <summary>実数リテラル、または型で保持できない数値との不一致。生成したリテラルを拒否するかどうか（CS0594）を、コンパイラーの字句解析で判断する。JSON の負号は独立した C# トークンなので、検査対象に含めない。</summary>
    private static string RealLiteral(string Text,SceneJsonValue Value,string Suffix,Func<string,string>Mismatch){
        if(Value.Kind!=SceneJsonKind.Number){ return Mismatch("expected a number"); }
        if(SyntaxFactory.ParseToken(Text.TrimStart('-')+Suffix).ContainsDiagnostics){
            return Mismatch("the number is beyond the range of "+(Suffix=="m"?"decimal":"double"));
        }
        return Text+Suffix;
    }
    private static string IntegerLiteral(SceneJsonValue Value,long Minimum,ulong Maximum,string Cast,Func<string,string>Mismatch){
        if(Value.Kind!=SceneJsonKind.Number){ return Mismatch("expected an integer"); }
        bool InRange;
        if(long.TryParse(Value.Text,NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out var Signed)){
            InRange=Signed>=Minimum&&(Signed<0||(ulong)Signed<=Maximum);
        } else if(ulong.TryParse(Value.Text,NumberStyles.None,CultureInfo.InvariantCulture,out var Unsigned)){
            InRange=Unsigned<=Maximum;
        } else {
            // ulong の範囲を超える数字だけのテキストは、どちらの解析でも失敗する。整数がないのではなく範囲外。
            var Digits=Value.Text!.TrimStart('-');
            return Mismatch(Digits.Length>0&&Digits.All(static Digit=>Digit>='0'&&Digit<='9')
                ?"the integer is out of range"
                :"expected an integer without a fraction or exponent");
        }
        if(!InRange){ return Mismatch("the integer is out of range"); }
        // 負のリテラルへキャストするときは括弧が必要。(short)(-5) が正しく、(short)-5 ではない。
        return Cast.Length==0?Value.Text!:Cast+"("+Value.Text+")";
    }
    private static void RejectUnknownKeys(SceneJsonValue Object,string[]Allowed,string Kind,List<SceneJsonProblem>Problems){
        foreach(var Property in Object.Properties){
            if(Array.IndexOf(Allowed,Property.Name)<0){
                Problems.Add(new SceneJsonProblem(SceneJsonDiagnostics.InvalidDocument,Property.NameStart,Property.NameStart+Property.Name.Length+2,
                    $"'{Property.Name}' is not a {Kind} key (expected {string.Join(", ",Allowed)})."));
            }
        }
    }
    private static string?OptionalString(SceneJsonValue Object,string Name,List<SceneJsonProblem>Problems){
        if(Object.Get(Name) is not { }Value){ return null; }
        if(Value.Kind!=SceneJsonKind.String){
            Problems.Add(Invalid(Value,$"'{Name}' must be a string."));
            return null;
        }
        return Value.Text;
    }
    private static SceneJsonProblem Invalid(SceneJsonValue At,string Message)=>new(SceneJsonDiagnostics.InvalidDocument,At.Start,At.End,Message);
    /// <summary>
    /// この名前で Scene クラスを生成できない理由、または生成上の問題がないことを示す。生成する <c>partial class</c> は同名のユーザー定義 partial クラスに結合する。ただし
    /// 、型ではないもの（構造体、partial ではないクラス、参照アセンブリの型）や、同じ完全修飾名の名前空間があると生成ファイルがコンパイルできない（CS0101、CS0260、CS0261、
    /// CS0436）。Scene 自身の生成ファイル（<c>{FullName}.Scene.g.cs</c>。アナライザーまたはエディターから見えるコンパイル内のもの）は競合しない。
    /// </summary>
    private static string?ClassNameConflict(string?Namespace,string ClassName,Compilation Compilation){
        var FullName=Namespace is null?ClassName:Namespace+"."+ClassName;
        foreach(var Type in Compilation.GetTypesByMetadataName(FullName)){
            var Declarations=Type.DeclaringSyntaxReferences.Where(static Reference=>
                !Reference.SyntaxTree.FilePath.EndsWith(".Scene.g.cs",StringComparison.OrdinalIgnoreCase)).ToArray();
            if(Type.DeclaringSyntaxReferences.IsEmpty
                ||Declarations.Length>0&&(Type.TypeKind!=TypeKind.Class||Declarations.Any(static Reference=>
                    Reference.GetSyntax() is not Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax Declaration
                    ||!Declaration.Modifiers.Any(SyntaxKind.PartialKeyword)))){
                return $"'{FullName}' is already a type of the project or its references; give the scene another className or namespace.";
            }
        }
        INamespaceSymbol?Current=Compilation.GlobalNamespace;
        foreach(var Segment in FullName.Split('.')){
            Current=Current?.GetNamespaceMembers().FirstOrDefault(Member=>Member.Name==Segment);
        }
        return Current is null?null:$"'{FullName}' is already a namespace; give the scene another className or namespace.";
    }
    private static bool IsIdentifier(string Value)=>SyntaxFacts.IsValidIdentifier(Value)&&SyntaxFacts.GetKeywordKind(Value)==SyntaxKind.None;
}
