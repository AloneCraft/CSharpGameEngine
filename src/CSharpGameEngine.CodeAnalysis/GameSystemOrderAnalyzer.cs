using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// <c>[SystemOrder]</c>(ADR-0036)を解析し、System単位/ファイル単位2種類のOrderマップを組み立てる。
/// ADR-0033決定事項2(属性を一切使わない構造的推論)からの唯一の例外で、複数ファイル・複数Systemの
/// 相対実行順序という、どの構造からも導出できない情報を宣言的に受け取るためだけに存在する。
/// </summary>
internal static class GameSystemOrderAnalyzer
{
    internal const string SystemOrderAttributeFullyQualifiedName = "CSharpGameEngine.Entities.SystemOrderAttribute";

    /// <summary>
    /// 対象が<c>ISystem</c>実装structなら<see cref="GameSystemOrderTargetKind.System"/>(その1つだけの
    /// 順序)、それ以外のclassなら<see cref="GameSystemOrderTargetKind.FileMarker"/>(ファイルの既定順序)
    /// として扱う。structだが<c>ISystem</c>を実装していない場合はSYSGEN025(付けても効果が無い)。
    /// </summary>
    internal static GameSystemOrderResult Analyze(GeneratorAttributeSyntaxContext ctx)
    {
        ImmutableArray<Diagnostic>.Builder diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        var symbol = (INamedTypeSymbol)ctx.TargetSymbol;
        Location location = symbol.Locations.FirstOrDefault() ?? Location.None;
        string filePath = ctx.TargetNode.SyntaxTree.FilePath;

        int order = 0;
        AttributeData attribute = ctx.Attributes[0];

        if (attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is int value)
        {
            order = value;
        }

        if (symbol.TypeKind == TypeKind.Struct)
        {
            bool implementsISystem = symbol.AllInterfaces.Any(static i =>
                i.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == GameSystemAnalyzer.SystemInterfaceFullyQualifiedName);

            if (!implementsISystem)
            {
                diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.SystemOrderOnNonSystemStruct, location, symbol.Name));
            }

            return new GameSystemOrderResult(
                GameSystemOrderTargetKind.System,
                filePath,
                order,
                symbol.Name,
                GameSystemAnalyzer.ComputeClassFullyQualifiedName(symbol),
                location,
                diagnostics.ToImmutable());
        }

        if (!symbol.IsFileLocal)
        {
            diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.SystemOrderMarkerMustBeFileScoped, location, symbol.Name));
        }

        return new GameSystemOrderResult(
            GameSystemOrderTargetKind.FileMarker,
            filePath,
            order,
            symbol.Name,
            null,
            location,
            diagnostics.ToImmutable());
    }

    /// <summary>
    /// 個々の<see cref="Analyze"/>結果を集約し、System単位/ファイル単位2つのマップと、集約段でしか
    /// 検出できない診断(1ファイル複数マーカー・複数ファイル同一値)を作る。
    /// </summary>
    internal static (GameSystemOrderContext Context, ImmutableArray<Diagnostic> Diagnostics) BuildMap(
        ImmutableArray<GameSystemOrderResult> results)
    {
        ImmutableArray<Diagnostic>.Builder diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        foreach (GameSystemOrderResult result in results)
        {
            diagnostics.AddRange(result.Diagnostics);
        }

        Dictionary<string, int> systemOrderByFqn = new(StringComparer.Ordinal);

        foreach (GameSystemOrderResult result in results)
        {
            if (result.Kind == GameSystemOrderTargetKind.System && result.SystemFullyQualifiedName is { } fqn)
            {
                // AllowMultiple=falseにより同一Systemへの複数付与はコンパイラ自身がCS0579で弾くため、
                // ここでの重複は起こり得ない。System同士が同じ明示値を選ぶのは意図的な「同着」を
                // 表現する正当な使い方なので、重複チェックはしない(ファイル既定値とは扱いが異なる)。
                systemOrderByFqn[fqn] = result.Order;
            }
        }

        List<GameSystemOrderResult> fileMarkers = [.. results.Where(static r => r.Kind == GameSystemOrderTargetKind.FileMarker)];
        Dictionary<string, int> fileOrderByPath = new(StringComparer.Ordinal);

        foreach (IGrouping<string, GameSystemOrderResult> group in fileMarkers.GroupBy(static r => r.FilePath, StringComparer.Ordinal))
        {
            List<GameSystemOrderResult> markers = [.. group];

            if (markers.Count > 1)
            {
                for (int i = 1; i < markers.Count; i++)
                {
                    diagnostics.Add(Diagnostic.Create(
                        GameSystemDiagnostics.DuplicateFileOrderInOneFile,
                        markers[i].Location,
                        markers[0].FilePath,
                        markers[0].TargetName,
                        markers[i].TargetName));
                }

                // 曖昧な状態でも生成は続行できるよう、宣言順で先に見つかったものを採用する。
                // TreatWarningsAsErrors=trueにより上記診断でビルド自体は失敗するため、この選択が
                // 実害を持つのは診断を無視して読み進めた場合のみ。
                markers.Sort(static (a, b) => a.Location.SourceSpan.Start.CompareTo(b.Location.SourceSpan.Start));
            }

            fileOrderByPath[group.Key] = markers[0].Order;
        }

        // ファイル既定値の衝突だけをエラーにする(ユーザー確認済み): あるファイル全体の実行順が
        // 未定義に戻ってしまうため。System単体の明示値同士が衝突しても「意図的な同着」でありうるため
        // 対象外(前段のsystemOrderByFqn側では重複チェックをしていない)。
        foreach (IGrouping<int, KeyValuePair<string, int>> group in fileOrderByPath.GroupBy(static kv => kv.Value))
        {
            List<string> filePaths = [.. group.Select(static kv => kv.Key)];

            if (filePaths.Count < 2)
            {
                continue;
            }

            filePaths.Sort(StringComparer.Ordinal);

            for (int i = 1; i < filePaths.Count; i++)
            {
                diagnostics.Add(Diagnostic.Create(
                    GameSystemDiagnostics.DuplicateFileOrderValue,
                    Location.None,
                    filePaths[0],
                    filePaths[i],
                    group.Key));
            }
        }

        return (new GameSystemOrderContext(systemOrderByFqn, fileOrderByPath), diagnostics.ToImmutable());
    }
}
