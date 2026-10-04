using System.Numerics;
using System.Runtime.InteropServices;
namespace CSharpGameEngine.Platform.Windows;
/// <summary>One XInput controller's state, normalized: sticks and triggers in [-1, 1] / [0, 1], Y up.</summary>
public readonly record struct GamepadSnapshot(bool Connected,ushort Buttons,float LeftTrigger,float RightTrigger,Vector2 LeftStick,Vector2 RightStick);
/// <summary>
/// Polls the first XInput controller (ADR-0067). Dead zones are left to the input bindings, as in
/// Unreal's Enhanced Input; xinput1_4.dll being absent reads as disconnected.
/// </summary>
public static partial class XInputGamepad {
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeGamepad {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short ThumbLeftX;
        public short ThumbLeftY;
        public short ThumbRightX;
        public short ThumbRightY;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeState {
        public uint PacketNumber;
        public NativeGamepad Gamepad;
    }
    [LibraryImport("xinput1_4.dll",EntryPoint="XInputGetState")]
    private static partial uint GetState(uint UserIndex,out NativeState State);
    private static bool Unavailable;
    public static GamepadSnapshot Poll(uint UserIndex=0){
        if(Unavailable){
            return default;
        }
        try {
            if(GetState(UserIndex,out var State)!=0){
                return default;
            }
            var Pad=State.Gamepad;
            return new GamepadSnapshot(true,Pad.Buttons,Pad.LeftTrigger/255f,Pad.RightTrigger/255f,
                new Vector2(Axis(Pad.ThumbLeftX),Axis(Pad.ThumbLeftY)),new Vector2(Axis(Pad.ThumbRightX),Axis(Pad.ThumbRightY)));
        }
        catch(DllNotFoundException){
            Unavailable=true;
            return default;
        }
    }
    private static float Axis(short Value)=>Math.Clamp(Value/32767f,-1f,1f);
}
