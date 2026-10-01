using CSharpGameEngine.Entities;
using CSharpGameEngine.Generated;
using CSharpGameEngine.Runtime.Physics;
using CSharpGameEngine.Runtime.Windows;

namespace CSharpGameEngine.Runtime.Windows.Tests;

/// <summary>
/// M0097: the editor world registers the scene's bodies for the physics debug overlay but does not advance time, so
/// the overlay stays on the objects the Scene view draws. Only the running game simulates.
/// </summary>
public sealed class EditorPhysicsTests {
    [Theory]
    [InlineData(EngineExecutionMode.Runtime,true)]
    [InlineData(EngineExecutionMode.EditorPreview,false)]
    public void PhysicsAdvancesOnlyInTheRunningGame(EngineExecutionMode Mode,bool Falls){
        using EngineHost Host=new();
        State State=Host.CreateState();
        SceneInstance Instance=State.World.InstantiateScene<UserEcsProject.Scenes.PhysicsStackBottomScene>();
        State.Enabled=true;
        State.ExecutionMode=Mode;
        GeneratedSystemSet Systems=default;
        Entity Body=State.World.GetSceneEntities(Instance).ToArray()
            .Single(Entity=>State.World.TryGetComponent(Entity,out RigidBody Rigid)&&Rigid.MotionType==PhysicsMotionType.Dynamic);
        Assert.True(State.World.TryGetComponent(Body,out Transform Before));
        try {
            for(int Tick=0;Tick<8;Tick++){ GeneratedGameLoop.RunBakeTick(ref State,ref Systems,Host); }
            Assert.True(State.World.TryGetComponent(Body,out Transform After));
            if(Falls){ Assert.True(After.Position.U<Before.Position.U); }
            else { Assert.Equal(Before.Position,After.Position); }
            // The bodies are still registered, so the overlay has something to draw.
            Assert.True(Host.PhysicsWorld.TryGetBodyState(Body,out _));
        }
        finally { GeneratedGameLoop.Shutdown(ref State,ref Systems,Host); }
    }
}
