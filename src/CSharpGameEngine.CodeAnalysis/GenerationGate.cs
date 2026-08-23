using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// <c>Compilation</c>から、生成の可否判定に必要な事実<b>だけ</b>を抜き出した比較可能な値(ADR-0040)。
///
/// <para>
/// 従来は最終出力段が<c>Combine(Context.CompilationProvider)</c>していたため、<c>Compilation</c>
/// インスタンスがキャッシュキーに含まれ、<b>キーストロークのたびに重い生成段が丸ごと再実行</b>されていた。
/// 実際に必要なのはここにある3つの事実だけなので、小さな値へ縮約してインクリメンタル性を取り戻す。
/// </para>
///
/// <para>
/// netstandard2.0では<c>record</c>が使えないため(<c>IsExternalInit</c>が無い)、
/// <see cref="IEquatable{T}"/>を手書きする。
/// </para>
/// </summary>
internal readonly struct GenerationGate(bool HasRuntimeMarker,OutputKind OutputKind,bool HasUserEntryPoint,string LogicalSpatialProfile="Unknown") : IEquatable<GenerationGate> {
    /// <summary>
    /// <c>CSharpGameEngine.Entities.ISystem</c>がコンパイル内に実在するか。ADR-0014により1つの
    /// アナライザーパッケージへ複数のGeneratorが同居するため、このECSを使う意図が無いプロジェクトでも
    /// 参照だけで本Generatorが走る。無条件に出力すると空の<c>World</c>等が紛れ込んで実際にビルドを壊す
    /// (ADR-0032実装時に確認済みの回帰)。
    /// </summary>
    internal readonly bool HasRuntimeMarker=HasRuntimeMarker;

    internal readonly OutputKind OutputKind=OutputKind;

    /// <summary>ユーザーが<c>Program.cs</c>を持っているか。持っていればエントリポイントを生成しない(CS0017の回避)。</summary>
    internal readonly bool HasUserEntryPoint=HasUserEntryPoint;
    internal readonly string LogicalSpatialProfile=LogicalSpatialProfile;

    internal static GenerationGate FromCompilation(Compilation Compilation,CancellationToken CancellationToken){
        var LogicalSpatialProfile="Unknown";
        foreach(var Tree in Compilation.SyntaxTrees){
            var Text=Tree.GetText(CancellationToken).ToString();
            if(Text.IndexOf("Logical256",StringComparison.Ordinal)>=0){ LogicalSpatialProfile="Logical256"; break; }
            if(Text.IndexOf("Logical192",StringComparison.Ordinal)>=0){ LogicalSpatialProfile="Logical192"; }
            else if(Text.IndexOf("Logical128",StringComparison.Ordinal)>=0){ LogicalSpatialProfile="Logical128"; }
        }
        return new GenerationGate(
            Compilation.GetTypeByMetadataName(GameSystemAnalyzer.SystemInterfaceMetadataName) is not null,
            Compilation.Options.OutputKind,
            Compilation.GetEntryPoint(CancellationToken) is not null,
            LogicalSpatialProfile);
    }

    public bool Equals(GenerationGate Other){
        return this.HasRuntimeMarker==Other.HasRuntimeMarker
            &&this.OutputKind==Other.OutputKind
            &&this.HasUserEntryPoint==Other.HasUserEntryPoint
            &&string.Equals(this.LogicalSpatialProfile,Other.LogicalSpatialProfile,StringComparison.Ordinal);
    }

    public override bool Equals(object?Obj)=>Obj is GenerationGate Other&&this.Equals(Other);

    public override int GetHashCode(){
        unchecked {
            var Hash=17;
            Hash=(Hash*31)+(this.HasRuntimeMarker?1:0);
            Hash=(Hash*31)+(int)this.OutputKind;
            Hash=(Hash*31)+(this.HasUserEntryPoint?1:0);
            Hash=(Hash*31)+StringComparer.Ordinal.GetHashCode(this.LogicalSpatialProfile);
            return Hash;
        }
    }
}
