using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.Runtime.Physics;
/// <summary>
/// M0096 G: a host that runs physics. The physics and character systems take it as their context, so they do not
/// depend on the Windows engine host; the generator passes the engine host, which implements it.
/// </summary>
public interface IPhysicsHost {
    PhysicsDomainRegistry Physics { get; }
}
/// <summary>
/// The physics domains of one host: one per active reference frame, created on first use, and the frame the
/// systems step. <paramref name="IsActiveFrame"/> tells which frames the host's frame graph currently holds.
/// </summary>
public sealed class PhysicsDomainRegistry(Func<ReferenceFrameId,bool>IsActiveFrame):IDisposable {
    private readonly Func<ReferenceFrameId,bool>IsActiveFrame=IsActiveFrame??throw new ArgumentNullException(nameof(IsActiveFrame));
    private readonly Dictionary<ReferenceFrameId,PhysicsDomainRuntime>Domains=[];
    private bool Disposed;
    /// <summary>The frame whose domain the systems step.</summary>
    public ReferenceFrameId Frame { get;private set; }=ReferenceFrameId.Root;
    internal PhysicsDomainRuntime Active=>this.Get(this.Frame);
    public IReadOnlyPhysicsWorld World(ReferenceFrameId Frame)=>this.Get(Frame).World;
    public void SetFrame(ReferenceFrameId Frame){
        this.Frame=this.Get(Frame).Frame;
    }
    internal PhysicsDomainRuntime Get(ReferenceFrameId Frame){
        ObjectDisposedException.ThrowIf(this.Disposed,this);
        if(!this.IsActiveFrame(Frame)){
            throw new ArgumentException($"The physics reference frame is not active: {Frame}.",nameof(Frame));
        }
        if(!this.Domains.TryGetValue(Frame,out PhysicsDomainRuntime? Domain)){
            Domain=new PhysicsDomainRuntime(Frame);
            this.Domains.Add(Frame,Domain);
        }
        return Domain;
    }
    public void Dispose(){
        if(this.Disposed){ return; }
        this.Disposed=true;
        try {
            foreach(PhysicsDomainRuntime Domain in this.Domains.Values){ Domain.Dispose(); }
        }
        finally {
            this.Domains.Clear();
        }
    }
}
