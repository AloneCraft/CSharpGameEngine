namespace CSharpGameEngine.Graphics.D3D12;
/// <summary>
/// 仮想ジオメトリの遮蔽判定（ADR-0100 決定 3、M0131）。主パスが書いた物理距離の画像から Hi-Z を作る（<see cref="StageHiZ"/>）。
/// 単位 1 では、試験の印があるときだけ主パスの最後に作る。2 パスの遮蔽（単位 2）で主パスの途中へ移す。
/// </summary>
public sealed unsafe partial class StageRenderer {
    private StageHiZ?HiZ;
    /// <summary>試験用: 主パスの最後に Hi-Z を作る。</summary>
    internal bool BuildHiZAfterMainPassForTesting { get;set; }
    /// <summary>主パスの最後で、印があれば Hi-Z を作る。計算の束縛に置き換わるので、後のパスのために印を立てる。</summary>
    private void RecordHiZAfterMainPass(ref FrameRecording Recording){
        if(!this.BuildHiZAfterMainPassForTesting){ return; }
        this.HiZ!.Record(this.CommandList,this.SceneDistanceBuffer,this.Width,this.Height);
        Recording.RanComputeAfterMainPass=true;
    }
    /// <summary>試験用: Hi-Z の段 <paramref name="Level"/>（行優先）と、その大きさ。GPU の完了を待って読む。</summary>
    internal (float[]Values,int Width,int Height)ReadHiZLevelForTesting(int Level){
        this.ThrowIfDisposed();
        this.WaitForGpuIdle();
        var Values=this.HiZ!.ReadLevelForTesting(Level);
        var Sizes=CSharpGameEngine.Rendering.Streaming.HiZPyramid.LevelSizes(this.HiZ.ScreenWidth,this.HiZ.ScreenHeight);
        return (Values,Sizes[Level].Width,Sizes[Level].Height);
    }
    /// <summary>試験用: 最後に作った Hi-Z の段の数（作っていなければ 0）。</summary>
    internal int HiZLevelCountForTesting=>this.HiZ?.Result==null?0:this.HiZ.LevelCount;
}
