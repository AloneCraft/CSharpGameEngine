using CSharpGameEngine.Input;
namespace CgeTopDown;
/// <summary>
/// UEのEnhanced Input(IA_SetDestination_Click)に相当する操作の宣言。入力マップ・ID・据え付けはSource Generatorが作り、
/// 値は毎フレーム、この構造体を<c>in</c>で受けたSystemへ渡される。
/// </summary>
[InputActions]
public partial struct PlayerActions {
    /// <summary>目的地の指定。マウスの左ボタン。押している間は<c>Held</c>がtrueで、カーソルを追って歩き続ける。</summary>
    [Mouse(MouseButton.Left)]
    public Button SetDestination;
    /// <summary>画面上のポインター。クライアント領域の割合で、3Dの位置は<c>Picking</c>サービスが求める。</summary>
    [Pointer]
    public Pointer Cursor;
}
