namespace CSharpGameEngine.Cooking;
/// <summary>
/// プラグインの Cook モジュールが足す、取り込みとベイクの処理(ADR-0079)。1 つの入力ファイルから、出力フォルダへファイルを書く。
/// 同じ入力からは同じバイト列を書く(決定的)。<c>CSharpGameEngine.Cook.Cli cook --verify</c> は 2 回書いて比べる。
/// 実装は公開の、引数なしのコンストラクターを持つクラスにする。
/// </summary>
public interface IAssetCooker {
    /// <summary>道具が表示し、<c>--cooker</c> で選ぶ名前。</summary>
    string Name { get; }
    /// <summary>読む入力の拡張子(小文字、点つき。例: <c>.json</c>)。</summary>
    IReadOnlyList<string> InputExtensions { get; }
    CookResult Cook(CookRequest Request);
}
/// <summary>1 回の調理の入力。</summary>
public sealed class CookRequest(string InputPath,string OutputDirectory,string ProjectDirectory) {
    /// <summary>入力ファイルの絶対パス。</summary>
    public string InputPath { get; }=InputPath;
    /// <summary>出力を書くフォルダ(在る)。この外には書かない。</summary>
    public string OutputDirectory { get; }=OutputDirectory;
    /// <summary>ゲームのプロジェクトのフォルダ。入力が参照するほかのファイルを探すのに使う。</summary>
    public string ProjectDirectory { get; }=ProjectDirectory;
}
/// <summary>1 回の調理の結果。<see cref="Outputs"/> は <see cref="CookRequest.OutputDirectory"/> からの相対パス。</summary>
public sealed class CookResult(bool Succeeded,IReadOnlyList<string> Outputs,IReadOnlyList<string> Messages) {
    public bool Succeeded { get; }=Succeeded;
    public IReadOnlyList<string> Outputs { get; }=Outputs;
    public IReadOnlyList<string> Messages { get; }=Messages;
    public static CookResult Success(params string[] Outputs)=>new(true,Outputs,[]);
    public static CookResult Failure(params string[] Messages)=>new(false,[],Messages);
}
