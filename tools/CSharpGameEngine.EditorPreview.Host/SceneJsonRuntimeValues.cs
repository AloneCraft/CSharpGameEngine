using System.Globalization;
using System.Reflection;
using CSharpGameEngine.SceneAnalysis.Json;

namespace CSharpGameEngine.EditorPreview.Host;

/// <summary>
/// リフレクションを使ってシーン JSON の値をゲーム独自の型の値へ変換する (ADR-0070 決定 6)。ジェネレーターの規則 (<c>SceneJsonSceneAnalyzer</c>) に従う。enum はメンバー名で指定し、数値は数値メンバーまたは <c>float</c> から暗黙の変換がある型に設定する。オブジェクトには、最も多くのキーを受け取り、その後メンバーも設定できる public コンストラクターを使う。ランタイムビューはこれを使ってエディターの値を実行中のワールドに適用する。ドキュメントはすでにジェネレーターで検査済みである。
/// スキーマ バージョン 2 以降のドキュメントでは、<c>[Angle]</c> メンバー内のすべての数値が回転数で記述され、角度のキーでは C# の単位語を省略できる (ADR-0074、<see cref="SceneJsonAngles.KeyFor"/> を参照)。
/// </summary>
internal static class SceneJsonRuntimeValues {
    internal static object?Create(Type Type,SceneJsonValue Value,string Where)=>Create(Type,Value,Where,new Reading(Turns:false),null);

    /// <summary>コンポーネント：メンバーオブジェクトを、コンポーネント型のオブジェクト値として読み込む。</summary>
    internal static object Component(Type Type,SceneJsonValue Members,string Where,bool Turns=false)=>
        Object(Type,Members,Where,new Reading(Turns),null);

    /// <summary>
    /// ドキュメント名で指定されたコンポーネント型。ジェネレーターの解決方法に従い、メタデータ名、または C# とドキュメントの名前 ( <c>Ns.Outer.Inner</c> ) で表される入れ子型を使う。メタデータ名では、入れ子型を '+' でつなぐ。
    /// </summary>
    internal static Type?FindType(IEnumerable<Assembly>Assemblies,string FullName){
        var Name=FullName;
        for(var Dot=Name.Length;Dot>0;Dot=Name.LastIndexOf('.',Dot-1)){
            if(Dot<Name.Length){ Name=Name.Substring(0,Dot)+"+"+Name.Substring(Dot+1); }
            foreach(var Assembly in Assemblies){
                if(Assembly.GetType(Name,throwOnError:false) is { }Type){ return Type; }
            }
        }
        return null;
    }

    /// <summary>ドキュメントの角度が回転数で記述されるかどうか：<c>schemaVersion</c> が 2 以上。</summary>
    internal static bool UsesTurns(SceneJsonValue Document)=>
        Document.Get("schemaVersion") is { Kind:SceneJsonKind.Number }Version
        &&int.TryParse(Version.Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out var Number)
        &&Number>=SceneJsonAngles.TurnsSchemaVersion;

    private sealed record Reading(bool Turns);

    private static object?Create(Type Type,SceneJsonValue Value,string Where,Reading Reading,SceneAngleUnit?Unit){
        if(Value.Kind==SceneJsonKind.Null){
            return !Type.IsValueType||Nullable.GetUnderlyingType(Type) is not null
                ?null
                :throw Mismatch(Where,Type,"null is only valid for reference and nullable types");
        }
        if(Nullable.GetUnderlyingType(Type) is { }Underlying){ return Create(Underlying,Value,Where,Reading,Unit); }
        if(Type.IsEnum){
            return Value.Kind==SceneJsonKind.String&&Enum.GetNames(Type).Contains(Value.Text,StringComparer.Ordinal)
                ?Enum.Parse(Type,Value.Text!)
                :throw Mismatch(Where,Type,"an enum value is written as the member name string");
        }
        if(Type==typeof(bool)){
            return Value.Kind switch {
                SceneJsonKind.True=>true,
                SceneJsonKind.False=>false,
                _=>throw Mismatch(Where,Type,"expected true or false"),
            };
        }
        if(Type==typeof(string)){
            return Value.Kind==SceneJsonKind.String?Value.Text!:throw Mismatch(Where,Type,"expected a string");
        }
        if(Value.Kind==SceneJsonKind.Number){
            // 回転数の角度はメンバーの単位へ変換し、残りの数値処理規則はそのまま適用する。
            var Text=Unit is { }AngleUnit&&Reading.Turns&&SceneJsonAngles.IsNumber(Value.Text)
                ?SceneJsonAngles.FromTurns(Value.Text!,AngleUnit).ToString("R",CultureInfo.InvariantCulture)
                :Value.Text!;
            return Number(Type,Text,Where);
        }
        if(Value.Kind==SceneJsonKind.Array){ return Collection(Type,Value,Where,Reading,Unit); }
        if(Value.Kind==SceneJsonKind.Object&&(Type.IsValueType||Type.IsClass)){ return Object(Type,Value,Where,Reading,Unit); }
        throw Mismatch(Where,Type,"this JSON value cannot be converted to the member's type");
    }

    /// <summary><c>[Angle]</c> 属性で宣言された単位。ゲーム自身の Contracts 読み込みも一致するよう、名前で照合する。</summary>
    private static SceneAngleUnit?AngleUnitOf(IEnumerable<CustomAttributeData>Attributes){
        foreach(var Attribute in Attributes){
            if(Attribute.AttributeType.FullName==SceneJsonAngles.AttributeFullName&&Attribute.ConstructorArguments is [{ }Argument]){
                return SceneJsonAngles.UnitFromArgument(Argument.Value);
            }
        }
        return null;
    }

    private static object Number(Type Type,string Text,string Where){
        IFormatProvider Culture=CultureInfo.InvariantCulture;
        // 実数はジェネレーターが出力するリテラルと同様に読み込む。指数表記も使える（Convert は decimal に対してそれを拒否する）。また、
        // 型の範囲を超える数値はビルドエラー (SCENE203) であり、.NET の解析では無限大になる。
        if(Type==typeof(float)){
            return float.TryParse(Text,RealStyles,Culture,out var Single)&&float.IsFinite(Single)
                ?Single
                :throw Mismatch(Where,Type,"the number is beyond the range of float");
        }
        if(Type.GetTypeCode(Type) is TypeCode.Double or TypeCode.Decimal){
            return Real(Type,Text)??throw Mismatch(Where,Type,"the number is beyond the range of "+Type.Name);
        }
        if(!Type.IsEnum&&Type.IsPrimitive&&Type!=typeof(bool)&&Type!=typeof(char)){
            try {
                return Convert.ChangeType(Text,Type,Culture);
            } catch(Exception Error)when(Error is FormatException or OverflowException or InvalidCastException){
                throw Mismatch(Where,Type,Error.Message);
            }
        }
        // ジェネレーターは f サフィックス付きのリテラルを型指定された位置に出力するため、型の
        // float（または float から得られる、より広い実数型）からの暗黙変換が適用される。object または
        // インターフェイス型（IComparable）のメンバーではボックス化し、それ以外では型自身の変換を使う。
        if(Type.IsAssignableFrom(typeof(float))){
            return float.TryParse(Text,RealStyles,Culture,out var Boxed)&&float.IsFinite(Boxed)
                ?Boxed
                :throw Mismatch(Where,Type,"the number is beyond the range of float");
        }
        foreach(var Conversion in Type.GetMethods(BindingFlags.Public|BindingFlags.Static)){
            if(Conversion.Name=="op_Implicit"&&Conversion.ReturnType==Type
                &&Conversion.GetParameters() is [{ }Only]
                &&Type.GetTypeCode(Only.ParameterType) is TypeCode.Single or TypeCode.Double){
                return Invoke(Where,Type,()=>Conversion.Invoke(null,[Convert.ChangeType(float.Parse(Text,Culture),Only.ParameterType,Culture)])!);
            }
        }
        throw Mismatch(Where,Type,"a number needs a numeric member or a type convertible from float");
    }

    /// <summary>コンパイラーがジェネレーターの実数リテラルを読み取る際の JSON 数値の形式（マイナス記号は別トークン）。</summary>
    private const NumberStyles RealStyles=NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint|NumberStyles.AllowExponent;

    /// <summary>JSON 数値から得る有限の double または decimal。型が保持できない場合は null。</summary>
#if CGE_NUMERIC_ADAPTER
    [CSharpGameEngine.Contracts.ExternalNumericAdapter]
#endif
    private static object?Real(Type Type,string Text){
        if(Type.GetTypeCode(Type)==TypeCode.Decimal){
            return decimal.TryParse(Text,RealStyles,CultureInfo.InvariantCulture,out var Decimal)?Decimal:null;
        }
        return double.TryParse(Text,RealStyles,CultureInfo.InvariantCulture,out var Double)&&double.IsFinite(Double)?Double:null;
    }

    private static object Collection(Type Type,SceneJsonValue Value,string Where,Reading Reading,SceneAngleUnit?Unit){
        var Element=Type.IsArray?Type.GetElementType():ElementType(Type);
        if(Element is null){ throw Mismatch(Where,Type,"an array needs an array or collection member"); }
        var Items=Array.CreateInstance(Element,Value.Items.Count);
        for(var Index=0;Index<Items.Length;Index++){
            Items.SetValue(Create(Element,Value.Items[Index],Where+"["+Index.ToString(CultureInfo.InvariantCulture)+"]",Reading,Unit),Index);
        }
        if(Type.IsAssignableFrom(Items.GetType())){ return Items; }
        if(Type.IsGenericType&&Type.GetGenericTypeDefinition().FullName=="System.Collections.Immutable.ImmutableArray`1"){
            var Create=Type.Assembly.GetType("System.Collections.Immutable.ImmutableArray")!
                .GetMethods().First(static Method=>Method.Name=="Create"&&Method.GetParameters() is [{ ParameterType.IsArray:true }]);
            return Create.MakeGenericMethod(Element).Invoke(null,[Items])!;
        }
        // そのほかは、ジェネレーターがコレクション式を作成する方法に従う。public な引数なしコンストラクターを持つ型と、
        // 要素 1 つを受け取る public Add を持つ型 (List<T>, HashSet<T>)。それ以外は [CollectionBuilder] 型 (ImmutableList<T>、
        // ReadOnlyCollection<T>) を使い、ビルダーの CreateRange、または要素を受け取る public コンストラクターで作成する。
        if(!Type.IsAbstract&&(Type.IsValueType||Type.GetConstructor(Type.EmptyTypes) is not null)&&AddMethod(Type,Element) is { }Add){
            var Filled=Activator.CreateInstance(Type)!;
            foreach(var Item in Items){ Invoke(Where,Type,()=>Add.Invoke(Filled,[Item])); }
            return Filled;
        }
        if(CollectionBuilder(Type) is { }Builder){
            foreach(var Method in Builder.GetMethods(BindingFlags.Public|BindingFlags.Static)){
                if(Method.Name!="CreateRange"||!Method.IsGenericMethodDefinition||Method.GetGenericArguments().Length!=1){ continue; }
                MethodInfo Closed;
                try { Closed=Method.MakeGenericMethod(Element); } catch(ArgumentException){ continue; }
                if(Closed.ReturnType==Type&&Closed.GetParameters() is [{ }Only]&&Only.ParameterType.IsAssignableFrom(Items.GetType())){
                    return Invoke(Where,Type,()=>Closed.Invoke(null,[Items])!);
                }
            }
            // コレクション式は要素を渡すコンストラクターを呼び出さないため、この方法で作成されるのはビルダー型だけ。
            foreach(var Constructor in Type.GetConstructors()){
                if(Constructor.GetParameters() is [{ }Only]&&Only.ParameterType.IsAssignableFrom(Items.GetType())){
                    return Invoke(Where,Type,()=>Constructor.Invoke([Items]));
                }
            }
        }
        throw Mismatch(Where,Type,"this collection type cannot be created from a JSON array");
    }

    /// <summary>要素 1 つを受け取る public インスタンス Add（それ以外の引数は省略可能）。</summary>
    private static MethodInfo?AddMethod(Type Type,Type Element)=>
        Type.GetMethods(BindingFlags.Public|BindingFlags.Instance).FirstOrDefault(Method=>Method.Name=="Add"
            &&Method.GetParameters() is [{ }First,..]&&First.ParameterType.IsAssignableFrom(Element)
            &&Method.GetParameters().Skip(1).All(static Parameter=>Parameter.IsOptional));

    /// <summary><c>[CollectionBuilder]</c> 属性が指定するビルダー型。ゲーム自身の読み込み規則に合わせ、名前で照合する。</summary>
    private static Type?CollectionBuilder(Type Type)=>
        (Type.IsGenericType?Type.GetGenericTypeDefinition():Type).CustomAttributes
            .FirstOrDefault(static Attribute=>Attribute.AttributeType.FullName=="System.Runtime.CompilerServices.CollectionBuilderAttribute")
            ?.ConstructorArguments is [{ Value:Type Builder },..]?Builder:null;

    private static Type?ElementType(Type Type)=>
        Type.GetInterfaces().Prepend(Type)
            .FirstOrDefault(static Interface=>Interface.IsGenericType&&Interface.GetGenericTypeDefinition()==typeof(IEnumerable<>))
            ?.GetGenericArguments()[0];

    private static object Object(Type Type,SceneJsonValue Value,string Where,Reading Reading,SceneAngleUnit?Unit){
        var Keys=Value.Properties.Select(static Property=>Property.Name).ToHashSet(StringComparer.Ordinal);
        var Constructor=ChooseConstructor(Type,Keys);
        HashSet<string>Consumed=new(StringComparer.Ordinal);
        object Instance;
        if(Constructor is not null){
            var Arguments=Constructor.GetParameters().Select(Parameter=>{
                var Key=KeyOf(Type,Parameter.Name!,Parameter.CustomAttributes);
                if((Value.Get(Parameter.Name!)??Value.Get(Key)) is { }Argument){
                    Consumed.Add(Parameter.Name!);
                    Consumed.Add(Key);
                    return Create(Parameter.ParameterType,Argument,Where+"."+Parameter.Name,Reading,Unit??AngleUnitOf(Parameter.CustomAttributes));
                }
                return Parameter.DefaultValue;
            }).ToArray();
            Instance=Invoke(Where,Type,()=>Constructor.Invoke(Arguments));
        } else {
            // 構造体の引数なしコンストラクターは、`new T { ... }` と同様にフィールド初期化子を実行する。クラスではそれを
            // 行わない。その場合、呼び出し元へ例外を伝えるのではなく不適合として扱う（生成コードもコンパイルできない）。
            try {
                Instance=Activator.CreateInstance(Type)
                    ??throw Mismatch(Where,Type,"the type cannot be constructed");
            } catch(MemberAccessException){
                throw Mismatch(Where,Type,"the type has no public parameterless constructor");
            }
        }
        foreach(var Property in Value.Properties){
            if(Consumed.Contains(Property.Name)){ continue; }
            switch(FindSettableMember(Type,Property.Name)){
                case FieldInfo Field:
                    Field.SetValue(Instance,Create(Field.FieldType,Property.Value,Where+"."+Property.Name,Reading,Unit??AngleUnitOf(Field.CustomAttributes)));
                    break;
                case PropertyInfo Member:{
                    var MemberValue=Create(Member.PropertyType,Property.Value,Where+"."+Property.Name,Reading,Unit??AngleUnitOf(Member.CustomAttributes));
                    Invoke(Where+"."+Property.Name,Member.PropertyType,()=>{ Member.SetValue(Instance,MemberValue);return MemberValue; });
                    break;
                }
                default:
                    throw Mismatch(Where,Type,$"'{Property.Name}' is not a settable member or constructor parameter");
            }
        }
        return Instance;
    }

    /// <summary>ジェネレーターの選択規則：パラメーターがすべて名前付きまたは省略可能で、最も多くのキーを消費する public コンストラクター。</summary>
    private static ConstructorInfo?ChooseConstructor(Type Type,HashSet<string>Keys){
        ConstructorInfo?Best=null;
        var BestCount=-1;
        foreach(var Candidate in Type.GetConstructors(BindingFlags.Public|BindingFlags.Instance)){
            // ジェネレーターと同様に、ref/out パラメーターには値を渡せず、エラーとして obsolete 指定されたコンストラクターも呼び出せない。
            if(Candidate.GetParameters().Any(static Parameter=>Parameter.ParameterType.IsByRef&&!Parameter.IsIn)
                ||Candidate.CustomAttributes.Any(static Attribute=>Attribute.AttributeType.FullName=="System.ObsoleteAttribute"
                    &&Attribute.ConstructorArguments is [_,{ Value:true }])){
                continue;
            }
            var Count=0;
            var Usable=true;
            foreach(var Parameter in Candidate.GetParameters()){
                if(Keys.Contains(Parameter.Name!)||Keys.Contains(KeyOf(Type,Parameter.Name!,Parameter.CustomAttributes))){
                    Count++;
                } else if(!Parameter.HasDefaultValue){
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

    /// <summary>キーが示す設定可能なメンバー。C# 名、または角度なら単位語を除いたキーで指定する。</summary>
    private static MemberInfo?FindSettableMember(Type Type,string Name){
        if(FindSettableMemberNamed(Type,Name) is { }Named){ return Named; }
        foreach(var Member in Type.GetMembers(BindingFlags.Public|BindingFlags.Instance)){
            if(Member.Name!=Name&&KeyOf(Type,Member.Name,Member.CustomAttributes)==Name&&FindSettableMemberNamed(Type,Member.Name) is { }Angle){
                return Angle;
            }
        }
        return null;
    }

    private static MemberInfo?FindSettableMemberNamed(Type Type,string Name){
        if(Type.GetField(Name,BindingFlags.Public|BindingFlags.Instance) is { IsInitOnly:false,IsLiteral:false }Field){ return Field; }
        return Type.GetProperty(Name,BindingFlags.Public|BindingFlags.Instance) is { SetMethod.IsPublic:true }Property?Property:null;
    }

    /// <summary>ジェネレーターの規則に従う、メンバーまたはコンストラクターパラメーターのキー (<c>SceneJsonSceneAnalyzer.KeyOf</c>)。</summary>
    private static string KeyOf(Type Type,string Name,IEnumerable<CustomAttributeData>Attributes)=>
        AngleUnitOf(Attributes) is null?Name:SceneJsonAngles.KeyFor(Name,Key=>
            Type.GetMember(Key,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static).Length>0
            ||Type.GetConstructors(BindingFlags.Public|BindingFlags.Instance).Any(Constructor=>Constructor.GetParameters().Any(Parameter=>Parameter.Name==Key)));

    private static InvalidOperationException Mismatch(string Where,Type Type,string Reason)=>
        new($"The value for '{Where}' does not fit '{Type.FullName}': {Reason}.");

    /// <summary>
    /// ゲーム型の変換、コンストラクター、または setter を呼び出す。型自身が拒否する値（Q64.64 の範囲を超える位置、負のライト色など）は適用できず、呼び出し元は最後に正常だった値を維持できる。
    /// </summary>
    private static T Invoke<T>(string Where,Type Type,Func<T>Call){
        try {
            return Call();
        } catch(TargetInvocationException Error)when(Error.InnerException is { }Rejection){
            throw Mismatch(Where,Type,Rejection.Message);
        }
    }
}
