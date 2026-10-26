using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.Runtime.Physics;
/// <summary>M0096 G：物理演算を実行するホスト。物理システムとキャラクターシステムはこれをコンテキストとして受け取るため、Windows エンジンホストに依存しない。生成コードは、このインターフェースを実装するエンジンホストを渡す。</summary>
public interface IPhysicsHost {
    PhysicsDomainRegistry Physics { get; }
}
/// <summary>
/// 1つのホストが持つ物理領域。アクティブな参照座標枠ごとに、初回利用時に作成する。システムがステップする座標枠を持つ。<paramref name="IsActiveFrame"/> は、ホストの
/// フレームグラフが現在保持している座標枠を示す。
/// </summary>
public sealed class PhysicsDomainRegistry(Func<ReferenceFrameId,bool>IsActiveFrame):IDisposable {
    private readonly Func<ReferenceFrameId,bool>IsActiveFrame=IsActiveFrame??throw new ArgumentNullException(nameof(IsActiveFrame));
    private readonly Dictionary<ReferenceFrameId,PhysicsDomainRuntime>Domains=[];
    private bool Disposed;
    /// <summary>システムがステップする物理領域の座標枠。</summary>
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
