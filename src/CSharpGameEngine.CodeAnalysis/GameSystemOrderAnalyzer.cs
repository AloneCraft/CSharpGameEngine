using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// <c>[SystemOrder]</c>(ADR-0036)を解析し、System単位/ファイル単位2種類のOrderマップを組み立てる。
/// ADR-0033決定事項2(属性を一切使わない構造的推論)からの唯一の例外で、複数ファイル・複数Systemの
/// 相対実行順序という、どの構造からも導出できない情報を宣言的に受け取るためだけに存在する。
/// </summary>
internal static class GameSystemOrderAnalyzer {
    internal const string SystemOrderAttributeFullyQualifiedName="CSharpGameEngine.Entities.SystemOrderAttribute";

    /// <summary>
    /// 対象が<c>ISystem</c>実装structなら<see cref="GameSystemOrderTargetKind.System"/>(その1つだけの
    /// 順序)、それ以外のclassなら<see cref="GameSystemOrderTargetKind.FileMarker"/>(ファイルの既定順序)
    /// として扱う。structだが<c>ISystem</c>を実装していない場合はSYSGEN025(付けても効果が無い)。
    /// </summary>
    internal static GameSystemOrderResult Analyze(GeneratorAttributeSyntaxContext Ctx){
        ImmutableArray<Diagnostic>.Builder Diagnostics=ImmutableArray.CreateBuilder<Diagnostic>();

        var Symbol=(INamedTypeSymbol)Ctx.TargetSymbol;
        Location Location=Symbol.Locations.FirstOrDefault()??Location.None;
        var FilePath=Ctx.TargetNode.SyntaxTree.FilePath;

        var Order=0;
        AttributeData Attribute=Ctx.Attributes[0];

        if(Attribute.ConstructorArguments.Length==1&&Attribute.ConstructorArguments[0].Value is int Value){
            Order=Value;
        }

        if(Symbol.TypeKind==TypeKind.Struct){
            var ImplementsISystem=Symbol.AllInterfaces.Any(static I=>
                I.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)==GameSystemAnalyzer.SystemInterfaceFullyQualifiedName);

            if(!ImplementsISystem){
                Diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.SystemOrderOnNonSystemStruct,Location,Symbol.Name));
            }

            return new GameSystemOrderResult(
                GameSystemOrderTargetKind.System,
                FilePath,
                Order,
                Symbol.Name,
                GameSystemAnalyzer.ComputeClassFullyQualifiedName(Symbol),
                Location,
                Diagnostics.ToImmutable());
        }

        if(!Symbol.IsFileLocal){
            Diagnostics.Add(Diagnostic.Create(GameSystemDiagnostics.SystemOrderMarkerMustBeFileScoped,Location,Symbol.Name));
        }

        return new GameSystemOrderResult(
            GameSystemOrderTargetKind.FileMarker,
            FilePath,
            Order,
            Symbol.Name,
            null,
            Location,
            Diagnostics.ToImmutable());
    }

    /// <summary>
    /// 個々の<see cref="Analyze"/>結果を集約し、System単位/ファイル単位2つのマップと、集約段でしか
    /// 検出できない診断(1ファイル複数マーカー・複数ファイル同一値)を作る。
    /// </summary>
    internal static(GameSystemOrderContext Context,ImmutableArray<Diagnostic>Diagnostics)BuildMap(
        ImmutableArray<GameSystemOrderResult>Results){
        ImmutableArray<Diagnostic>.Builder Diagnostics=ImmutableArray.CreateBuilder<Diagnostic>();

        foreach(GameSystemOrderResult Result in Results){
            Diagnostics.AddRange(Result.Diagnostics);
        }

        Dictionary<string,int>SystemOrderByFqn=new(StringComparer.Ordinal);

        foreach(GameSystemOrderResult Result in Results){
            if(Result.Kind==GameSystemOrderTargetKind.System&&Result.SystemFullyQualifiedName is { } Fqn){
                // AllowMultiple=falseにより同一Systemへの複数付与はコンパイラ自身がCS0579で弾くため、
                // ここでの重複は起こり得ない。System同士が同じ明示値を選ぶのは意図的な「同着」を
                // 表現する正当な使い方なので、重複チェックはしない(ファイル既定値とは扱いが異なる)。
                SystemOrderByFqn[Fqn]=Result.Order;
            }
        }

        List<GameSystemOrderResult>FileMarkers=[.. Results.Where(static R=>R.Kind==GameSystemOrderTargetKind.FileMarker)];
        Dictionary<string,int>FileOrderByPath=new(StringComparer.Ordinal);

        foreach(IGrouping<string,GameSystemOrderResult>Group in FileMarkers.GroupBy(static R=>R.FilePath,StringComparer.Ordinal)){
            List<GameSystemOrderResult>Markers=[.. Group];

            if(Markers.Count>1){
                for(var I=1;I<Markers.Count;I++){
                    Diagnostics.Add(Diagnostic.Create(
                        GameSystemDiagnostics.DuplicateFileOrderInOneFile,
                        Markers[I].Location,
                        Markers[0].FilePath,
                        Markers[0].TargetName,
                        Markers[I].TargetName));
                }

                // 曖昧な状態でも生成は続行できるよう、宣言順で先に見つかったものを採用する。
                // TreatWarningsAsErrors=trueにより上記診断でビルド自体は失敗するため、この選択が
                // 実害を持つのは診断を無視して読み進めた場合のみ。
                Markers.Sort(static(A,B)=>A.Location.SourceSpan.Start.CompareTo(B.Location.SourceSpan.Start));
            }

            FileOrderByPath[Group.Key]=Markers[0].Order;
        }

        // ファイル既定値の衝突だけをエラーにする(ユーザー確認済み): あるファイル全体の実行順が
        // 未定義に戻ってしまうため。System単体の明示値同士が衝突しても「意図的な同着」でありうるため
        // 対象外(前段のsystemOrderByFqn側では重複チェックをしていない)。
        foreach(IGrouping<int,KeyValuePair<string,int>>Group in FileOrderByPath.GroupBy(static Kv=>Kv.Value)){
            List<string>FilePaths=[.. Group.Select(static Kv=>Kv.Key)];

            if(FilePaths.Count<2){
                continue;
            }

            FilePaths.Sort(StringComparer.Ordinal);

            for(var I=1;I<FilePaths.Count;I++){
                Diagnostics.Add(Diagnostic.Create(
                    GameSystemDiagnostics.DuplicateFileOrderValue,
                    Location.None,
                    FilePaths[0],
                    FilePaths[I],
                    Group.Key));
            }
        }

        return(new GameSystemOrderContext(SystemOrderByFqn,FileOrderByPath),Diagnostics.ToImmutable());
    }
}
