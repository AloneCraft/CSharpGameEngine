using CSharpGameEngine.Core;
namespace CSharpGameEngine.Input;
/// <summary>
/// どの機器でも同じ、キーとゲームパッドのコントロールの名前(Unreal のキー名に合わせる)。[InputActions] から生成した
/// バインドはこれを使うので、Windows 以外(ADR-0101)でもそのままコンパイルできる。各機器の入力変換は同じ名前でイベントを出す。
/// </summary>
public static class InputControls {
    public static InputControl GamepadLeftStick=>new(InputDeviceKind.Gamepad,"Gamepad_Left2D",InputControlKind.Axis2D);
    public static InputControl GamepadRightStick=>new(InputDeviceKind.Gamepad,"Gamepad_Right2D",InputControlKind.Axis2D);
    public static InputControl GamepadLeftTrigger=>new(InputDeviceKind.Gamepad,"Gamepad_LeftTriggerAxis",InputControlKind.Axis1D);
    public static InputControl GamepadRightTrigger=>new(InputDeviceKind.Gamepad,"Gamepad_RightTriggerAxis",InputControlKind.Axis1D);
    /// <summary>
    /// スティックの1軸を個別のコントロールとして扱う（UnrealのGamepad_LeftX／Gamepad_LeftY／Gamepad_RightX／Gamepad_RightY、M0108）。長さでは
    /// なく軸ごとにデッドゾーンを適用する。
    /// </summary>
    public static InputControl GamepadLeftX=>new(InputDeviceKind.Gamepad,"Gamepad_LeftX",InputControlKind.Axis1D);
    public static InputControl GamepadLeftY=>new(InputDeviceKind.Gamepad,"Gamepad_LeftY",InputControlKind.Axis1D);
    public static InputControl GamepadRightX=>new(InputDeviceKind.Gamepad,"Gamepad_RightX",InputControlKind.Axis1D);
    public static InputControl GamepadRightY=>new(InputDeviceKind.Gamepad,"Gamepad_RightY",InputControlKind.Axis1D);
    /// <summary>文字、数字、名前付きキーのUnrealキー名。その他の仮想キーはVK_xx。キーは Windows の仮想キーの番号で指す。</summary>
    public static string KeyCode(int VirtualKeyCode)=>VirtualKeyCode switch {
        0x20=>"SpaceBar",0x25=>"Left",0x26=>"Up",0x27=>"Right",0x28=>"Down",0x70=>"F1",
        >=0x30 and <=0x39=>((char)VirtualKeyCode).ToString(),
        >=0x41 and <=0x5A=>((char)VirtualKeyCode).ToString(),
        _=>$"VK_{VirtualKeyCode:X2}",
    };
}
