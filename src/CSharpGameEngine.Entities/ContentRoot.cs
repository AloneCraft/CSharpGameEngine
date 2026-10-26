namespace CSharpGameEngine.Entities;
/// <summary>
/// ランタイムが相対パスのコンテンツ（メッシュ、スケルタルアセット、コリジョンメッシュ、シェーダーアーティファクト）を解決する基準ディレクトリ。
/// ゲーム自身のディレクトリを指す。Scene Designerのランタイムビューはプレビューホスト内でゲームを実行し、表示中のゲームビルドを指定する
///（ADR-0070 B1）。読み込まれたゲームアセンブリごとに個別のルートを持つ。
/// </summary>
public static class ContentRoot {
    public static string Directory { get;private set; }=AppContext.BaseDirectory;

    public static string Resolve(string RelativePath)=>Path.GetFullPath(Path.Combine(Directory,RelativePath));

    public static void Use(string Directory){
        ArgumentException.ThrowIfNullOrWhiteSpace(Directory);
        ContentRoot.Directory=Path.GetFullPath(Directory);
    }
}
