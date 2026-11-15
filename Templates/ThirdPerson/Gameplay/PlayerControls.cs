using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Character;
namespace CgeThirdPerson;
/// <summary>
/// BP_ThirdPersonCharacterのDoMove/DoLook/DoJump相当。操作を視点・移動・ジャンプへ翻訳するだけで、実際の移動は
/// エンジンのCharacterMotionが行う。入力を読むので、エンジンのWindowの後のFrameInputで動く(属性は要らない)。
/// </summary>
internal struct PlayerControls:ISystem {
    public void Update(in PlayerActions Input,ref ViewAngles View,ref MoveIntent Intent){
        View.Turn(Input.Look);
        Intent.Direction=View.Planar(Input.Move);
        if(Input.Jump.Pressed){
            Intent.Jump();
        }
    }
}
