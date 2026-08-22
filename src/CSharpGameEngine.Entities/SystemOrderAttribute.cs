namespace CSharpGameEngine.Entities;

/// <summary>
/// Systemの実行順序はソース上の記述順が基本(ADR-0033決定事項1)だが、複数ファイルにまたがった
/// 場合はファイルパスのOrdinal順という開発者が制御できない基準にフォールバックしていた
/// (ADR-0033未解決事項)。この属性は、ソースコード間の実行順序を宣言的に指定する唯一の手段で、
/// 2通りの付け方ができる:
///
/// <list type="number">
/// <item>
/// <c>ISystem</c>実装struct自身に直接付ける — そのSystem<b>だけ</b>の順序を指定する
/// (最も細かい粒度)。
/// <code>
/// [SystemOrder(0)]
/// internal struct FooSystem : ISystem { ... }
/// </code>
/// </item>
/// <item>
/// <c>file</c>スコープのマーカークラスに付ける — そのファイルに書かれた、自分自身の順序を
/// 持たない全Systemの既定順序になる(ファイル単位のまとめ指定)。
/// <code>
/// [SystemOrder(0)]
/// file sealed class Order;
/// </code>
/// </item>
/// </list>
///
/// 同じSystemに両方が影響し得る場合は①が優先される。どちらも無ければ既定値0で、既存の
/// ファイルパスOrdinal順というタイブレークにフォールバックする(後方互換)。
///
/// <para>
/// 属性を一切使わない構造的推論という設計(ADR-0033決定事項2)からの意図的な例外である(ADR-0036)。
/// Component/SystemのComponent集合やロールはそれ自体の構造(ref-kind等)から判別できるが、
/// 「複数ファイル・複数Systemの相対順序」はどの構造からも導出できない情報であり、宣言的に指定する
/// 以外に手段が無い。
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Struct|AttributeTargets.Class,AllowMultiple=false,Inherited=false)]
public sealed class SystemOrderAttribute(int Order):Attribute {
    public int Order { get; }=Order;
}
