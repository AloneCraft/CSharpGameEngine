using CSharpGameEngine.Entities;
using CSharpGameEngine.Numerics;
namespace CSharpGameEngine.Runtime.Windows;
/// <summary>
/// A burst of short-lived meshes (ADR-0078/M0105), after the Niagara systems the Unreal templates use for a one-shot effect
/// (<c>FX_Cursor_Success</c>: one burst, count 1, lifetime 0.7 s, white). Put it on a node of a Scene next to a <see cref="Lifetime"/>
/// and spawn the Scene with <see cref="Spawner"/>: at the first fixed tick the node's world position emits <see cref="Count"/> particles, each
/// an Entity with its own <see cref="Lifetime"/>, that fly out, fall, change size and disappear. There is no sprite pass in the renderer, so a particle
/// is a mesh the same way every other object is (a <see cref="StaticMesh"/> asset or a <see cref="PrimitiveMesh"/>), lit like it and drawn opaque.
/// The random numbers come from <see cref="Seed"/>, so the same burst looks the same every time.
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Rendering,30)]
public struct ParticleBurst() {
    /// <summary>The upper limit of <see cref="Count"/>; a larger value emits this many.</summary>
    public const int MaxCount=256;
    /// <summary>The mesh of one particle: a path under the game's Assets (a <c>.glb</c>), or empty to use a <see cref="PrimitiveMesh"/> of <see cref="Shape"/>.</summary>
    public string AssetPath=string.Empty;
    /// <summary>The primitive of one particle when <see cref="AssetPath"/> is empty. A unit-sized shape, scaled by <see cref="StartScale"/> and <see cref="EndScale"/>.</summary>
    public PrimitiveType Shape=PrimitiveType.Sphere;
    /// <summary>How many particles, 0 to <see cref="MaxCount"/>.</summary>
    public int Count=8;
    /// <summary>How long one particle lives, in seconds.</summary>
    public LogicalScalar Lifetime=LogicalScalar.FromSingle(0.7f);
    /// <summary>Metres per second along the particle's direction at birth. 0 keeps the particles where they were born.</summary>
    public LogicalScalar Speed=LogicalScalar.Zero;
    /// <summary>How much faster or slower one particle is than <see cref="Speed"/>, as a fraction of it (0.25 is ±25 %).</summary>
    public LogicalScalar SpeedJitter=LogicalScalar.Zero;
    /// <summary>How far to the side the directions lean, as sideways distance per unit of upward distance: 0 is straight up, 1 a 45° cone, large values nearly flat.</summary>
    public LogicalScalar Spread=LogicalScalar.One;
    /// <summary>Metres per second squared pulling the particles down.</summary>
    public LogicalScalar Gravity=LogicalScalar.Zero;
    /// <summary>The particle's scale at birth.</summary>
    public LogicalScalar StartScale=LogicalScalar.One;
    /// <summary>The particle's scale when its life is over; it moves from <see cref="StartScale"/> to this in a straight line.</summary>
    public LogicalScalar EndScale=LogicalScalar.Zero;
    /// <summary>Seeds the random directions and speeds.</summary>
    public uint Seed=1u;
}
/// <summary>Whether a <see cref="ParticleBurst"/> has gone off; it does so once.</summary>
public struct ParticleBurstRuntime:IRuntimeComponent<ParticleBurst> {
    public bool Emitted;
}
/// <summary>One particle of a <see cref="ParticleBurst"/>. Its position and size are its Entity's <see cref="Transform"/>, its age its <see cref="Lifetime"/>.</summary>
public struct Particle {
    public LogicalVector3 Velocity;
    public LogicalScalar Gravity;
    public LogicalScalar StartScale;
    public LogicalScalar EndScale;
    /// <summary>The life it started with, so the age is known from the <see cref="Lifetime"/> left.</summary>
    public LogicalScalar Duration;
}
/// <summary>Registers the shapes a burst creates: a particle of a mesh asset and of a primitive.</summary>
internal struct ParticleShapes:ISystem {
    public void Spawn(ref Transform Transform,ref StaticMesh Mesh,ref Particle Particle,ref Lifetime Lifetime){}
    public void Spawn(ref Transform Transform,ref PrimitiveMesh Mesh,ref Particle Particle,ref Lifetime Lifetime){}
}
/// <summary>ADR-0078/M0105: sets off every <see cref="ParticleBurst"/> that has not gone off yet, at the node's world position.</summary>
[SystemPhase(SystemPhase.FixedSimulation)]
internal struct ParticleBurstEmit:ISystem {
    public void Update(Entity Entity,ref Transform Transform,ref ParticleBurst Burst,ref ParticleBurstRuntime Runtime,ref State State){
        if(Runtime.Emitted){ return; }
        // A node whose parents cannot be resolved yet is tried again at the next tick.
        if(RuntimeWorldTransform.TryResolveWorld(Entity,State.World,out var World)!=FixedTransformResolveStatus.Success){ return; }
        Runtime.Emitted=true;
        var Origin=new LogicalVector3(World.M41,World.M42,World.M43);
        var Count=Math.Clamp(Burst.Count,0,ParticleBurst.MaxCount);
        var Duration=Burst.Lifetime>LogicalScalar.Zero?Burst.Lifetime:LogicalScalar.Zero;
        var Random=new Random(Burst.Seed);
        for(var Index=0;Index<Count;Index++){
            var Direction=Random.Direction(Burst.Spread);
            var Speed=Burst.Speed*(LogicalScalar.One+Burst.SpeedJitter*Random.Signed());
            var Placed=new Transform {
                Frame=Transform.Frame,
                Position=Origin,
                Scale=new LogicalVector3(Burst.StartScale,Burst.StartScale,Burst.StartScale),
            };
            var Particle=new Particle {
                Velocity=Direction*Speed,
                Gravity=Burst.Gravity,
                StartScale=Burst.StartScale,
                EndScale=Burst.EndScale,
                Duration=Duration,
            };
            var Life=new Lifetime { Seconds=Duration };
            if(string.IsNullOrEmpty(Burst.AssetPath)){
                State.World.CreateDeferred(Placed,new PrimitiveMesh { Type=Burst.Shape },Particle,Life);
            } else {
                State.World.CreateDeferred(Placed,new StaticMesh { AssetPath=Burst.AssetPath },Particle,Life);
            }
        }
    }
    /// <summary>A small deterministic generator (xorshift32); the burst must look the same on every machine, so it does not use the system's.</summary>
    private struct Random(uint Seed) {
        private uint State=Seed==0u?0x9E3779B9u:Seed;
        private uint Next(){
            var Value=this.State;
            Value^=Value<<13;
            Value^=Value>>17;
            Value^=Value<<5;
            return this.State=Value;
        }
        /// <summary>A number in [-1, 1) with 16 bits of fraction, exact in fixed point.</summary>
        internal LogicalScalar Signed()=>LogicalScalar.FromInt64((long)(this.Next()>>16)-32768L)/LogicalScalar.FromInt64(32768L);
        /// <summary>A unit direction leaning at most <paramref name="Spread"/> sideways per unit up, spread evenly over the disc of the sideways lean.</summary>
        internal LogicalVector3 Direction(LogicalScalar Spread){
            LogicalScalar R;
            LogicalScalar F;
            do {
                R=this.Signed();
                F=this.Signed();
            } while(R*R+F*F>LogicalScalar.One);
            var Lean=new LogicalVector3(R*Spread,LogicalScalar.One,F*Spread);
            return Lean.TryNormalize(out var Unit)?Unit:LogicalVector3.UnitU;
        }
    }
}
/// <summary>ADR-0078/M0105: moves, pulls down and rescales every <see cref="Particle"/>, before its <see cref="Lifetime"/> is counted down.</summary>
[SystemPhase(SystemPhase.FixedSimulation)]
[RunsBefore(typeof(LifetimeExpire))]
internal struct ParticleMotion:ISystem {
    public void Update(ref Transform Transform,ref Particle Particle,ref Lifetime Lifetime,ref State State){
        var Step=LogicalScalar.One/LogicalScalar.FromInt64(State.FixedTickRate);
        Transform.Position+=Particle.Velocity*Step;
        Particle.Velocity=new LogicalVector3(Particle.Velocity.R,Particle.Velocity.U-Particle.Gravity*Step,Particle.Velocity.F);
        var Age=Particle.Duration>LogicalScalar.Zero?LogicalScalar.One-Lifetime.Seconds/Particle.Duration:LogicalScalar.One;
        Age=Age<LogicalScalar.Zero?LogicalScalar.Zero:Age>LogicalScalar.One?LogicalScalar.One:Age;
        var Scale=Particle.StartScale+(Particle.EndScale-Particle.StartScale)*Age;
        Transform.Scale=new LogicalVector3(Scale,Scale,Scale);
    }
}
