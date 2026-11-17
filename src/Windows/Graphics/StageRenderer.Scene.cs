using System.Numerics;
using CSharpGameEngine.Rendering;
using CSharpGameEngine.Rendering.Pipeline;
using TerraFX.Interop.DirectX;
using static CSharpGameEngine.Rendering.Pipeline.StageGpuLayouts;
namespace CSharpGameEngine.Graphics.D3D12;
/// <summary>
/// GPU シーン（ADR-0100 決定 2、M0129）。物体の表と材質の表を 1 フレームに 1 回書き、描画は物体の番号だけを渡す。
/// 表の形は L4 の <see cref="StageInstanceData"/> と <see cref="StageMaterialData"/> が正本で、ここは値を写すだけ。
/// </summary>
public sealed unsafe partial class StageRenderer {
    /// <summary>主パスと影のルートシグネチャで、描画ごとの定数（b1）を置く引数の番号。</summary>
    private const uint DrawConstantsRootParameter=1;
    /// <summary>影のルートシグネチャで、物体の表（t20）を置く引数の番号。</summary>
    private const uint ShadowInstanceTableRootParameter=2;
    /// <summary>主パスのルートシグネチャで、物体の表（t20）を置く引数の番号。</summary>
    private const uint InstanceTableRootParameter=11;
    /// <summary>主パスのルートシグネチャで、材質の表（t21）を置く引数の番号。</summary>
    private const uint MaterialTableRootParameter=12;
    private ID3D12Resource*InstanceTableBuffer;
    private byte*InstanceTableMapped;
    private ID3D12Resource*MaterialTableBuffer;
    private byte*MaterialTableMapped;
    /// <summary>表をフレームの枠の数だけ作る。GPU が前のフレームの表を読んでいる間に、次のフレームの表を書けるようにするため。</summary>
    private void CreateSceneTables(){
        this.CreateUploadBuffer(checked(InstanceDataSize*(uint)this.ObjectCapacity*FrameCount),out this.InstanceTableBuffer,out this.InstanceTableMapped,"creating the instance table");
        this.CreateUploadBuffer(checked(MaterialDataSize*(uint)this.MaterialCapacity*FrameCount),out this.MaterialTableBuffer,out this.MaterialTableMapped,"creating the material table");
    }
    private void ReleaseSceneTables(){
        if(this.InstanceTableBuffer!=null){
            this.InstanceTableBuffer->Unmap(0,null);
            this.InstanceTableMapped=null;
            _=this.InstanceTableBuffer->Release();
            this.InstanceTableBuffer=null;
        }
        if(this.MaterialTableBuffer!=null){
            this.MaterialTableBuffer->Unmap(0,null);
            this.MaterialTableMapped=null;
            _=this.MaterialTableBuffer->Release();
            this.MaterialTableBuffer=null;
        }
    }
    private ulong InstanceTableOffset(uint FrameIndex)=>(ulong)FrameIndex*(ulong)this.ObjectCapacity*InstanceDataSize;
    private ulong MaterialTableOffset(uint FrameIndex)=>(ulong)FrameIndex*(ulong)this.MaterialCapacity*MaterialDataSize;
    /// <summary>
    /// このフレームの枠の表を、今の物体と材質の値で書く（<see cref="StagePass.SceneUpload"/>）。値を設定した時点ではなくここで全部を書くので、
    /// 設定を毎フレーム呼び直さなくても、どの枠の表も今の値になる。保守的なラスタ深度は、主ビューが対数深度のときだけ求める（M0100）。
    /// メッシュの境界の中心でのビューの深さ（ビュー投影の W）で接するので、隠れた破片をどの距離でも早く捨てられる。
    /// </summary>
    private void RecordSceneUpload(){
        // 済んだフレームが使っていた資源を放し、積んであったテクスチャを、どの描画よりも前に送る（M0129 単位 5）。
        this.ReleaseCompletedRetirements();
        this.RecordPendingTextureUploads();
        var Instances=(StageInstanceData*)(this.InstanceTableMapped+this.InstanceTableOffset(this.FrameIndex));
        var Logarithmic=this.CurrentViewDepth.Encoding==RenderDepthEncoding.Logarithmic;
        var ViewProjection=this.CurrentViewProjection;
        for(var ObjectIndex=0;ObjectIndex<this.ObjectCount;ObjectIndex++){
            if(!this.ObjectActive[ObjectIndex]){ continue; }
            var World=this.ObjectTransforms[ObjectIndex];
            var RasterDepth=Vector2.Zero;
            if(Logarithmic){
                var Centre=Vector3.Transform(this.MeshBoundsCentres[this.ObjectMeshIndices[ObjectIndex]],World);
                var ViewDepth=Centre.X*ViewProjection.M14+Centre.Y*ViewProjection.M24+Centre.Z*ViewProjection.M34+ViewProjection.M44;
                var Depth=this.CurrentViewDepth.Projection.ConservativeDepthAt(ViewDepth);
                RasterDepth=new(Depth.Limit,Depth.Slope);
            }
            Instances[ObjectIndex]=new(){
                World=World,
                ConservativeRasterDepth=RasterDepth,
                EmissiveScale=this.ObjectEmissiveScales[ObjectIndex],
                SelectionWeight=this.ObjectSelectionWeights[ObjectIndex],
                Material=(uint)this.ObjectMaterialIndices[ObjectIndex],
            };
        }
        var Materials=(StageMaterialData*)(this.MaterialTableMapped+this.MaterialTableOffset(this.FrameIndex));
        for(var MaterialIndex=0;MaterialIndex<this.MaterialCount;MaterialIndex++){
            Materials[MaterialIndex]=StageMaterialData.Create(
                this.MaterialBaseColorFactors[MaterialIndex],this.MaterialRoughnessFactors[MaterialIndex],this.MaterialMetallicFactors[MaterialIndex],
                this.MaterialEmissiveNits[MaterialIndex],this.MaterialWorldGrids[MaterialIndex],this.MaterialWorldGridAnchors[MaterialIndex],this.MaterialTextureIndices[MaterialIndex]);
        }
    }
    /// <summary>主パスのルートシグネチャへ、このフレームの物体の表と材質の表を束縛する。</summary>
    private void BindSceneTables(){
        this.CommandList->SetGraphicsRootShaderResourceView(InstanceTableRootParameter,this.InstanceTableBuffer->GetGPUVirtualAddress()+this.InstanceTableOffset(this.FrameIndex));
        this.CommandList->SetGraphicsRootShaderResourceView(MaterialTableRootParameter,this.MaterialTableBuffer->GetGPUVirtualAddress()+this.MaterialTableOffset(this.FrameIndex));
    }
    /// <summary>影のルートシグネチャへ、このフレームの物体の表を束縛する。影は材質を読まない。</summary>
    private void BindShadowInstanceTable(){
        this.CommandList->SetGraphicsRootShaderResourceView(ShadowInstanceTableRootParameter,this.InstanceTableBuffer->GetGPUVirtualAddress()+this.InstanceTableOffset(this.FrameIndex));
    }
    /// <summary>次の描画が描く物体を渡す。主パスと影のどちらのルートシグネチャでも、同じ番号の引数に同じ形で置く。</summary>
    private void SetDrawConstants(int ObjectIndex,StageDrawFlags Flags=StageDrawFlags.None){
        StageDrawConstants Draw=new(){ Instance=(uint)ObjectIndex,PresentationMode=(uint)this.PresentationMode,Flags=(uint)Flags };
        this.CommandList->SetGraphicsRoot32BitConstants(DrawConstantsRootParameter,DrawConstantCount,&Draw,0);
    }
}
