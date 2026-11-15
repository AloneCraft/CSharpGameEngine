using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Character;
namespace CgeFirstPerson;
/// <summary>
/// BP_FirstPersonCharacterのDoMove/DoLook/DoJump相当。操作を視点・移動・ジャンプへ翻訳するだけで、実際の移動はエンジンの
/// CharacterMotionが行う。ThirdPersonと同じ翻訳に加えて、UEのbUseControllerRotationYawに当たる、体の向きを視点のYawに合わせる処理がある
/// (体の向きは、体のメッシュの影と、アニメーションの向きの角度を決める。カメラの向きは視点そのもので、体の向きとは別)。
/// </summary>
internal struct PlayerControls:ISystem {
    public void Update(in PlayerActions Input,ref ViewAngles View,ref MoveIntent Intent,ref Transform Body){
        View.Turn(Input.Look);
        Intent.Direction=View.Planar(Input.Move);
        if(Input.Jump.Pressed){
            Intent.Jump();
        }
        Body.Rotation=PlayerControl.YawRotation(View);
    }
}
