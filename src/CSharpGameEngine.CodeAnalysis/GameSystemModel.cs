using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis;

/// <remarks>
/// このプロジェクトはRoslynホストとの互換性のためnetstandard2.0を対象としており(ADR-0014)、
/// <c>record</c>/<c>record struct</c>が要求する<c>System.Runtime.CompilerServices.IsExternalInit</c>を
/// BCLが提供しないため、recordではなく通常のstructとして定義する(既存Model群と同じ理由)。
/// </remarks>
internal enum ComponentAccess
{
    Read,
    Write,
}

/// <summary>パラメータ順(<see cref="ComponentAccess"/>を除きQueryのAll集合にもなる)を保った、1コンポーネントパラメータの情報。</summary>
internal readonly struct ComponentParameterModel
{
    internal readonly string ParameterName;
    internal readonly string TypeFullyQualifiedName;
    internal readonly ComponentAccess Access;
    internal readonly bool IsTag;

    internal ComponentParameterModel(string parameterName, string typeFullyQualifiedName, ComponentAccess access, bool isTag)
    {
        this.ParameterName = parameterName;
        this.TypeFullyQualifiedName = typeFullyQualifiedName;
        this.Access = access;
        this.IsTag = isTag;
    }
}

/// <summary>[Context]パラメータの情報。値型なら<c>in</c>、参照型なら値渡しでexecutorへそのまま転送される。</summary>
internal readonly struct ContextParameterModel
{
    internal readonly string ParameterName;
    internal readonly string TypeFullyQualifiedName;
    internal readonly bool IsReferenceType;

    internal ContextParameterModel(string parameterName, string typeFullyQualifiedName, bool isReferenceType)
    {
        this.ParameterName = parameterName;
        this.TypeFullyQualifiedName = typeFullyQualifiedName;
        this.IsReferenceType = isReferenceType;
    }
}

internal enum ParameterRole
{
    Component,
    Context,
    CurrentEntity,
    Commands,
}

/// <summary>
/// 元のメソッド宣言でのパラメータ並び順を1つずつ表す。<see cref="SystemMethodModel.ComponentParameters"/>/
/// <see cref="SystemMethodModel.ContextParameters"/>は役割ごとにまとめられているため並び順の情報を
/// 失っており、Emitterがユーザーメソッドを位置引数で正しく呼び出すにはこの順序情報が別途必要になる。
/// </summary>
internal readonly struct ParameterSlot
{
    internal readonly ParameterRole Role;
    internal readonly int Index;

    internal ParameterSlot(ParameterRole role, int index)
    {
        this.Role = role;
        this.Index = index;
    }
}

/// <summary>1つの<c>[Update]</c>メソッドの解析結果。</summary>
internal readonly struct SystemMethodModel
{
    internal readonly string MethodName;
    internal readonly int Order;
    internal readonly int MemberIndex;
    internal readonly Location MethodLocation;
    internal readonly ImmutableArray<ComponentParameterModel> ComponentParameters;
    internal readonly ImmutableArray<ContextParameterModel> ContextParameters;
    internal readonly ImmutableArray<ParameterSlot> CallOrder;
    internal readonly bool UsesCurrentEntity;
    internal readonly bool UsesCommands;
    internal readonly ImmutableArray<string> WithoutComponentTypes;
    internal readonly ImmutableArray<string> ReadComponentTypes;
    internal readonly ImmutableArray<string> WriteComponentTypes;

    internal SystemMethodModel(
        string methodName,
        int order,
        int memberIndex,
        Location methodLocation,
        ImmutableArray<ComponentParameterModel> componentParameters,
        ImmutableArray<ContextParameterModel> contextParameters,
        ImmutableArray<ParameterSlot> callOrder,
        bool usesCurrentEntity,
        bool usesCommands,
        ImmutableArray<string> withoutComponentTypes,
        ImmutableArray<string> readComponentTypes,
        ImmutableArray<string> writeComponentTypes)
    {
        this.MethodName = methodName;
        this.Order = order;
        this.MemberIndex = memberIndex;
        this.MethodLocation = methodLocation;
        this.ComponentParameters = componentParameters;
        this.ContextParameters = contextParameters;
        this.CallOrder = callOrder;
        this.UsesCurrentEntity = usesCurrentEntity;
        this.UsesCommands = usesCommands;
        this.WithoutComponentTypes = withoutComponentTypes;
        this.ReadComponentTypes = readComponentTypes;
        this.WriteComponentTypes = writeComponentTypes;
    }

    /// <summary>
    /// <paramref name="classNameOrFullyQualifiedName"/>には、宣言側(同一名前空間内からの参照、
    /// 単純クラス名でよい)と、生成ゲームループ側(名前空間外からの参照、
    /// <see cref="GameSystemClassResult.ClassFullyQualifiedName"/>が必要)のどちらも渡せる。
    /// クラス名を含めるのは、メソッド名の重複はクラス内でのみSYSGEN018が禁止しており、
    /// 異なる[GameSystem]クラスが同名メソッド(例: 複数クラスの"Update")を持つのは正当なため、
    /// メソッド名だけではExecutor型名が同一名前空間内で衝突しうるため。
    /// </summary>
    internal string BuildExecutorTypeName(string classNameOrFullyQualifiedName) => $"{classNameOrFullyQualifiedName}_{this.MethodName}_Executor";
}

/// <summary>1つの<c>[GameSystem]</c>クラスの解析結果。<see cref="Diagnostics"/>が1件でもあれば、このクラス分のExecutor生成をスキップする。</summary>
internal readonly struct GameSystemClassResult
{
    internal readonly string? Namespace;
    internal readonly string ClassName;
    internal readonly ImmutableArray<SystemMethodModel> Methods;
    internal readonly ImmutableArray<Diagnostic> Diagnostics;

    internal GameSystemClassResult(string? @namespace, string className, ImmutableArray<SystemMethodModel> methods, ImmutableArray<Diagnostic> diagnostics)
    {
        this.Namespace = @namespace;
        this.ClassName = className;
        this.Methods = methods;
        this.Diagnostics = diagnostics;
    }

    internal string ClassFullyQualifiedName => this.Namespace is null ? this.ClassName : $"{this.Namespace}.{this.ClassName}";
}
