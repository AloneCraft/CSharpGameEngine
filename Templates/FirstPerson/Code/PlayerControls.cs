using CSharpGameEngine.Entities;
namespace FirstPerson;
/// <summary>操作を視点・移動・ジャンプへ翻訳する。視点が一人称でも三人称でもこのコードは変わらない(違いはシーンのカメラだけ)。</summary>
internal struct PlayerControls:ISystem {
    public void Update(in PlayerActions Input,ref ViewAngles View,ref MoveIntent Intent){
        View.Turn(Input.Look);
        Intent.Direction=View.Planar(Input.Move);
        if(Input.Jump.Pressed){
            Intent.Jump();
        }
    }
}
