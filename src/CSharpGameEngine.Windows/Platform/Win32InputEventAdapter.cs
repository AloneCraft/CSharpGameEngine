using System.Diagnostics;
using CSharpGameEngine.Core;
namespace CSharpGameEngine.Platform.Windows;
public sealed class Win32InputEventAdapter {
    private static readonly StableId DeviceNamespace=StableId.Parse("3d4419d8-ed0f-59e4-8dc4-88b27e8735eb");
    private readonly Func<MonotonicTimestamp>Clock;
    private readonly Dictionary<InputDeviceId,ulong>Sequences=[];
    private readonly Dictionary<(InputDeviceId DeviceId,InputControl Control),InputValue>Held=[];
    private readonly List<InputEvent>Pending=[];
    public Win32InputEventAdapter(WindowId WindowId,Func<MonotonicTimestamp>?Clock=null){
        if(!WindowId.IsValid){ throw new ArgumentException("A valid window ID is required.",nameof(WindowId)); }
        this.WindowId=WindowId;
        this.Clock=Clock??ReadTimestamp;
        this.KeyboardDevice=new InputDeviceId(StableId.FromName(DeviceNamespace,WindowId+"/keyboard"));
        this.MouseDevice=new InputDeviceId(StableId.FromName(DeviceNamespace,WindowId+"/mouse"));
        this.GamepadDevice=new InputDeviceId(StableId.FromName(DeviceNamespace,WindowId+"/gamepad0"));
    }
    /// <summary>Unreal key names for the XInput buttons, by XINPUT_GAMEPAD bit.</summary>
    private static readonly (ushort Mask,string Code)[]GamepadButtons=[
        (0x0001,"Gamepad_DPad_Up"),(0x0002,"Gamepad_DPad_Down"),(0x0004,"Gamepad_DPad_Left"),(0x0008,"Gamepad_DPad_Right"),
        (0x0010,"Gamepad_Special_Right"),(0x0020,"Gamepad_Special_Left"),(0x0040,"Gamepad_LeftThumbstick"),(0x0080,"Gamepad_RightThumbstick"),
        (0x0100,"Gamepad_LeftShoulder"),(0x0200,"Gamepad_RightShoulder"),
        (0x1000,"Gamepad_FaceButton_Bottom"),(0x2000,"Gamepad_FaceButton_Right"),(0x4000,"Gamepad_FaceButton_Left"),(0x8000,"Gamepad_FaceButton_Top"),
    ];
    public static InputControl GamepadLeftStick=>new(InputDeviceKind.Gamepad,"Gamepad_Left2D",InputControlKind.Axis2D);
    public static InputControl GamepadRightStick=>new(InputDeviceKind.Gamepad,"Gamepad_Right2D",InputControlKind.Axis2D);
    public static InputControl GamepadLeftTrigger=>new(InputDeviceKind.Gamepad,"Gamepad_LeftTriggerAxis",InputControlKind.Axis1D);
    public static InputControl GamepadRightTrigger=>new(InputDeviceKind.Gamepad,"Gamepad_RightTriggerAxis",InputControlKind.Axis1D);
    private GamepadSnapshot Gamepad;
    /// <summary>
    /// Turns a polled controller state into events: button presses/releases, stick and trigger value
    /// changes, and Connected/Disconnected transitions (a disconnect releases everything held).
    /// </summary>
    public void RecordGamepad(in GamepadSnapshot State){
        var Previous=this.Gamepad;
        this.Gamepad=State;
        if(!State.Connected){
            if(Previous.Connected){ this.ReleaseDevice(this.GamepadDevice,InputReleaseReason.DeviceRemoved,true); }
            return;
        }
        if(!Previous.Connected){ this.Enqueue(this.GamepadDevice,default,InputEventKind.Connected,InputValue.Zero); }
        foreach(var (Mask,Code) in GamepadButtons){
            var Down=(State.Buttons&Mask)!=0;
            if(Down!=((Previous.Buttons&Mask)!=0)){ this.RecordButton(this.GamepadDevice,new InputControl(InputDeviceKind.Gamepad,Code),Down); }
        }
        if(State.LeftStick!=Previous.LeftStick){ this.Enqueue(this.GamepadDevice,GamepadLeftStick,InputEventKind.ValueChanged,new InputValue(State.LeftStick.X,State.LeftStick.Y)); }
        if(State.RightStick!=Previous.RightStick){ this.Enqueue(this.GamepadDevice,GamepadRightStick,InputEventKind.ValueChanged,new InputValue(State.RightStick.X,State.RightStick.Y)); }
        if(State.LeftTrigger!=Previous.LeftTrigger){ this.Enqueue(this.GamepadDevice,GamepadLeftTrigger,InputEventKind.ValueChanged,new InputValue(State.LeftTrigger,0)); }
        if(State.RightTrigger!=Previous.RightTrigger){ this.Enqueue(this.GamepadDevice,GamepadRightTrigger,InputEventKind.ValueChanged,new InputValue(State.RightTrigger,0)); }
    }
    public static long TimestampFrequency=>Stopwatch.Frequency;
    public WindowId WindowId { get;}
    public InputDeviceId KeyboardDevice { get;}
    public InputDeviceId MouseDevice { get;}
    public InputDeviceId GamepadDevice { get;}
    public void RecordKey(int VirtualKeyCode,bool Pressed)=>this.RecordButton(this.KeyboardDevice,new InputControl(InputDeviceKind.Keyboard,KeyCode(VirtualKeyCode)),Pressed);
    public void RecordMouseButton(string Code,bool Pressed)=>this.RecordButton(this.MouseDevice,new InputControl(InputDeviceKind.Mouse,Code),Pressed);
    public void RecordMouseDelta(float DeltaX,float DeltaY){
        if(DeltaX==0&&DeltaY==0){ return; }
        this.Enqueue(this.MouseDevice,new InputControl(InputDeviceKind.Mouse,"Delta",InputControlKind.RelativeAxis2D),InputEventKind.ValueChanged,new InputValue(DeltaX,DeltaY));
    }
    public void RecordMouseWheel(float Delta)=>this.Enqueue(this.MouseDevice,new InputControl(InputDeviceKind.Mouse,"Wheel",InputControlKind.RelativeAxis1D),InputEventKind.ValueChanged,new InputValue(Delta,0));
    public void RecordFocusLost()=>this.ReleaseAll(InputReleaseReason.FocusLost);
    public void RecordCaptureReleased()=>this.ReleaseDevice(this.MouseDevice,InputReleaseReason.CaptureReleased,false);
    public void RecordDeviceRemoved()=>this.ReleaseAll(InputReleaseReason.DeviceRemoved,true);
    public void RecordWindowDestroyed()=>this.ReleaseAll(InputReleaseReason.WindowDestroyed);
    public IReadOnlyList<InputEvent>Drain(){
        if(this.Pending.Count==0){ return []; }
        var Result=this.Pending.ToArray();
        this.Pending.Clear();
        return Result;
    }
    public static string KeyCode(int VirtualKeyCode)=>VirtualKeyCode switch {
        0x25=>"Left",0x26=>"Up",0x27=>"Right",0x28=>"Down",0x70=>"F1",
        >=0x30 and <=0x39=>((char)VirtualKeyCode).ToString(),
        >=0x41 and <=0x5A=>((char)VirtualKeyCode).ToString(),
        _=>$"VK_{VirtualKeyCode:X2}",
    };
    private void RecordButton(InputDeviceId Device,InputControl Control,bool Pressed){
        var Key=(Device,Control);
        var WasPressed=this.Held.ContainsKey(Key);
        if(Pressed==WasPressed){ return; }
        if(Pressed){
            this.Held[Key]=InputValue.Pressed;
            this.Enqueue(Device,Control,InputEventKind.Pressed,InputValue.Pressed);
        } else {
            this.Held.Remove(Key);
            this.Enqueue(Device,Control,InputEventKind.Released,InputValue.Zero,InputReleaseReason.PhysicalRelease);
        }
    }
    private void ReleaseAll(InputReleaseReason Reason,bool Disconnect=false){
        this.ReleaseDevice(this.KeyboardDevice,Reason,Disconnect);
        this.ReleaseDevice(this.MouseDevice,Reason,Disconnect);
    }
    private void ReleaseDevice(InputDeviceId Device,InputReleaseReason Reason,bool Disconnect){
        foreach(var Pair in this.Held.Where(Pair=>Pair.Key.DeviceId==Device).OrderBy(static Pair=>Pair.Key.Control.Code,StringComparer.Ordinal).ToArray()){
            this.Held.Remove(Pair.Key);
            this.Enqueue(Device,Pair.Key.Control,InputEventKind.Released,InputValue.Zero,Reason);
        }
        if(Disconnect){ this.Enqueue(Device,default,InputEventKind.Disconnected,InputValue.Zero,Reason); }
    }
    private void Enqueue(InputDeviceId Device,InputControl Control,InputEventKind Kind,InputValue Value,InputReleaseReason Reason=InputReleaseReason.None){
        var Sequence=this.Sequences.TryGetValue(Device,out var Current)?checked(Current+1):1;
        this.Sequences[Device]=Sequence;
        this.Pending.Add(new InputEvent(this.Clock(),this.WindowId,Device,Sequence,Control,Kind,Value,Reason));
    }
    private static MonotonicTimestamp ReadTimestamp()=>new(Stopwatch.GetTimestamp());
}
