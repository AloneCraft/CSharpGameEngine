using CSharpGameEngine.Core;
using CSharpGameEngine.Contracts;
using System.Security.Cryptography;
using System.Text;
namespace CSharpGameEngine.Input;
/// <summary>
/// An Unreal Enhanced Input style value modifier, applied in order after the binding's dead zone
/// and scale.
/// </summary>
public abstract record InputModifier {
    public abstract InputValue Apply(InputValue Value);
}
/// <summary>Negates the selected axes.</summary>
public sealed record InputNegateModifier(bool X=true,bool Y=true):InputModifier {
    public override InputValue Apply(InputValue Value)=>new(this.X?-Value.X:Value.X,this.Y?-Value.Y:Value.Y);
}
/// <summary>Swaps X and Y (Unreal's Swizzle Input Axis Values, order YXZ), e.g. a key driving the forward axis.</summary>
public sealed record InputSwizzleModifier:InputModifier {
    public override InputValue Apply(InputValue Value)=>new(Value.Y,Value.X);
}
public sealed record InputBinding(InputActionId ActionId,InputControl Control,IReadOnlyList<InputControl>?Chord=null,float DeadZone=0,float Scale=1,bool Reserved=false,IReadOnlyList<InputModifier>?Modifiers=null) {
    public IReadOnlyList<InputControl>RequiredControls=>this.Chord??[];
    public IReadOnlyList<InputModifier>ValueModifiers=>this.Modifiers??[];
}
public sealed record InputContext(InputContextId Id,string Name,int Priority,IReadOnlyList<InputBinding>Bindings);
public sealed class InputActionMap(IReadOnlyList<InputContext>Contexts,IReadOnlySet<InputActionId>?ReservedActions=null) {
    public IReadOnlyList<InputContext>Contexts { get;}=Contexts;
    public IReadOnlySet<InputActionId>ReservedActions { get;}=ReservedActions??new HashSet<InputActionId>();
}
public sealed class InputServer {
    private readonly Dictionary<InputContextId,InputContext>Contexts;
    private readonly HashSet<InputContextId>ActiveContexts=[];
    private readonly Dictionary<(InputDeviceId DeviceId,InputControl Control),InputValue>ControlValues=[];
    private readonly Dictionary<InputActionId,(InputContextId ContextId,InputDeviceId DeviceId,InputValue RawValue,InputValue Value)>ActiveActions=[];
    private readonly HashSet<InputActionId>ReservedActions;
    private readonly List<InputActionValue>Frame=[];
    private readonly Dictionary<InputControl,InputValue>RelativeFrame=[];
    public InputServer(InputActionMap Map){
        ArgumentNullException.ThrowIfNull(Map);
        this.Contexts=Map.Contexts.ToDictionary(static Context=>Context.Id);
        this.ReservedActions=[.. Map.ReservedActions];
        if(this.Contexts.Count!=Map.Contexts.Count){ throw new ArgumentException("Input context IDs must be unique.",nameof(Map)); }
    }
    public IReadOnlyList<InputActionValue>CurrentFrame=>this.Frame;
    public static string ComputeActionHash(IEnumerable<InputActionValue>Values){
        ArgumentNullException.ThrowIfNull(Values);
        using var Stream=new MemoryStream();
        using(var Writer=new BinaryWriter(Stream,new UTF8Encoding(false),true)){
            foreach(var Value in Values){
                Writer.Write(Value.Timestamp.Ticks);
                WriteStableId(Writer,Value.WindowId.Value);
                WriteStableId(Writer,Value.DeviceId.Value);
                Writer.Write(Value.Sequence);
                WriteStableId(Writer,Value.ContextId.Value);
                WriteStableId(Writer,Value.ActionId.Value);
                Writer.Write((int)Value.Phase);
                Writer.Write(BitConverter.SingleToInt32Bits(Value.RawValue.X));
                Writer.Write(BitConverter.SingleToInt32Bits(Value.RawValue.Y));
                Writer.Write(BitConverter.SingleToInt32Bits(Value.Value.X));
                Writer.Write(BitConverter.SingleToInt32Bits(Value.Value.Y));
                Writer.Write((int)Value.ReleaseReason);
            }
        }
        return Convert.ToHexString(SHA256.HashData(Stream.GetBuffer().AsSpan(0,checked((int)Stream.Length))));
    }
    /// <summary>
    /// The action's current value (Unreal's action value): the sum of every binding of the action in
    /// the highest-priority active context that binds it, each transformed by its dead zone, scale and
    /// modifiers. Held controls contribute their held value; relative controls contribute what they
    /// accumulated in the last <see cref="ProcessEvents"/> call.
    /// </summary>
    public InputValue ReadValue(InputActionId ActionId){
        var Context=this.Contexts.Values
            .Where(Context=>this.ActiveContexts.Contains(Context.Id)&&Context.Bindings.Any(Binding=>Binding.ActionId==ActionId))
            .OrderByDescending(static Context=>Context.Priority).ThenBy(static Context=>Context.Id.ToString(),StringComparer.Ordinal).FirstOrDefault();
        if(Context is null){ return InputValue.Zero; }
        float X=0;
        float Y=0;
        foreach(var Binding in Context.Bindings){
            if(Binding.ActionId!=ActionId){ continue; }
            if(!Binding.RequiredControls.All(Required=>this.ControlValues.Any(Pair=>Pair.Key.Control==Required&&!Pair.Value.IsZero))){ continue; }
            var Raw=IsRelative(Binding.Control)
                ?this.RelativeFrame.GetValueOrDefault(Binding.Control)
                :this.ControlValues.Where(Pair=>Pair.Key.Control==Binding.Control).Select(static Pair=>Pair.Value).FirstOrDefault();
            if(Raw.IsZero){ continue; }
            var Value=Transform(Raw,Binding);
            X+=Value.X;
            Y+=Value.Y;
        }
        return new InputValue(X,Y);
    }
    private static bool IsRelative(InputControl Control)=>Control.Kind is InputControlKind.RelativeAxis1D or InputControlKind.RelativeAxis2D;
    public void ProcessEvents(IEnumerable<InputEvent>Events){
        ArgumentNullException.ThrowIfNull(Events);
        this.Frame.Clear();
        this.RelativeFrame.Clear();
        foreach(var EventValue in Events.OrderBy(static Value=>Value.Timestamp.Ticks).ThenBy(static Value=>Value.DeviceId.ToString(),StringComparer.Ordinal).ThenBy(static Value=>Value.Sequence)){
            if(EventValue.Kind==InputEventKind.Disconnected){
                this.ReleaseDevice(EventValue);
                continue;
            }
            if(EventValue.Kind==InputEventKind.Connected){ continue; }
            if(IsRelative(EventValue.Control)){
                // Increments are read through ReadValue only; they never become held state.
                var Sum=this.RelativeFrame.GetValueOrDefault(EventValue.Control);
                this.RelativeFrame[EventValue.Control]=new InputValue(Sum.X+EventValue.Value.X,Sum.Y+EventValue.Value.Y);
                continue;
            }
            var Key=(EventValue.DeviceId,EventValue.Control);
            if(EventValue.Kind==InputEventKind.Pressed&&this.ControlValues.TryGetValue(Key,out var Existing)&&!Existing.IsZero){ continue; }
            var Raw=EventValue.Kind==InputEventKind.Released?InputValue.Zero:EventValue.Value;
            if(Raw.IsZero){ this.ControlValues.Remove(Key); }else{ this.ControlValues[Key]=Raw; }
            this.Evaluate(EventValue,Raw);
        }
    }
    public void SetContextActive(InputContextId ContextId,bool Active,MonotonicTimestamp Timestamp){
        if(!this.Contexts.ContainsKey(ContextId)){ throw new KeyNotFoundException($"Unknown input context: {ContextId}"); }
        this.Frame.Clear();
        if(Active){
            if(!this.ActiveContexts.Add(ContextId)){ return; }
            var Activated=this.Contexts[ContextId];
            foreach(var Pair in this.ActiveActions.OrderBy(static Pair=>Pair.Key.ToString(),StringComparer.Ordinal).ToArray()){
                var Previous=this.Contexts[Pair.Value.ContextId];
                var PreviousBinding=Previous.Bindings.FirstOrDefault(Binding=>Binding.ActionId==Pair.Key);
                if(PreviousBinding is null||Previous.Priority>=Activated.Priority||!Activated.Bindings.Any(Binding=>Binding.Control==PreviousBinding.Control)){ continue; }
                this.Frame.Add(new InputActionValue(Timestamp,default,Pair.Value.DeviceId,0,Pair.Value.ContextId,Pair.Key,InputActionPhase.Canceled,Pair.Value.RawValue,InputValue.Zero,InputReleaseReason.ContextChanged));
                this.ActiveActions.Remove(Pair.Key);
            }
            return;
        }
        if(!this.ActiveContexts.Remove(ContextId)){ return; }
        foreach(var Pair in this.ActiveActions.Where(Pair=>Pair.Value.ContextId==ContextId).OrderBy(static Pair=>Pair.Key.ToString(),StringComparer.Ordinal).ToArray()){
            this.Frame.Add(new InputActionValue(Timestamp,default,Pair.Value.DeviceId,0,ContextId,Pair.Key,InputActionPhase.Canceled,Pair.Value.RawValue,InputValue.Zero,InputReleaseReason.ContextChanged));
            this.ActiveActions.Remove(Pair.Key);
        }
    }
    public bool TryRebind(InputContextId ContextId,InputActionId ActionId,InputControl Control,out string Error){
        if(!this.Contexts.TryGetValue(ContextId,out var Context)){Error="Unknown input context.";return false;}
        if(this.ReservedActions.Contains(ActionId)||Context.Bindings.Any(Binding=>Binding.ActionId==ActionId&&Binding.Reserved)){Error="The action is reserved.";return false;}
        if(Context.Bindings.Any(Binding=>Binding.ActionId!=ActionId&&Binding.Control==Control)){Error="Input binding collision.";return false;}
        var Bindings=Context.Bindings.Select(Binding=>Binding.ActionId==ActionId?Binding with { Control=Control }:Binding).ToArray();
        if(!Bindings.Any(Binding=>Binding.ActionId==ActionId)){Error="Unknown input action.";return false;}
        this.Contexts[ContextId]=Context with { Bindings=Bindings };
        if(this.ActiveActions.Remove(ActionId,out var Active)){
            this.Frame.Clear();
            this.Frame.Add(new InputActionValue(default,default,Active.DeviceId,0,ContextId,ActionId,InputActionPhase.Canceled,Active.RawValue,InputValue.Zero,InputReleaseReason.Rebound));
        }
        Error=string.Empty;
        return true;
    }
    private void Evaluate(InputEvent EventValue,InputValue Raw){
        var Context=this.Contexts.Values.Where(Context=>this.ActiveContexts.Contains(Context.Id)&&Context.Bindings.Any(Binding=>Binding.Control==EventValue.Control||Binding.RequiredControls.Contains(EventValue.Control))).OrderByDescending(static Context=>Context.Priority).ThenBy(static Context=>Context.Id.ToString(),StringComparer.Ordinal).FirstOrDefault();
        if(Context is null){ return; }
        foreach(var Binding in Context.Bindings.Where(Binding=>Binding.Control==EventValue.Control||Binding.RequiredControls.Contains(EventValue.Control)).OrderBy(static Binding=>Binding.ActionId.ToString(),StringComparer.Ordinal)){
                var BindingRaw=Binding.Control==EventValue.Control?Raw:this.ControlValues.Where(Pair=>Pair.Key.Control==Binding.Control).Select(static Pair=>Pair.Value).FirstOrDefault();
                var ChordSatisfied=Binding.RequiredControls.All(Required=>this.ControlValues.Any(Pair=>Pair.Key.Control==Required&&!Pair.Value.IsZero));
                var Value=ChordSatisfied?Transform(BindingRaw,Binding):InputValue.Zero;
                var WasActive=this.ActiveActions.TryGetValue(Binding.ActionId,out var Active);
                if(Value.IsZero){
                    if(WasActive){
                        var Reason=EventValue.ReleaseReason==InputReleaseReason.None?InputReleaseReason.PhysicalRelease:EventValue.ReleaseReason;
                        this.Frame.Add(new InputActionValue(EventValue.Timestamp,EventValue.WindowId,EventValue.DeviceId,EventValue.Sequence,Context.Id,Binding.ActionId,InputActionPhase.Canceled,BindingRaw,Value,Reason));
                        this.ActiveActions.Remove(Binding.ActionId);
                    }
                    continue;
                }
                var Phase=WasActive?InputActionPhase.Performed:InputActionPhase.Started;
                if(WasActive&&Active.Value==Value){ continue; }
                this.ActiveActions[Binding.ActionId]=(Context.Id,EventValue.DeviceId,BindingRaw,Value);
                this.Frame.Add(new InputActionValue(EventValue.Timestamp,EventValue.WindowId,EventValue.DeviceId,EventValue.Sequence,Context.Id,Binding.ActionId,Phase,BindingRaw,Value));
        }
    }
    private void ReleaseDevice(InputEvent EventValue){
        foreach(var Key in this.ControlValues.Keys.Where(Key=>Key.DeviceId==EventValue.DeviceId).ToArray()){ this.ControlValues.Remove(Key); }
        foreach(var Pair in this.ActiveActions.Where(Pair=>Pair.Value.DeviceId==EventValue.DeviceId).OrderBy(static Pair=>Pair.Key.ToString(),StringComparer.Ordinal).ToArray()){
            this.Frame.Add(new InputActionValue(EventValue.Timestamp,EventValue.WindowId,EventValue.DeviceId,EventValue.Sequence,Pair.Value.ContextId,Pair.Key,InputActionPhase.Canceled,Pair.Value.RawValue,InputValue.Zero,EventValue.ReleaseReason==InputReleaseReason.None?InputReleaseReason.DeviceRemoved:EventValue.ReleaseReason));
            this.ActiveActions.Remove(Pair.Key);
        }
    }
    private static InputValue Transform(InputValue Raw,InputBinding Binding){
        var Value=TransformRaw(Raw,Binding);
        foreach(var Modifier in Binding.ValueModifiers){ Value=Modifier.Apply(Value); }
        return Value;
    }
    private static InputValue TransformRaw(InputValue Raw,InputBinding Binding){
        static float Apply(float Value,float DeadZone,float Scale){
            var Magnitude=Math.Abs(Value);
            if(Magnitude<=DeadZone){ return 0; }
            var Normalized=DeadZone>=1?0:(Magnitude-DeadZone)/(1-DeadZone);
            return MathF.CopySign(Normalized*Scale,Value);
        }
        if(Binding.Control.Kind is InputControlKind.Axis2D or InputControlKind.RelativeAxis2D){
            var Magnitude=MathF.Sqrt(Raw.X*Raw.X+Raw.Y*Raw.Y);
            if(Magnitude<=Binding.DeadZone||Magnitude==0){ return InputValue.Zero; }
            var Normalized=Binding.DeadZone>=1?0:(Magnitude-Binding.DeadZone)/(1-Binding.DeadZone);
            var Factor=Normalized*Binding.Scale/Magnitude;
            return new InputValue(Raw.X*Factor,Raw.Y*Factor);
        }
        return Binding.Control.Kind switch {
            InputControlKind.Button=>Raw.IsZero?InputValue.Zero:new InputValue(Binding.Scale,0),
            InputControlKind.Axis1D or InputControlKind.RelativeAxis1D=>new InputValue(Apply(Raw.X,Binding.DeadZone,Binding.Scale),0),
            _=>InputValue.Zero,
        };
    }
    private static void WriteStableId(BinaryWriter Writer,StableId Value){
        Writer.Write(Value.ToString());
    }
}
