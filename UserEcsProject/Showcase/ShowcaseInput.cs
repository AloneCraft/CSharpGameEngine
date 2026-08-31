using CSharpGameEngine.Core;
using CSharpGameEngine.Input;
namespace UserEcsProject.Showcase;
public static class ShowcaseInput {
    public static readonly InputContextId ContextId=new(StableId.Parse("ab93a13f-6f73-54ca-8b97-c33e60d26924"));
    public static readonly InputActionId EcsPage=new(StableId.Parse("cfaa4cf5-3ae5-5cc4-9417-2138b10690d2"));
    public static readonly InputActionId RenderingPage=new(StableId.Parse("0fcf9341-962e-5a18-9571-4cc21333328b"));
    public static readonly InputActionId PhysicsPage=new(StableId.Parse("1a6d308f-197c-54e9-98ca-87a4ed91d201"));
    public static readonly InputActionId ProgressPage=new(StableId.Parse("53a5c73e-326a-5d55-a082-3ec16bd600c8"));
    public static readonly InputActionId Pause=new(StableId.Parse("83172220-c0bb-5a8b-a157-0237c1c88d92"));
    public static readonly InputActionId Step=new(StableId.Parse("5c755e1d-e86b-515e-8be5-997ac4ff313a"));
    public static readonly InputActionId TogglePanel=new(StableId.Parse("e045ca04-f392-5a8c-b53c-af7d6b91b994"));
    public static readonly InputActionId Previous=new(StableId.Parse("63642696-f5f1-50d9-af29-572aa33d47b8"));
    public static readonly InputActionId Next=new(StableId.Parse("ec143a50-8785-56ef-a397-d213c3c47e72"));
    public static readonly InputActionId LayerUp=new(StableId.Parse("ec14168f-eb7e-50cc-b745-bf4be0b71c9b"));
    public static readonly InputActionId LayerDown=new(StableId.Parse("f8d8f3ad-32f8-5a22-9fa0-028605b8484f"));
    public static InputServer CreateServer(){
        var Keyboard=InputDeviceKind.Keyboard;
        InputBinding Bind(InputActionId Action,string Code)=>new(Action,new InputControl(Keyboard,Code),Reserved:true);
        InputBinding[]Bindings=[
            Bind(EcsPage,"1"),Bind(RenderingPage,"2"),Bind(PhysicsPage,"3"),Bind(ProgressPage,"4"),
            Bind(Pause,"P"),Bind(Step,"O"),Bind(TogglePanel,"F1"),
            Bind(Previous,"Left"),Bind(Next,"Right"),Bind(LayerUp,"Up"),Bind(LayerDown,"Down"),
        ];
        var Reserved=Bindings.Select(static Binding=>Binding.ActionId).ToHashSet();
        var Server=new InputServer(new InputActionMap([new InputContext(ContextId,"EngineShowcase",100,Bindings)],Reserved));
        Server.SetContextActive(ContextId,true,default);
        return Server;
    }
}
