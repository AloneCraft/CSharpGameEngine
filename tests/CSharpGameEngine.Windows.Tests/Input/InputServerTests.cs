using CSharpGameEngine.Core;
using CSharpGameEngine.Input;
namespace CSharpGameEngine.Windows.Tests.Input;
public sealed class InputServerTests {
    private static readonly WindowId Window=new(StableId.Parse("35177e2f-4d15-4e5e-8a79-cc7cc65ad2a0"));
    private static readonly InputDeviceId Keyboard=new(StableId.Parse("568d0766-0f20-48a0-b632-cd1ab6d07778"));
    private static readonly InputContextId Context=new(StableId.Parse("29a45747-ae2b-49dc-8926-6f03784d0d48"));
    private static readonly InputActionId Action=new(StableId.Parse("b22ac9df-06c5-41ca-9b89-dbca9d4c8e26"));
    private static readonly InputControl Space=new(InputDeviceKind.Keyboard,"Space",InputControlKind.Button);
    [Fact]
    public void SameTrace_ProducesSameActionSequence(){
        var First=RunTrace();
        var Second=RunTrace();
        Assert.Equal(First,Second);
        Assert.Equal(InputServer.ComputeActionHash(First),InputServer.ComputeActionHash(Second));
        Assert.Collection(First,Value=>Assert.Equal(InputActionPhase.Started,Value.Phase),Value=>Assert.Equal(InputActionPhase.Canceled,Value.Phase));
    }
    [Fact]
    public void ContextPriority_HighestMatchingContextConsumesControl(){
        var LowerContext=new InputContextId(StableId.Parse("7362d43f-c518-4727-a72f-670ed09c4917"));
        var LowerAction=new InputActionId(StableId.Parse("fd58d501-7e71-4751-b023-e666d664b0bb"));
        var Map=new InputActionMap([
            new InputContext(LowerContext,"Lower",1,[new InputBinding(LowerAction,Space)]),
            new InputContext(Context,"Higher",10,[new InputBinding(Action,Space)]),
        ]);
        var Server=new InputServer(Map);
        Server.SetContextActive(LowerContext,true,new MonotonicTimestamp(1));
        Server.SetContextActive(Context,true,new MonotonicTimestamp(1));
        Server.ProcessEvents([Event(2,1,Space,InputEventKind.Pressed,InputValue.Pressed)]);
        var Value=Assert.Single(Server.CurrentFrame);
        Assert.Equal(Action,Value.ActionId);
        Assert.Equal(Context,Value.ContextId);
    }
    [Fact]
    public void ActivatingHigherPriorityContext_CancelsShadowedAction(){
        var LowerContext=new InputContextId(StableId.Parse("7362d43f-c518-4727-a72f-670ed09c4917"));
        var LowerAction=new InputActionId(StableId.Parse("fd58d501-7e71-4751-b023-e666d664b0bb"));
        var Server=new InputServer(new InputActionMap([
            new InputContext(LowerContext,"Lower",1,[new InputBinding(LowerAction,Space)]),
            new InputContext(Context,"Higher",10,[new InputBinding(Action,Space)]),
        ]));
        Server.SetContextActive(LowerContext,true,new MonotonicTimestamp(1));
        Server.ProcessEvents([Event(2,1,Space,InputEventKind.Pressed,InputValue.Pressed)]);
        Assert.Equal(LowerAction,Assert.Single(Server.CurrentFrame).ActionId);
        Server.SetContextActive(Context,true,new MonotonicTimestamp(3));
        var Canceled=Assert.Single(Server.CurrentFrame);
        Assert.Equal(LowerAction,Canceled.ActionId);
        Assert.Equal(InputActionPhase.Canceled,Canceled.Phase);
        Assert.Equal(InputReleaseReason.ContextChanged,Canceled.ReleaseReason);
    }
    [Fact]
    public void ChordAndContext_AreRequiredAndContextRemovalCancels(){
        var Shift=new InputControl(InputDeviceKind.Keyboard,"Shift",InputControlKind.Button);
        var Map=new InputActionMap([new InputContext(Context,"Showcase",10,[new InputBinding(Action,Space,[Shift])])]);
        var Server=new InputServer(Map);
        Server.SetContextActive(Context,true,new MonotonicTimestamp(1));
        Server.ProcessEvents([Event(2,1,Space,InputEventKind.Pressed,InputValue.Pressed)]);
        Assert.Empty(Server.CurrentFrame);
        Server.ProcessEvents([Event(3,2,Shift,InputEventKind.Pressed,InputValue.Pressed),Event(4,3,Space,InputEventKind.Pressed,InputValue.Pressed)]);
        Assert.Contains(Server.CurrentFrame,Value=>Value.ActionId==Action&&Value.Phase==InputActionPhase.Started);
        Server.SetContextActive(Context,false,new MonotonicTimestamp(5));
        Assert.Contains(Server.CurrentFrame,Value=>Value.ActionId==Action&&Value.Phase==InputActionPhase.Canceled&&Value.ReleaseReason==InputReleaseReason.ContextChanged);
    }
    [Fact]
    public void DeadZoneAndRebindCollision_AreDeterministic(){
        var AxisAction=new InputActionId(StableId.Parse("c38ff90f-c434-4a94-9583-791cbd422c66"));
        var Axis=new InputControl(InputDeviceKind.Gamepad,"LeftX",InputControlKind.Axis1D);
        var Map=new InputActionMap([new InputContext(Context,"Showcase",10,[new InputBinding(Action,Space),new InputBinding(AxisAction,Axis,DeadZone:0.25f)])]);
        var Server=new InputServer(Map);
        Server.SetContextActive(Context,true,new MonotonicTimestamp(1));
        Server.ProcessEvents([new InputEvent(new MonotonicTimestamp(2),Window,Keyboard,1,Axis,InputEventKind.ValueChanged,new InputValue(0.2f,0))]);
        Assert.Empty(Server.CurrentFrame);
        Server.ProcessEvents([new InputEvent(new MonotonicTimestamp(3),Window,Keyboard,2,Axis,InputEventKind.ValueChanged,new InputValue(0.5f,0))]);
        Assert.Equal(1f/3f,Assert.Single(Server.CurrentFrame).Value.X,5);
        Assert.False(Server.TryRebind(Context,AxisAction,Space,out var Error));
        Assert.Contains("collision",Error,StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public void TwoDimensionalDeadZone_IsRadialAndReservedActionsCannotRebind(){
        var LookAction=new InputActionId(StableId.Parse("77e94279-e9ab-4fae-94ab-ed4a37d8265c"));
        var Look=new InputControl(InputDeviceKind.Gamepad,"Look",InputControlKind.Axis2D);
        var Map=new InputActionMap([new InputContext(Context,"Showcase",10,[new InputBinding(LookAction,Look,DeadZone:0.5f,Reserved:true)])],new HashSet<InputActionId>{LookAction});
        var Server=new InputServer(Map);
        Server.SetContextActive(Context,true,new MonotonicTimestamp(1));
        Server.ProcessEvents([new InputEvent(new MonotonicTimestamp(2),Window,Keyboard,1,Look,InputEventKind.ValueChanged,new InputValue(0.3f,0.4f))]);
        Assert.Empty(Server.CurrentFrame);
        Server.ProcessEvents([new InputEvent(new MonotonicTimestamp(3),Window,Keyboard,2,Look,InputEventKind.ValueChanged,new InputValue(0.6f,0.8f))]);
        var Value=Assert.Single(Server.CurrentFrame).Value;
        Assert.Equal(0.6f,Value.X,5);
        Assert.Equal(0.8f,Value.Y,5);
        Assert.False(Server.TryRebind(Context,LookAction,Space,out var Error));
        Assert.Contains("reserved",Error,StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public void DeviceRemoval_CancelsHeldActions(){
        var Server=CreateServer();
        Server.ProcessEvents([Event(2,1,Space,InputEventKind.Pressed,InputValue.Pressed)]);
        Server.ProcessEvents([new InputEvent(new MonotonicTimestamp(3),Window,Keyboard,2,default,InputEventKind.Disconnected,InputValue.Zero,InputReleaseReason.DeviceRemoved)]);
        var Value=Assert.Single(Server.CurrentFrame);
        Assert.Equal(InputActionPhase.Canceled,Value.Phase);
        Assert.Equal(InputReleaseReason.DeviceRemoved,Value.ReleaseReason);
    }
    private static InputActionValue[]RunTrace(){
        var Server=CreateServer();
        var Results=new List<InputActionValue>();
        Server.ProcessEvents([Event(2,1,Space,InputEventKind.Pressed,InputValue.Pressed)]);Results.AddRange(Server.CurrentFrame);
        Server.ProcessEvents([Event(3,2,Space,InputEventKind.Released,InputValue.Zero)]);Results.AddRange(Server.CurrentFrame);
        return [.. Results];
    }
    private static InputServer CreateServer(){
        var Server=new InputServer(new InputActionMap([new InputContext(Context,"Showcase",10,[new InputBinding(Action,Space)])]));
        Server.SetContextActive(Context,true,new MonotonicTimestamp(1));
        return Server;
    }
    private static InputEvent Event(long Tick,ulong Sequence,InputControl Control,InputEventKind Kind,InputValue Value)=>new(new MonotonicTimestamp(Tick),Window,Keyboard,Sequence,Control,Kind,Value);
}
