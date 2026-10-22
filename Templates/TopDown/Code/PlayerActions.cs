using CSharpGameEngine.Input;
namespace TopDown;
/// <summary>クリック(またはタッチ)した場所へ歩く操作。マウスとタッチは同じ「ポインター」として扱う。</summary>
[InputActions]
public partial struct PlayerActions {
    /// <summary>目的地の指定。押している間は<c>Held</c>がtrueで、カーソルを追って歩き続ける。</summary>
    [Mouse(MouseButton.Left)]
    [Touch]
    public Button SetDestination;
    /// <summary>画面上のポインター。<c>Ray</c>はカメラから画面の点を通るワールドの光線。</summary>
    [Pointer]
    public Pointer Cursor;
}
