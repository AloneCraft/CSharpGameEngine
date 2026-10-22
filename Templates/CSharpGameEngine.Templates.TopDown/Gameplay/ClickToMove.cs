using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Character;
using CSharpGameEngine.Runtime.Windows;
namespace CgeTopDown;
/// <summary>
/// BP_TopDownControllerのクリック移動に相当する。押されている間、カーソルの下の地面を目的地にする。
/// 歩く処理(減速して止まるところまで)はエンジンのCharacterMotionが<c>MoveIntent.MoveTo</c>の目的地に向けて行う。
/// UEはナビメッシュで経路を探すが、ここは障害物をコリジョンで避けるだけの直線の移動。
/// </summary>
internal struct ClickToMove:ISystem {
    public void Update(in PlayerActions Input,ref MoveIntent Intent,Picking Picking){
        if(Input.SetDestination.Held&&Picking.TryPick(Input.Cursor,out var Hit)){
            Intent.MoveTo(Hit.Point);
        }
    }
}
