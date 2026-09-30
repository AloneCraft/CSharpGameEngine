using System.Diagnostics;
using CSharpGameEngine.Core;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Input;
using CSharpGameEngine.Runtime.Character;
using CSharpGameEngine.Runtime.Windows;
using CgeThirdPerson.Input;
namespace CgeThirdPerson.Gameplay;
/// <summary>
/// BP_ThirdPersonCharacterのDoMove/DoLook/DoJump相当。入力マップを据え付け、マウスを捕捉し、毎フレーム
/// 視点入力で制御回転を回し、移動入力を制御回転のYaw基準のワールド方向にしてCharacterInputへ渡す。
/// 視点のY入力は正で見上げる(マウスはIMC_MouseLookのNegate Yで上移動が正になる)。
/// </summary>
[SystemPhase(SystemPhase.FrameInput)]
[SystemOrder(2)]
internal struct ThirdPersonControls:ISystem {
    /// <summary>
    /// マウス1カウントあたりの回転角。UEの既定値(MouseX/YのAxisConfig感度0.07と、旧来の入力スケール2.5)の
    /// 積から推定した値で、UE上での実測ではない。
    /// </summary>
    private const float MouseDegreesPerCount=0.175f;
    /// <summary>スティック全倒しの回転速度。UEは毎フレーム2.5°(60 Hzで150°/s)を足すので、その60 Hz相当を時間基準にした。</summary>
    private const float GamepadDegreesPerSecond=150f;
    private bool InputInstalled;
    private long LastTimestamp;
    public bool Update(ref PlayerController Controller,ref CharacterInput Input,EngineHost Host,ref State State){
        if(!this.InputInstalled){
            Host.SetInput(ThirdPersonInput.CreateServer());
            this.InputInstalled=true;
            this.LastTimestamp=Stopwatch.GetTimestamp();
            return true;
        }
        // An automation run never captures the user's cursor.
        if(Host.Automation is null&&Host.Window is{ IsMouseCaptured:false }Window){
            Window.SetMouseCaptured(true);
        }
        var Now=Stopwatch.GetTimestamp();
        var DeltaSeconds=Math.Min((Now-this.LastTimestamp)/(float)Stopwatch.Frequency,0.1f);
        this.LastTimestamp=Now;
        var Server=Host.Input;
        var Mouse=Server.ReadValue(ThirdPersonInput.IA_MouseLook);
        var Stick=Server.ReadValue(ThirdPersonInput.IA_Look);
        PlayerControl.AddLook(ref Controller,
            Mouse.X*MouseDegreesPerCount+Stick.X*GamepadDegreesPerSecond*DeltaSeconds,
            Mouse.Y*MouseDegreesPerCount+Stick.Y*GamepadDegreesPerSecond*DeltaSeconds);
        var Move=Server.ReadValue(ThirdPersonInput.IA_Move);
        Input.Move=PlayerControl.MoveDirection(Controller,Move.X,Move.Y);
        foreach(var Action in Server.CurrentFrame){
            if(Action.ActionId==ThirdPersonInput.IA_Jump&&Action.Phase==InputActionPhase.Started){
                Input.Jump=true;
            }
        }
        return true;
    }
}
