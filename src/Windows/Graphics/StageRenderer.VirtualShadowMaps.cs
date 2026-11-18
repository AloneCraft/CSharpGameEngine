using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CSharpGameEngine.Rendering;
using CSharpGameEngine.Rendering.Pipeline;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.DirectX.DirectX;
using static TerraFX.Interop.DirectX.D3D12_HEAP_FLAGS;
using static TerraFX.Interop.DirectX.D3D12_HEAP_TYPE;
using static TerraFX.Interop.DirectX.D3D12_RESOURCE_FLAGS;
using static TerraFX.Interop.DirectX.D3D12_RESOURCE_STATES;
using static TerraFX.Interop.Windows.Windows;
using static CSharpGameEngine.Rendering.Pipeline.StageGpuLayouts;
namespace CSharpGameEngine.Graphics.D3D12;
/// <summary>
/// 作り直した仮想シャドウ（ADR-0100 決定 5、M0132）。計画は L4 の <see cref="VirtualShadowMapPlanner"/>、ここは実行だけを持つ。
/// <list type="bullet">
/// <item>主パスの前（単位 3）: その枠へ前に写した画面の要求を読み戻し、要求を作ったフレームの窓の位置でページの番号に直して計画し、作るページを置き場へ描き、ページ表を上げる。</item>
/// <item>主パスの後（単位 2）: このフレームの段の行列で、距離の画像からページの要求を作る。</item>
/// </list>
/// 単位 3 までは試験の印があるときだけ動き、影の絵は M0115 のまま（単位 4 で影を引くシェーダーをこちらへ切り替える）。
/// 置き場は 1 行 64 ページ（8192 テクセル）で、行の数は置き場の数（<see cref="DirectionalShadowSettings.PageBudget"/>）で決まる。休む状態は画素シェーダーの読み取り。
/// </summary>
public sealed unsafe partial class StageRenderer {
    private readonly VirtualShadowMapPlanner ShadowMapPlanner=new();
    private StageVirtualShadowRequests?ShadowRequests;
    /// <summary>格子を世界に固定する位置（既定は区画 0、ずれ 0。描画空間がそのまま世界の位置のとき）。</summary>
    internal VirtualShadowAnchor ShadowAnchor { get;set; }
    /// <summary>試験用: 仮想シャドウの新しい経路（要求、計画、ページの描画、ページ表）を動かす。</summary>
    internal bool VirtualShadowMapsForTesting { get;set; }
    /// <summary>
    /// 試験用: ページを描いた後、段 Level のページ (PageX, PageY) を左下とする Pages × Pages ページの範囲を、まとめて 1 枚の深度へ描く
    /// （<see cref="ReadVirtualShadowMapForTesting"/> で、置き場のページと比べる）。
    /// </summary>
    internal (int Level,int PageX,int PageY,int Pages)?VirtualShadowReferenceForTesting { get;set; }
    private readonly (int X,int Y)[]ShadowRequestOrigins=new (int X,int Y)[VirtualShadowMapPlanner.LevelCount];
    private readonly Matrix4x4[]ShadowRequestProjections=new Matrix4x4[VirtualShadowMapPlanner.LevelCount];
    /// <summary>読み戻しの輪の枠ごとの、要求を作ったフレームの窓の位置（印はその窓の中の位置なので、読むときにこれでページの番号に直す）。</summary>
    private readonly (int X,int Y)[][]ShadowRequestSlotOrigins=Enumerable.Range(0,FrameCount).Select(static _=>new (int X,int Y)[VirtualShadowMapPlanner.LevelCount]).ToArray();
    private readonly bool[]ShadowRequestSlotRecorded=new bool[FrameCount];
    private readonly uint[]ShadowRequestBits=new uint[StageVirtualShadowRequests.WordCount];
    /// <summary>最後に読んだ要求（新しい要求が読めないフレームも、これで計画する）。</summary>
    private readonly List<VirtualShadowPageKey>ShadowMapRequests=[];
    private readonly List<VirtualShadowBounds>ShadowMapObjects=[];
    private bool ShadowMapPlannedThisFrame;
    private ID3D12Resource*ShadowPageAtlas;
    private int ShadowPageAtlasPages;
    private ID3D12Resource*ShadowPageReference;
    private int ShadowPageReferenceSize;
    /// <summary>深度の記述子 2 つ: 0 = 置き場、1 = 試験の比べる深度。</summary>
    private ID3D12DescriptorHeap*ShadowPageDsvHeap;
    private ID3D12Resource*ShadowPageTableBuffer;
    private byte*ShadowPageTableMapped;
    /// <summary>ページの行列の定数（フレームごとに、置き場の数 + 試験の 1 つ）。</summary>
    private ID3D12Resource*ShadowPageProjectionBuffer;
    private byte*ShadowPageProjectionMapped;
    private uint ShadowPageLastFrameIndex;
    private const int ShadowPageProjectionsPerFrame=VirtualShadowMapPlanner.MaximumPhysicalPages+1;
    /// <summary>
    /// 主パスの前: 要求を読み戻して計画し、作るページを置き場へ描き、ページ表をこのフレームの枠へ上げる。
    /// </summary>
    private void RenderVirtualShadowMapPages(){
        this.ShadowMapPlannedThisFrame=false;
        if(!this.VirtualShadowMapsForTesting||this.DirectionalShadowIndex<0){ return; }
        var PhysicalPages=this.ShadowSettings.PageBudget;
        this.EnsureShadowPageResources(PhysicalPages);
        // この枠へ前に写した要求（FrameCount フレーム前）。MoveToNextFrame がその枠のフェンスを待ってある。
        if(this.ShadowRequestSlotRecorded[this.FrameIndex]){
            this.ShadowRequestSlotRecorded[this.FrameIndex]=false;
            if(this.ShadowRequests!.TryRead(this.FrameIndex,this.ShadowRequestBits)){
                this.DecodeShadowRequests(this.ShadowRequestSlotOrigins[this.FrameIndex]);
            }
        }
        this.CollectShadowMapObjects();
        var Frame=this.FrameSlot(this.FrameIndex);
        var Light=*(Vector4*)(Frame+FrameDirectionalLightsOffset+(uint)this.DirectionalShadowIndex*DirectionalLightStride);
        this.ShadowMapPlanner.Plan(this.ShadowAnchor,this.CurrentCameraPosition,new Vector3(Light.X,Light.Y,Light.Z),CollectionsMarshal.AsSpan(this.ShadowMapObjects),
            CollectionsMarshal.AsSpan(this.ShadowMapRequests),PhysicalPages);
        this.ShadowMapPlannedThisFrame=true;
        this.ShadowPageLastFrameIndex=this.FrameIndex;
        this.ShadowMapPlanner.PageTable.CopyTo(new Span<uint>(this.ShadowPageTableMapped+(ulong)this.FrameIndex*VirtualShadowMapPlanner.TableLength*4,VirtualShadowMapPlanner.TableLength));
        var Pages=this.ShadowMapPlanner.PagesToRender;
        var Reference=this.VirtualShadowReferenceForTesting;
        if(Pages.Count==0&&Reference is null){ return; }
        var ToWrite=D3D12_RESOURCE_BARRIER.InitTransition(this.ShadowPageAtlas,D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE,D3D12_RESOURCE_STATE_DEPTH_WRITE);
        this.CommandList->ResourceBarrier(1,&ToWrite);
        this.CommandList->SetGraphicsRootSignature(this.ShadowRootSignature);
        this.BindShadowInstanceTable();
        this.CommandList->SetPipelineState(this.ShadowPipelineState);
        this.CommandList->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        var VariantAvailable=this.VirtualGeometryPipelineFor(StagePipelines.Shadow)!=null;
        var Dsv=this.ShadowPageDsvHeap->GetCPUDescriptorHandleForHeapStart();
        this.CommandList->OMSetRenderTargets(0,null,FALSE,&Dsv);
        for(var Index=0;Index<Pages.Count;Index++){
            var Page=Pages[Index];
            var X=Page.PhysicalIndex%VirtualShadowMapPlanner.PhysicalPagesPerAxis*VirtualShadowMapPlanner.PageTexels;
            var Y=Page.PhysicalIndex/VirtualShadowMapPlanner.PhysicalPagesPerAxis*VirtualShadowMapPlanner.PageTexels;
            this.DrawShadowMapRange(Dsv,Index,Page.Projection,X,Y,VirtualShadowMapPlanner.PageTexels,VariantAvailable);
        }
        if(Reference is{ }Range){
            var Size=Range.Pages*VirtualShadowMapPlanner.PageTexels;
            this.EnsureShadowPageReference(Size);
            var ReferenceDsv=new D3D12_CPU_DESCRIPTOR_HANDLE(Dsv,1,this.DsvDescriptorSize);
            this.CommandList->OMSetRenderTargets(0,null,FALSE,&ReferenceDsv);
            var Projection=this.ShadowMapPlanner.RangeProjection(this.ShadowAnchor,Range.Level,Range.PageX,Range.PageY,Range.Pages);
            this.DrawShadowMapRange(ReferenceDsv,ShadowPageProjectionsPerFrame-1,Projection,0,0,Size,VariantAvailable);
        }
        var ToRead=D3D12_RESOURCE_BARRIER.InitTransition(this.ShadowPageAtlas,D3D12_RESOURCE_STATE_DEPTH_WRITE,D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE);
        this.CommandList->ResourceBarrier(1,&ToRead);
    }
    /// <summary>行列 <paramref name="Projection"/> の範囲を、描画先 <paramref name="Dsv"/> の (X, Y) から Size × Size テクセルへ描く（深度を 1 に消してから）。</summary>
    private void DrawShadowMapRange(D3D12_CPU_DESCRIPTOR_HANDLE Dsv,int ProjectionSlot,Matrix4x4 Projection,int X,int Y,int Size,bool VariantAvailable){
        var Offset=((ulong)this.FrameIndex*ShadowPageProjectionsPerFrame+(ulong)ProjectionSlot)*ConstantAlignment;
        Unsafe.CopyBlock(this.ShadowPageProjectionMapped+Offset,&Projection,64);
        this.CommandList->SetGraphicsRootConstantBufferView(0,this.ShadowPageProjectionBuffer->GetGPUVirtualAddress()+Offset);
        var Rect=new RECT { left=X,top=Y,right=X+Size,bottom=Y+Size };
        this.CommandList->ClearDepthStencilView(Dsv,D3D12_CLEAR_FLAGS.D3D12_CLEAR_FLAG_DEPTH,1f,0,1,&Rect);
        var Viewport=new D3D12_VIEWPORT { TopLeftX=X,TopLeftY=Y,Width=Size,Height=Size,MaxDepth=1f };
        this.CommandList->RSSetViewports(1,&Viewport);
        this.CommandList->RSSetScissorRects(1,&Rect);
        this.DrawShadowCasters(Projection,this.ShadowMapObjects,VariantAvailable);
    }
    /// <summary>印（段 × 窓の中の 128 × 128）を、要求を作ったフレームの窓の位置でページの番号に直す。</summary>
    private void DecodeShadowRequests((int X,int Y)[]Origins){
        this.ShadowMapRequests.Clear();
        for(var Word=0;Word<this.ShadowRequestBits.Length;Word++){
            var Bits=this.ShadowRequestBits[Word];
            while(Bits!=0){
                var Index=Word*32+System.Numerics.BitOperations.TrailingZeroCount(Bits);
                Bits&=Bits-1;
                var Level=Index/(VirtualShadowMapPlanner.PagesPerAxis*VirtualShadowMapPlanner.PagesPerAxis);
                var Y=Index/VirtualShadowMapPlanner.PagesPerAxis%VirtualShadowMapPlanner.PagesPerAxis;
                var X=Index%VirtualShadowMapPlanner.PagesPerAxis;
                this.ShadowMapRequests.Add(new(Level,Origins[Level].X+X,Origins[Level].Y+VirtualShadowMapPlanner.PagesPerAxis-1-Y));
            }
        }
    }
    /// <summary>影を落とす物の包む箱（描画空間）。骨のあるメッシュは控えめに大きい箱で、毎フレーム動いた扱い。</summary>
    private void CollectShadowMapObjects(){
        this.ShadowMapObjects.Clear();
        for(var ObjectIndex=0;ObjectIndex<this.ObjectCount;ObjectIndex++){
            if(!this.ObjectActive[ObjectIndex]){ continue; }
            var Mesh=this.ObjectMeshIndices[ObjectIndex];
            var Bounds=this.Skinning!.ConservativeBounds(Mesh,this.MeshBoundsMinimums[Mesh],this.MeshBoundsMaximums[Mesh]);
            var Minimum=new Vector3(float.PositiveInfinity);
            var Maximum=new Vector3(float.NegativeInfinity);
            for(var Corner=0;Corner<8;Corner++){
                var Point=Vector3.Transform(new Vector3((Corner&1)==0?Bounds.Minimum.X:Bounds.Maximum.X,(Corner&2)==0?Bounds.Minimum.Y:Bounds.Maximum.Y,(Corner&4)==0?Bounds.Minimum.Z:Bounds.Maximum.Z),this.ObjectTransforms[ObjectIndex]);
                Minimum=Vector3.Min(Minimum,Point);
                Maximum=Vector3.Max(Maximum,Point);
            }
            this.ShadowMapObjects.Add(new(ObjectIndex,Minimum,Maximum,Visible:true,CastsShadow:!this.ObjectNoShadow[ObjectIndex],Deforming:this.Skinning.IsSkinned(Mesh),Mesh,this.ObjectTransforms[ObjectIndex]));
        }
    }
    /// <summary>置き場（置き場の数が変わったら作り直す）、深度の記述子、ページ表と行列の定数。</summary>
    private void EnsureShadowPageResources(int PhysicalPages){
        if(this.ShadowPageDsvHeap==null){
            var HeapDesc=new D3D12_DESCRIPTOR_HEAP_DESC { Type=D3D12_DESCRIPTOR_HEAP_TYPE.D3D12_DESCRIPTOR_HEAP_TYPE_DSV,NumDescriptors=2 };
            ID3D12DescriptorHeap*Heap;
            D3D12Diagnostics.EnsureSuccess(this.Device!.Device->CreateDescriptorHeap(&HeapDesc,__uuidof<ID3D12DescriptorHeap>(),(void**)&Heap),nameof(this.EnsureShadowPageResources),"virtual shadow map DSV heap");
            this.ShadowPageDsvHeap=Heap;
            this.CreateUploadBuffer((uint)(VirtualShadowMapPlanner.TableLength*4*FrameCount),out this.ShadowPageTableBuffer,out this.ShadowPageTableMapped,"virtual shadow map page tables");
            Unsafe.InitBlock(this.ShadowPageTableMapped,0,(uint)(VirtualShadowMapPlanner.TableLength*4*FrameCount));
            this.CreateUploadBuffer((uint)(ShadowPageProjectionsPerFrame*ConstantAlignment*FrameCount),out this.ShadowPageProjectionBuffer,out this.ShadowPageProjectionMapped,"virtual shadow map page projections");
        }
        if(this.ShadowPageAtlas!=null&&this.ShadowPageAtlasPages==PhysicalPages){ return; }
        if(this.ShadowPageAtlas!=null){
            this.WaitForGpuIdle();
            _=this.ShadowPageAtlas->Release();
            this.ShadowPageAtlas=null;
        }
        var Columns=Math.Min(PhysicalPages,VirtualShadowMapPlanner.PhysicalPagesPerAxis);
        var Rows=(PhysicalPages+VirtualShadowMapPlanner.PhysicalPagesPerAxis-1)/VirtualShadowMapPlanner.PhysicalPagesPerAxis;
        this.ShadowPageAtlas=this.CreateShadowDepthTexture(Columns*VirtualShadowMapPlanner.PageTexels,Rows*VirtualShadowMapPlanner.PageTexels,D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE,0,"virtual shadow map page atlas");
        this.ShadowPageAtlasPages=PhysicalPages;
    }
    private void EnsureShadowPageReference(int Size){
        if(this.ShadowPageReference!=null&&this.ShadowPageReferenceSize==Size){ return; }
        if(this.ShadowPageReference!=null){
            this.WaitForGpuIdle();
            _=this.ShadowPageReference->Release();
            this.ShadowPageReference=null;
        }
        this.ShadowPageReference=this.CreateShadowDepthTexture(Size,Size,D3D12_RESOURCE_STATE_DEPTH_WRITE,1,"virtual shadow map reference depth");
        this.ShadowPageReferenceSize=Size;
    }
    /// <summary>影の深度の画像を作り、記述子の置き場の <paramref name="DsvIndex"/> 番目に深度の記述子を書く。</summary>
    private ID3D12Resource*CreateShadowDepthTexture(int Width,int Height,D3D12_RESOURCE_STATES State,int DsvIndex,string Context){
        var Desc=D3D12_RESOURCE_DESC.Tex2D(ShadowAtlasResourceFormat,(ulong)Width,(uint)Height,mipLevels:1);
        Desc.Flags|=D3D12_RESOURCE_FLAG_ALLOW_DEPTH_STENCIL;
        var Heap=new D3D12_HEAP_PROPERTIES(D3D12_HEAP_TYPE_DEFAULT);
        var Clear=new D3D12_CLEAR_VALUE(ShadowAtlasDsvFormat,1f,0);
        ID3D12Resource*Texture;
        D3D12Diagnostics.EnsureSuccess(this.Device!.Device->CreateCommittedResource(&Heap,D3D12_HEAP_FLAG_NONE,&Desc,State,&Clear,__uuidof<ID3D12Resource>(),(void**)&Texture),nameof(this.CreateShadowDepthTexture),Context);
        var View=new D3D12_DEPTH_STENCIL_VIEW_DESC { Format=ShadowAtlasDsvFormat,ViewDimension=D3D12_DSV_DIMENSION.D3D12_DSV_DIMENSION_TEXTURE2D };
        this.Device.Device->CreateDepthStencilView(Texture,&View,new D3D12_CPU_DESCRIPTOR_HANDLE(this.ShadowPageDsvHeap->GetCPUDescriptorHandleForHeapStart(),DsvIndex,this.DsvDescriptorSize));
        return Texture;
    }
    private void DisposeVirtualShadowMaps(){
        if(this.ShadowPageAtlas!=null){ _=this.ShadowPageAtlas->Release();this.ShadowPageAtlas=null; }
        if(this.ShadowPageReference!=null){ _=this.ShadowPageReference->Release();this.ShadowPageReference=null; }
        if(this.ShadowPageDsvHeap!=null){ _=this.ShadowPageDsvHeap->Release();this.ShadowPageDsvHeap=null; }
        if(this.ShadowPageTableBuffer!=null){ _=this.ShadowPageTableBuffer->Release();this.ShadowPageTableBuffer=null; }
        if(this.ShadowPageProjectionBuffer!=null){ _=this.ShadowPageProjectionBuffer->Release();this.ShadowPageProjectionBuffer=null; }
        this.ShadowPageAtlasPages=0;
        this.ShadowPageReferenceSize=0;
        Array.Clear(this.ShadowRequestSlotRecorded);
        this.ShadowMapRequests.Clear();
        this.ShadowMapPlanner.Reset();
    }
    /// <summary>主パスの後: このフレームの段の行列で、距離の画像からページの要求を作る。計算の束縛に置き換わるので印を立てる。</summary>
    private void RecordVirtualShadowRequests(ref FrameRecording Recording){
        if(!this.ShadowMapPlannedThisFrame){ return; }
        var Origins=this.ShadowRequestSlotOrigins[this.FrameIndex];
        for(var Level=0;Level<VirtualShadowMapPlanner.LevelCount;Level++){
            Origins[Level]=this.ShadowMapPlanner.WindowOrigin(Level);
            this.ShadowRequestOrigins[Level]=Origins[Level];
            this.ShadowRequestProjections[Level]=this.ShadowMapPlanner.Projections[Level];
        }
        Matrix4x4.Invert(this.InvertibleViewProjection,out var Inverse);
        var Metric=VirtualGeometryMetric(this.CurrentViewProjection,this.Height);
        this.ShadowRequests!.Record(this.CommandList,this.FrameIndex,this.SceneDistanceBuffer,this.Width,this.Height,Inverse,this.ShadowMapPlanner.Projections,this.CurrentCameraPosition,Metric.ShaderParameters.X);
        this.ShadowRequestSlotRecorded[this.FrameIndex]=true;
        Recording.RanComputeAfterMainPass=true;
    }
    /// <summary>
    /// 試験用: 最後のフレームの要求の印、そのフレームの段の窓の位置と行列、要求を作った逆ビュー投影・カメラの位置・1 m 先の 1 m の画素数。GPU の完了を待って読む。
    /// </summary>
    internal (uint[]Bits,(int X,int Y)[]Origins,Matrix4x4[]Projections,Matrix4x4 InverseViewProjection,Vector3 Camera,float PixelsPerMeter)ReadVirtualShadowRequestsForTesting(){
        this.ThrowIfDisposed();
        this.WaitForGpuIdle();
        Matrix4x4.Invert(this.InvertibleViewProjection,out var Inverse);
        return (this.ShadowRequests!.ReadLatestForTesting(),[..this.ShadowRequestOrigins],[..this.ShadowRequestProjections],Inverse,this.CurrentCameraPosition,
            VirtualGeometryMetric(this.CurrentViewProjection,this.Height).ShaderParameters.X);
    }
    /// <summary>
    /// 試験用: 最後のフレームのページ表（GPU へ上げた枠から）、段ごとの窓の位置、置き場の深度（幅 <c>AtlasWidth</c> の行優先）、比べる深度（無ければ null）、計画の統計。GPU の完了を待って読む。
    /// </summary>
    internal (uint[]Table,(int X,int Y)[]Origins,float[]Atlas,int AtlasWidth,float[]?Reference,VirtualShadowStatistics Statistics)ReadVirtualShadowMapForTesting(){
        this.ThrowIfDisposed();
        this.WaitForGpuIdle();
        if(this.ShadowPageAtlas==null){ throw new InvalidOperationException("No virtual shadow map page has been planned."); }
        var Table=new ReadOnlySpan<uint>(this.ShadowPageTableMapped+(ulong)this.ShadowPageLastFrameIndex*VirtualShadowMapPlanner.TableLength*4,VirtualShadowMapPlanner.TableLength).ToArray();
        var Origins=Enumerable.Range(0,VirtualShadowMapPlanner.LevelCount).Select(this.ShadowMapPlanner.WindowOrigin).ToArray();
        var (Atlas,AtlasWidth)=this.ReadDepthTexture(this.ShadowPageAtlas,D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE);
        float[]?Reference=null;
        if(this.ShadowPageReference!=null){ Reference=this.ReadDepthTexture(this.ShadowPageReference,D3D12_RESOURCE_STATE_DEPTH_WRITE).Depth; }
        return (Table,Origins,Atlas,AtlasWidth,Reference,this.ShadowMapPlanner.Statistics);
    }
    private (float[]Depth,int Width)ReadDepthTexture(ID3D12Resource*Texture,D3D12_RESOURCE_STATES RestingState){
        var (Data,RowPitch,Width,Height)=GpuTextureReadback.Read(this.Device!,Texture,RestingState);
        var Result=new float[Width*Height];
        for(var Row=0;Row<Height;Row++){
            MemoryMarshal.Cast<byte,float>(Data.AsSpan(Row*RowPitch,Width*4)).CopyTo(Result.AsSpan(Row*Width,Width));
        }
        return (Result,Width);
    }
}
