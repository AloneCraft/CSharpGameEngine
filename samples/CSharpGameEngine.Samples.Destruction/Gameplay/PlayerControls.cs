using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Character;
namespace CgeDestructionSample;
/// <summary>The ThirdPerson template's BP_ThirdPersonCharacter DoMove/DoLook/DoJump: the controls turn into the view, the walk and the jump; the engine's CharacterMotion moves.</summary>
internal struct PlayerControls:ISystem {
    public void Update(in PlayerActions Input,ref ViewAngles View,ref MoveIntent Intent){
        View.Turn(Input.Look);
        Intent.Direction=View.Planar(Input.Move);
        if(Input.Jump.Pressed){
            Intent.Jump();
        }
    }
}
