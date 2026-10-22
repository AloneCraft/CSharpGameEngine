using CgeTopDown.Scenes;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Character;
using CSharpGameEngine.Runtime.Windows;
namespace CgeTopDown;
/// <summary>
/// BP_TopDownControllerのクリック移動に相当する。押されている間、カーソルの下の地面を目的地にする。押した瞬間には、その地点に目印
/// (<c>FX_Cursor_Success</c>に当たる<see cref="CursorMarker"/>シーン。0.7秒で自分で消える)を出す。
/// 歩く処理(減速して止まるところまで)はエンジンのCharacterMotionが<c>MoveIntent.MoveTo</c>の目的地に向けて行う。
/// UEはナビメッシュで経路を探し、ここでもNavigationプラグイン(<c>Scenes/TopDownNavigation.json</c>の<c>NavMeshBounds</c>で焼いたナビメッシュと、キャラクターの<c>NavAgent</c>)が、
/// <c>MoveTo</c>の目的地への経路(台のスロープを下り、壁の端を回る)に沿って歩かせる。コードは目的地を渡すだけ。UEは目印を、押していた時間が短かったときの離した瞬間に出す
/// (押していた時間を測る入力は<c>M0108</c>)。ここでは押した瞬間に出す。
/// </summary>
internal struct ClickToMove:ISystem {
    public void Update(in PlayerActions Input,ref MoveIntent Intent,Picking Picking,Spawner Spawner){
        if(Input.SetDestination.Held&&Picking.TryPick(Input.Cursor,out var Hit)){
            Intent.MoveTo(Hit.Point);
            if(Input.SetDestination.Pressed){
                Spawner.Spawn<CursorMarker>(Hit.Point);
            }
        }
    }
}
