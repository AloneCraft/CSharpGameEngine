namespace CSharpGameEngine.Rendering;

/// <summary>同一のレンダーワールドスナップショットを、プロバイダーに依存せず表示するビュー。表示用の派生状態であり、シミュレーションの正本データではない。</summary>
public enum RenderPresentationMode:byte {
    Lit,
    BaseColor,
    Normal,
    Wireframe,
}
