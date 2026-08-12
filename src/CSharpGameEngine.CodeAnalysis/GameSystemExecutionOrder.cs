namespace CSharpGameEngine.CodeAnalysis;

/// <summary>
/// Systemの実行順序を決める唯一の比較関数。<see cref="GameSystemIndexAssigner"/>と
/// <see cref="GameSystemEntryPointEmitter"/>がそれぞれ独自にソートすると、2箇所が食い違ったときに
/// 「生成されたループの実行順」と「エントリポイント生成を妨げているContextの診断メッセージ」が
/// 指す順序がズレてしまう。両方がこの1箇所を呼ぶことでそれを防ぐ。
///
/// <para>
/// キーは4段: ①<c>[SystemOrder]</c>をSystem自身に直接付けた明示値(ADR-0036) ②
/// <c>[SystemOrder]</c>を<c>file</c>スコープのマーカーへ付けたファイル既定値(宣言が無いファイルは0)
/// ③<c>FilePath</c>のOrdinal順(ADR-0033からの既存の決定的タイブレーク、後方互換のため残す)
/// ④ファイル内の<c>DeclarationPosition</c>(ADR-0033決定事項1、常に記述順)。①はそのSystemだけの
/// 順序を、②はファイル内で自分の順序を持たない全Systemの既定値を表すため、①が優先される。
/// </para>
/// </summary>
internal static class GameSystemExecutionOrder {
    internal static int Compare(GameSystemResult A,GameSystemResult B,GameSystemOrderContext Context){
        var OrderA=OrderOf(A,Context);
        var OrderB=OrderOf(B,Context);
        var OrderCompare=OrderA.CompareTo(OrderB);

        if(OrderCompare!=0){
            return OrderCompare;
        }

        var FileCompare=string.CompareOrdinal(A.FilePath,B.FilePath);
        return FileCompare!=0?FileCompare:A.DeclarationPosition.CompareTo(B.DeclarationPosition);
    }

    private static int OrderOf(GameSystemResult System,GameSystemOrderContext Context){
        if(Context.SystemOrderByFqn.TryGetValue(System.ClassFullyQualifiedName,out var ExplicitOrder)){
            return ExplicitOrder;
        }

        return Context.FileOrderByPath.TryGetValue(System.FilePath,out var FileOrder)?FileOrder:0;
    }
}
