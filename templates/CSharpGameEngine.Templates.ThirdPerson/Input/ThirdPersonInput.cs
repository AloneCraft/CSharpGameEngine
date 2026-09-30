using CSharpGameEngine.Core;
using CSharpGameEngine.Input;
using CSharpGameEngine.Platform.Windows;
namespace CgeThirdPerson.Input;
/// <summary>
/// UEのEnhanced Input(IMC_Default、IMC_MouseLook)の移植。キー名はUEの名前、
/// Mouse2Dはraw inputのマウス移動量(Delta)に対応する。modifierはUEと同じ順に適用される。
/// </summary>
public static class ThirdPersonInput {
    public static readonly InputContextId IMC_Default=new(StableId.Parse("bfad9c6a-b2c2-9058-bcf2-a03dd602d921"));
    public static readonly InputContextId IMC_MouseLook=new(StableId.Parse("6a14571b-a053-5859-8b5e-81f52a0b3f84"));
    public static readonly InputActionId IA_Jump=new(StableId.Parse("33e7e15f-f4c5-1c5c-9c3a-f5f238a4cc79"));
    public static readonly InputActionId IA_Look=new(StableId.Parse("dd0d0b0e-6200-8f50-8a6f-af040b6e672b"));
    public static readonly InputActionId IA_MouseLook=new(StableId.Parse("2a74551d-da21-7657-a1c8-95db3440a78f"));
    public static readonly InputActionId IA_Move=new(StableId.Parse("77270c59-5061-a955-886f-80331dab445c"));
    public static InputServer CreateServer(){
        static InputControl Key(string Code)=>new(InputDeviceKind.Keyboard,Code);
        static InputControl Button(string Code)=>new(InputDeviceKind.Gamepad,Code);
        var Server=new InputServer(new InputActionMap([
            new InputContext(IMC_Default,"IMC_Default",0,[
                new InputBinding(IA_Jump,Key("SpaceBar")),
                new InputBinding(IA_Jump,Button("Gamepad_FaceButton_Bottom")),
                new InputBinding(IA_Move,Key("W"),Modifiers:[new InputSwizzleModifier()]),
                new InputBinding(IA_Move,Key("S"),Modifiers:[new InputSwizzleModifier(),new InputNegateModifier()]),
                new InputBinding(IA_Move,Key("A"),Modifiers:[new InputNegateModifier()]),
                new InputBinding(IA_Move,Key("D")),
                new InputBinding(IA_Move,Key("Up"),Modifiers:[new InputSwizzleModifier()]),
                new InputBinding(IA_Move,Key("Down"),Modifiers:[new InputSwizzleModifier(),new InputNegateModifier()]),
                new InputBinding(IA_Move,Key("Right")),
                new InputBinding(IA_Move,Key("Left"),Modifiers:[new InputNegateModifier()]),
                new InputBinding(IA_Move,Win32InputEventAdapter.GamepadLeftStick,DeadZone:0.2f),
                new InputBinding(IA_Look,Win32InputEventAdapter.GamepadRightStick,DeadZone:0.2f),
            ]),
            new InputContext(IMC_MouseLook,"IMC_MouseLook",0,[
                new InputBinding(IA_MouseLook,new InputControl(InputDeviceKind.Mouse,"Delta",InputControlKind.RelativeAxis2D),Modifiers:[new InputNegateModifier(X:false,Y:true)]),
            ]),
        ]));
        Server.SetContextActive(IMC_Default,true,default);
        Server.SetContextActive(IMC_MouseLook,true,default);
        return Server;
    }
}
