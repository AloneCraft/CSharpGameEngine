using CgeTopDown.Scenes;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Numerics;
using CSharpGameEngine.Runtime.Character;
using CSharpGameEngine.Runtime.Windows;
namespace CgeTopDown;
/// <summary>BP_TopDownControllerの<c>CachedDestination</c>。最後にクリックの下で見つけた地面の位置で、クリックを離したときの短押しの目的地になる。キャラクターのEntityに付ける。</summary>
public struct ClickDestination {
    public LogicalVector3 Point;
    /// <summary>クリックの下に地面を見つけたことがあるか(UEの<c>CachedDestination</c>は(0,0,0)で始まるが、ここは何も無いところへは歩かせない)。</summary>
    public bool Found;
}
/// <summary>
/// BP_TopDownControllerの<c>IA_SetDestination_Click</c>に相当する。UEと同じく押している時間で二通りに分ける。
/// 押した瞬間(Started): 歩きを止め(<c>StopMovement</c>)、カーソルの下の地面を<see cref="ClickDestination"/>に覚える。
/// 押している間(Triggered): カーソルの下の地面を覚え直し、その向きへ直接歩く(<c>Follow</c> = <c>AddMovementInput(GetDirectionUnitVector(自分の位置, 位置), 1)</c>。
/// 3次元の単位ベクトルなので、地面が足元に近いほど水平の入力は小さくなる)。
/// 離した瞬間(Completed/Canceled): 押していた時間(<see cref="Button.HeldSeconds"/>)が<see cref="PressedThreshold"/>より短ければ(短押し)、覚えた地面へ
/// <c>MoveTo</c>(<c>SimpleMoveToLocation</c>)で歩き、その地点に目印(<c>FX_Cursor_Success</c>に当たる<see cref="CursorMarker"/>シーン。0.7秒で自分で消える)を出す。
/// 長押しなら何もせず、直接歩くのをやめて止まる。
/// 経路で歩く処理(減速して止まるところまで)はエンジンのCharacterMotionが<c>MoveIntent.MoveTo</c>の目的地に向けて行う。ナビメッシュを焼いたレベルでは、
/// Navigationプラグイン(<c>Scenes/TopDownNavigation.json</c>の<c>NavMeshBounds</c>と、キャラクターの<c>NavAgent</c>)が台のスロープを下り、壁の端を回る経路に沿って歩かせる。
/// </summary>
internal struct ClickToMove:ISystem {
    /// <summary><c>PressedThreshold</c>(秒)。これ未満で離したら短押し。ちょうどこの長さなら長押し(UEの<c>Less</c>は厳密な比較)。</summary>
    public const float PressedThreshold=0.5f;
    public void Update(in PlayerActions Input,ref MoveIntent Intent,ref ClickDestination Cached,in Transform Body,Picking Picking,Spawner Spawner){
        var Click=Input.SetDestination;
        var Found=Picking.TryPick(Input.Cursor,out var Hit);
        if(Click.Pressed){
            Intent.Stop();
            if(Found){
                Cached.Point=Hit.Point;
                Cached.Found=true;
            }
        }
        if(Click.Held){
            // 地面が見つからないフレームは入力を足さない(UEのTriggeredはヒットがあるときだけ動く)。
            Intent.Direction=default;
            if(Found){
                Cached.Point=Hit.Point;
                Cached.Found=true;
                if((Cached.Point-Body.Position).TryNormalize(out var Direction)){
                    Intent.Direction=Direction;
                }
            }
        }
        if(Click.Released){
            if(Click.ReleasedBefore(PressedThreshold)&&Cached.Found){
                Intent.MoveTo(Cached.Point);
                Spawner.Spawn<CursorMarker>(Cached.Point);
            } else {
                Intent.Stop();
            }
        }
    }
}
