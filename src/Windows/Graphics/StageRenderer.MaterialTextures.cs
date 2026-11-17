using System.Numerics;
using CSharpGameEngine.Rendering;
using CSharpGameEngine.Rendering.Pipeline;
using TerraFX.Interop.DirectX;
using static CSharpGameEngine.Rendering.Pipeline.StageGpuLayouts;
using static TerraFX.Interop.Windows.Windows;
using static TerraFX.Interop.DirectX.D3D12_DESCRIPTOR_HEAP_FLAGS;
using static TerraFX.Interop.DirectX.D3D12_DESCRIPTOR_HEAP_TYPE;
using static TerraFX.Interop.DirectX.D3D12_SRV_DIMENSION;
using static TerraFX.Interop.DirectX.DirectX;
namespace CSharpGameEngine.Graphics.D3D12;
/// <summary>
/// 材質のテクスチャの番号引き（ADR-0100 決定 2、M0129 単位 4）。シェーダーは材質の表に書いた番号で 1 本の配列（t0 space1）を引くので、
/// 描画のたびにテクスチャの束を切り替えない。記述子の置き場の並びは「環境の表（t12〜t19）、影の画像（t4）、材質のテクスチャ」。
/// 材質とテクスチャが足りなくなったら置き場と表を広げるので、材質の数に上限は無い。
/// </summary>
public sealed unsafe partial class StageRenderer {
    /// <summary>番号引きの配列が使う D3D12 のリソース束縛の段の下限。上限の無い SRV の配列は段 2 から使える。</summary>
    internal const int RequiredResourceBindingTier=2;
    /// <summary>環境の表の中で、影の画像（t4）を置く位置。t12〜t19 の後ろ。</summary>
    private const int ShadowAtlasEnvironmentSlot=EnvironmentDescriptorCount;
    /// <summary>材質のテクスチャの配列が始まる、記述子の置き場の位置。</summary>
    private const int MaterialTextureDescriptorBase=EnvironmentDescriptorCount+1;
    /// <summary>テクスチャ 1 枚の見え方。基本色と発光は sRGB として、法線と metallic・粗さはそのままの値として読む（材質のどの欄で使うかで決まる）。</summary>
    private readonly List<(nint Resource,bool Srgb)>MaterialTextureViews=[];
    private readonly Dictionary<(nint Resource,bool Srgb),int>MaterialTextureViewIndices=[];
    /// <summary>材質ごとの 4 つのテクスチャの番号（基本色、法線、metallic と粗さ、発光）。</summary>
    private StageUInt4[]MaterialTextureIndices=[];
    /// <summary>記述子の置き場が、材質のテクスチャを何枚まで置けるか。</summary>
    private int MaterialTextureViewCapacity;
    /// <summary>デバイスが番号引きの配列を使えないなら、描画器を作る前に理由を添えて止める。段 1 の GPU は DirectX 12 対応の GPU のごく一部。</summary>
    internal static void RequireResourceBindingTier(int Tier,string AdapterDescription){
        if(Tier<RequiredResourceBindingTier){
            throw new NotSupportedException($"The GPU '{AdapterDescription}' reports D3D12 resource binding tier {Tier}; the Stage renderer needs tier {RequiredResourceBindingTier} or higher for its material texture array (ADR-0100 decision 2).");
        }
    }
    /// <summary>材質の配列と、材質のテクスチャを置ける枚数を初めて決める。</summary>
    private void CreateMaterialStorage(int MaterialCapacity){
        this.MaterialCapacity=MaterialCapacity;
        this.MaterialTextureIndices=new StageUInt4[MaterialCapacity];
        this.MaterialBaseColorFactors=new Vector4[MaterialCapacity];
        this.MaterialRoughnessFactors=new float[MaterialCapacity];
        this.MaterialMetallicFactors=new float[MaterialCapacity];
        this.MaterialEmissiveNits=new Vector3[MaterialCapacity];
        this.MaterialWorldGrids=new StageWorldGrid?[MaterialCapacity];
        this.MaterialWorldGridAnchors=new Vector3[MaterialCapacity];
        this.SrvDescriptorSize=this.Device!.Device->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV);
        this.CreateMaterialDescriptorHeap(MaterialCapacity*4);
    }
    /// <summary>
    /// 材質をもう 1 つ置けるようにする。足りなければ倍に広げ、材質の表も作り直す。古い表は、それを読んだかもしれないフレームが済んでから放す（M0129 単位 5）。
    /// </summary>
    private void EnsureMaterialCapacity(int Count){
        if(Count<=this.MaterialCapacity){ return; }
        var Capacity=Math.Max(Count,this.MaterialCapacity*2);
        Array.Resize(ref this.MaterialTextureIndices,Capacity);
        Array.Resize(ref this.MaterialBaseColorFactors,Capacity);
        Array.Resize(ref this.MaterialRoughnessFactors,Capacity);
        Array.Resize(ref this.MaterialMetallicFactors,Capacity);
        Array.Resize(ref this.MaterialEmissiveNits,Capacity);
        Array.Resize(ref this.MaterialWorldGrids,Capacity);
        Array.Resize(ref this.MaterialWorldGridAnchors,Capacity);
        this.MaterialCapacity=Capacity;
        if(this.MaterialTableBuffer!=null){
            this.MaterialTableBuffer->Unmap(0,null);
            this.MaterialTableMapped=null;
            this.Retire(this.MaterialTableBuffer);
            this.MaterialTableBuffer=null;
            this.CreateUploadBuffer(checked(MaterialDataSize*(uint)this.MaterialCapacity*FrameCount),out this.MaterialTableBuffer,out this.MaterialTableMapped,"growing the material table");
        }
    }
    /// <summary>
    /// 記述子の置き場を作る（広げるときは作り直す）。すべての材質のテクスチャと環境の表を書き直す。古い置き場は、それを使ったかもしれない
    /// フレームが済んでから放す（M0129 単位 5）。
    /// </summary>
    private void CreateMaterialDescriptorHeap(int TextureViewCapacity){
        D3D12_DESCRIPTOR_HEAP_DESC Desc=new(){
            Type=D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV,
            NumDescriptors=checked((uint)(MaterialTextureDescriptorBase+TextureViewCapacity)),
            Flags=D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE,
        };
        ID3D12DescriptorHeap*Heap;
        D3D12Diagnostics.EnsureSuccess(this.Device!.Device->CreateDescriptorHeap(&Desc,__uuidof<ID3D12DescriptorHeap>(),(void**)&Heap),
            "ID3D12Device::CreateDescriptorHeap","creating the material SRV descriptor heap");
        this.Retire(this.SrvHeap);
        this.SrvHeap=Heap;
        this.MaterialTextureViewCapacity=TextureViewCapacity;
        for(var Index=0;Index<this.MaterialTextureViews.Count;Index++){ this.WriteMaterialTextureView(Index); }
        if(this.GlobalIllumination!=null){ this.WriteEnvironmentDescriptors(); }
    }
    /// <summary>テクスチャの見え方 1 つの番号を返す。初めての見え方なら置き場に書く（置き場が足りなければ広げる）。</summary>
    private uint GetOrAddMaterialTextureView(nint Resource,bool Srgb){
        if(this.MaterialTextureViewIndices.TryGetValue((Resource,Srgb),out var Existing)){ return (uint)Existing; }
        var Index=this.MaterialTextureViews.Count;
        this.MaterialTextureViews.Add((Resource,Srgb));
        this.MaterialTextureViewIndices.Add((Resource,Srgb),Index);
        if(Index>=this.MaterialTextureViewCapacity){ this.CreateMaterialDescriptorHeap(Math.Max(Index+1,this.MaterialTextureViewCapacity*2)); }
        else { this.WriteMaterialTextureView(Index); }
        return (uint)Index;
    }
    /// <summary>材質を足すのに失敗したとき、その途中で増えた見え方を消す。置き場の書いた記述子は、次に使うときに上書きされる。</summary>
    private void TruncateMaterialTextureViews(int Count){
        for(var Index=this.MaterialTextureViews.Count-1;Index>=Count;Index--){
            this.MaterialTextureViewIndices.Remove(this.MaterialTextureViews[Index]);
            this.MaterialTextureViews.RemoveAt(Index);
        }
    }
    private void WriteMaterialTextureView(int Index){
        var (Resource,Srgb)=this.MaterialTextureViews[Index];
        D3D12_SHADER_RESOURCE_VIEW_DESC Desc=new(){
            Format=Srgb?MaterialColorTextureViewFormat:MaterialTextureFormat,
            ViewDimension=D3D12_SRV_DIMENSION_TEXTURE2D,
            Shader4ComponentMapping=(uint)TerraFX.Interop.DirectX.D3D12.D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING,
        };
        // テクスチャの全ミップ（M0110）。-1はD3D12で「最も詳細なレベルからすべて」を意味する。
        Desc.Texture2D.MipLevels=uint.MaxValue;
        var Handle=new D3D12_CPU_DESCRIPTOR_HANDLE(this.SrvHeap->GetCPUDescriptorHandleForHeapStart(),MaterialTextureDescriptorBase+Index,this.SrvDescriptorSize);
        this.Device!.Device->CreateShaderResourceView((ID3D12Resource*)Resource,&Desc,Handle);
    }
    /// <summary>影の画像（t4）の見え方を、環境の表の末尾に書く。</summary>
    private void WriteShadowAtlasEnvironmentDescriptor(){
        D3D12_SHADER_RESOURCE_VIEW_DESC Desc=new(){
            Format=ShadowAtlasSrvFormat,
            ViewDimension=D3D12_SRV_DIMENSION_TEXTURE2D,
            Shader4ComponentMapping=(uint)TerraFX.Interop.DirectX.D3D12.D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING,
        };
        Desc.Texture2D.MipLevels=1;
        var Handle=new D3D12_CPU_DESCRIPTOR_HANDLE(this.SrvHeap->GetCPUDescriptorHandleForHeapStart(),ShadowAtlasEnvironmentSlot,this.SrvDescriptorSize);
        this.Device!.Device->CreateShaderResourceView(this.ShadowAtlas,&Desc,Handle);
    }
    private D3D12_GPU_DESCRIPTOR_HANDLE GetMaterialTextureTable()=>
        new(this.SrvHeap->GetGPUDescriptorHandleForHeapStart(),MaterialTextureDescriptorBase,this.SrvDescriptorSize);
    /// <summary>試験用: 記述子の置き場が今置ける材質のテクスチャの枚数と、置いている枚数。</summary>
    internal (int Capacity,int Count)MaterialTextureViewsForTesting=>(this.MaterialTextureViewCapacity,this.MaterialTextureViews.Count);
}
