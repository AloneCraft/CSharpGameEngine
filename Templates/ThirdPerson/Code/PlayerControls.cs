using CSharpGameEngine.Entities;
namespace ThirdPerson;
/// <summary>操作を「やりたいこと」(視点・移動・ジャンプ)へ翻訳するだけ。実際の移動は物理側のCharacterMotionが行う。</summary>
internal struct PlayerControls:ISystem {
    public void Update(in PlayerActions Input,ref ViewAngles View,ref MoveIntent Intent){
        View.Turn(Input.Look);
        Intent.Direction=View.Planar(Input.Move);
        if(Input.Jump.Pressed){
            Intent.Jump();
        }
    }
}
