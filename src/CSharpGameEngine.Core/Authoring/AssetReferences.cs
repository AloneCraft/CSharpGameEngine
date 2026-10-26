namespace CSharpGameEngine.Authoring;
/// <summary>
/// 型付きのアセット参照(ADR-0077)。<c>Assets/</c>の下のファイルごとに、Source Generatorが<c>Assets.Sounds.Jump</c>のような
/// 静的メンバーを作る。ファイルが無い・名前を打ち間違えるとコンパイルエラーになり、サウンドを渡すところにモデルは渡せない。
/// 中身はプロジェクト相対のパス(<c>Assets/Sounds/Jump.wav</c>)で、文字列への暗黙の変換を持つ。
/// </summary>
public readonly struct SoundAsset(string Path) {
    public readonly string Path=Path;
    public static implicit operator string(SoundAsset Asset)=>Asset.Path;
    public override string ToString()=>this.Path;
}
/// <summary>メッシュ・骨格・アニメーションのクリップ(<c>.glb</c>/<c>.gltf</c>/<c>.fbx</c>)。</summary>
public readonly struct ModelAsset(string Path) {
    public readonly string Path=Path;
    public static implicit operator string(ModelAsset Asset)=>Asset.Path;
    public override string ToString()=>this.Path;
}
/// <summary>画像(<c>.png</c>/<c>.jpg</c>)。</summary>
public readonly struct TextureAsset(string Path) {
    public readonly string Path=Path;
    public static implicit operator string(TextureAsset Asset)=>Asset.Path;
    public override string ToString()=>this.Path;
}

/// <summary>GPUテクスチャとは別個の、エンコード済みソース画像出力。</summary>
public readonly struct ImageAsset(string Path) {
    public readonly string Path=Path;
    public override string ToString()=>this.Path;
}
/// <summary>焼いたフォント(<c>.cgefont</c>。<c>tools/CSharpGameEngine.FontBaker</c> が TrueType から作る)。<c>ScreenText.Font</c> に渡すパスになる(ADR-0078/M0104)。</summary>
public readonly struct FontAsset(string Path) {
    public readonly string Path=Path;
    public static implicit operator string(FontAsset Asset)=>Asset.Path;
    public override string ToString()=>this.Path;
}
