using System.Numerics;
using CSharpGameEngine.Rendering;
using CSharpGameEngine.ShaderArtifacts;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.DirectX.D3D12_DESCRIPTOR_HEAP_FLAGS;
using static TerraFX.Interop.DirectX.D3D12_DESCRIPTOR_HEAP_TYPE;
using static TerraFX.Interop.DirectX.D3D12_DESCRIPTOR_RANGE_TYPE;
using static TerraFX.Interop.DirectX.D3D12_HEAP_FLAGS;
using static TerraFX.Interop.DirectX.D3D12_HEAP_TYPE;
using static TerraFX.Interop.DirectX.D3D12_RESOURCE_FLAGS;
using static TerraFX.Interop.DirectX.D3D12_RESOURCE_STATES;
using static TerraFX.Interop.DirectX.D3D12_ROOT_PARAMETER_TYPE;
using static TerraFX.Interop.DirectX.D3D12_SHADER_VISIBILITY;
using static TerraFX.Interop.DirectX.DirectX;
using static TerraFX.Interop.DirectX.DXGI_FORMAT;
using static TerraFX.Interop.Windows.Windows;
namespace CSharpGameEngine.Graphics.D3D12;
/// <summary>
/// グローバル距離場（ADR-0078/M0110）。M0111 の Lumen 相当追跡処理用に、シーンの符号付き距離場クリップマップをカメラ周囲に GPU 上で生成する。
/// GlobalDistanceField.hlsl が描画オブジェクトのメッシュ距離場から作成する。各レベルは解像度^3個の半精度浮動小数点数を持つ Texture3D で、下位レベルの2倍の
/// ボクセルサイズを持つ。ワールドのボクセル格子に固定し、トロイダル方式でアドレス指定する（ワールド座標 g のボクセルはテクセル g mod Resolution）。毎フレーム再計算するのは変化した
/// 8^3ボクセルのブリックだけ。カメラ移動で新たに覆うブリック（各レベルのクリップ範囲はブリック単位でスナップする）と、オブジェクト移動、出現、消失の位置から4ボクセル以内にあるブリックが対象。ブリック
/// は影響するオブジェクトから毎回再計算するため、どのような更新順でも最終的な距離場は一度に全体を作った場合と一致する。M0111 の読み取り内容（
/// <see cref="ClipmapConstants"/> と HLSL の SampleGlobalDistance / TraceGlobalDistanceField を参照）：
/// シェーダーリソース状態の各レベルのテクスチャ（<see cref="ShaderResourceState"/>）と、レベルごとのボクセルサイズ、切り捨て距離、解像度、レンダリング空間でのクリップ角、
/// 角に対応するテクセル。ルートシグネチャは GlobalDistanceField.hlsl を参照。
/// </summary>
internal sealed unsafe class StageGlobalDistanceField:IDisposable {
    internal const string ArtifactFileName="GlobalDistanceField.cgeshaderartifact";
    internal const int BrickSize=8;
    /// <summary>フィールドがそのレベルのボクセル何個分まで距離を保持するか。それより遠い値はこの上限にする。</summary>
    internal const float TruncationVoxels=4f;
    internal const D3D12_RESOURCE_STATES ShaderResourceState=D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE|D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
    private const DXGI_FORMAT FieldFormat=DXGI_FORMAT_R16_FLOAT;
    private const int MaximumLevels=GlobalDistanceFieldSettings.MaximumClipmapCount;
    private const int MeshRecordSize=32;
    private const int ObjectRecordSize=64;
    private const int BrickRecordSize=32;
    private const uint ConstantsStride=256;
    private const int ConstantsPerFrame=MaximumLevels+1;
    private const int MaximumTraceRays=512;
    private readonly GraphicsDevice Device;
    private readonly string ArtifactPath;
    private readonly uint FrameCount;
    private ID3D12RootSignature*RootSignature;
    private ID3D12PipelineState*PipelineState;
    private ID3D12DescriptorHeap*DescriptorHeap;
    private uint DescriptorSize;
    private readonly ID3D12Resource*[]Levels=new ID3D12Resource*[MaximumLevels];
    private (float VoxelSize,int Count,int Resolution)?Configuration;
    private readonly List<MeshEntry?>MeshEntries=[];
    private readonly List<ushort>MeshHalves=[];
    private readonly List<MeshDistanceField>MeshFields=[];
    private bool MeshDataDirty;
    private ID3D12Resource*MeshRecordBuffer;
    private ID3D12Resource*MeshDistanceBuffer;
    private UploadRing ObjectRing;
    private UploadRing BrickRing;
    private UploadRing BrickObjectRing;
    private ID3D12Resource*ConstantsBuffer;
    private byte*ConstantsMapped;
    private ID3D12Resource*DummyBuffer;
    private readonly LongVector3[]ClipMinimum=new LongVector3[MaximumLevels];
    private readonly bool[]ClipValid=new bool[MaximumLevels];
    private ObjectState[]Objects=[];
    private LongVector3 PreviousOrigin;
    private Vector3 PreviousRemainder;
    private bool HasPreviousOrigin;
    private bool Disposed;
    internal StageGlobalDistanceField(GraphicsDevice Device,string StageArtifactPath,uint FrameCount){
        this.Device=Device;
        this.FrameCount=FrameCount;
        this.ArtifactPath=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(StageArtifactPath))??string.Empty,ArtifactFileName);
    }
    /// <summary>直近の <see cref="Update"/> でレベルごとに再計算したブリック（診断とテスト用）。</summary>
    internal int[]LastUpdatedBricks { get; }=new int[MaximumLevels];
    /// <summary>直近の <see cref="Update"/> で距離場に含まれていたオブジェクト。</summary>
    internal int LastFieldObjects { get;private set; }
    /// <summary>距離場を持つレンダラーのメッシュ。</summary>
    internal int MeshesWithFields=>this.MeshFields.Count;
    internal int LevelCount=>this.Configuration?.Count??0;
    internal int Resolution=>this.Configuration?.Resolution??0;
    /// <summary>レベルごとの値（x はボクセルサイズ、y は切り捨て距離、z は解像度、w は 1 / 解像度）、レンダリング空間でのクリップ角、および角に対応するテクセル。</summary>
    internal (Vector4 Scale,Vector4 Origin,Vector4 Texel)[]ClipmapConstants { get; }=new (Vector4,Vector4,Vector4)[MaximumLevels];
    internal ID3D12Resource*Level(int Index)=>this.Levels[Index];
    /// <summary>
    /// M0111：<see cref="MeshRecordAddress"/> にあるレンダラーメッシュの記録（距離場の境界、ボクセルサイズ、解像度、
    /// <see cref="MeshDistanceAddress"/> 内のオフセット）。距離場を持たないメッシュでは false。次の <see cref="Update"/> 以降に有効。
    /// </summary>
    internal bool TryGetMeshRecord(int MeshIndex,out int Record){
        Record=-1;
        if((uint)MeshIndex>=(uint)this.MeshEntries.Count||this.MeshEntries[MeshIndex] is not{ }Entry){
            return false;
        }
        Record=Entry.Record;
        return true;
    }
    /// <summary><see cref="Update"/> 後のメッシュ記録（各32バイト。GlobalDistanceField.hlsl の MeshRecord を参照）。更新前は0。</summary>
    internal ulong MeshRecordAddress=>this.MeshRecordBuffer!=null?this.MeshRecordBuffer->GetGPUVirtualAddress():0;
    /// <summary><see cref="Update"/> 後のメッシュ距離（半精度浮動小数点数。ワード単位で読み取る）。更新前は0。</summary>
    internal ulong MeshDistanceAddress=>this.MeshDistanceBuffer!=null?this.MeshDistanceBuffer->GetGPUVirtualAddress():0;
    /// <summary>レンダラーメッシュ1つの距離場を登録する（持たない場合は null）。次の <see cref="Update"/> 以降に使用する。</summary>
    internal void SetMesh(int MeshIndex,MeshDistanceField?Field){
        while(this.MeshEntries.Count<=MeshIndex){
            this.MeshEntries.Add(null);
        }
        if(Field is null){
            this.MeshEntries[MeshIndex]=null;
            return;
        }
        var Record=this.MeshFields.Count;
        this.MeshFields.Add(Field);
        var Offset=this.MeshHalves.Count;
        foreach(var Value in Field.Distances){
            this.MeshHalves.Add(BitConverter.HalfToUInt16Bits(Value));
        }
        this.MeshEntries[MeshIndex]=new MeshEntry(Record,Offset,Field.BoundsMin,Field.BoundsMax);
        this.MeshDataDirty=true;
    }
    /// <summary>
    /// このフレーム向けに距離場を更新する。設定が変わった場合は各レベルを再作成し、新しいメッシュ距離場をアップロードし、カメラとオブジェクトの移動を反映し、変化したブリックを再計算する。フレーム処理中の
    /// GPU が読み取る可能性のあるリソースを置き換える前に、<paramref name="WaitForGpuIdle"/> を呼び出す。すべてのレベルを
    /// <see cref="ShaderResourceState"/> の状態にして完了する。
    /// </summary>
    internal void Update(ID3D12GraphicsCommandList*CommandList,uint FrameIndex,in GlobalDistanceFieldSettings Settings,
        ReadOnlySpan<bool>Active,ReadOnlySpan<int>MeshIndices,ReadOnlySpan<Matrix4x4>Transforms,int ObjectCount,Action WaitForGpuIdle){
        this.EnsurePipeline();
        var Wanted=(Settings.VoxelSizeMeters,Settings.ClipmapCount,Settings.Resolution);
        if(this.Configuration!=Wanted){
            if(this.Configuration is not null){
                WaitForGpuIdle();
            }
            this.CreateLevels(Wanted);
        }
        if(this.MeshDataDirty){
            if(this.MeshRecordBuffer!=null){
                WaitForGpuIdle();
            }
            this.UploadMeshes();
        }
        var Count=Settings.ClipmapCount;
        var Resolution=Settings.Resolution;
        var BaseVoxel=Settings.VoxelSizeMeters;
        // 各レベルの専用格子での原点 O_L = floor(O_0 / 2^L) と、レンダリング空間の原点から O_L の角までの距離。
        Span<LongVector3>Origin=stackalloc LongVector3[MaximumLevels];
        Span<Vector3>Remainder=stackalloc Vector3[MaximumLevels];
        for(var Level=0;Level<Count;Level++){
            var Scale=1L<<Level;
            var LevelOrigin=new LongVector3(FloorDivide(Settings.OriginVoxel.X,Scale),FloorDivide(Settings.OriginVoxel.Y,Scale),FloorDivide(Settings.OriginVoxel.Z,Scale));
            Origin[Level]=LevelOrigin;
            Remainder[Level]=Settings.OriginRemainder+new Vector3(Settings.OriginVoxel.X-LevelOrigin.X*Scale,Settings.OriginVoxel.Y-LevelOrigin.Y*Scale,Settings.OriginVoxel.Z-LevelOrigin.Z*Scale)*BaseVoxel;
        }
        var Dirty=new HashSet<LongVector3>[Count];
        var BricksPerSide=Resolution/BrickSize;
        for(var Level=0;Level<Count;Level++){
            Dirty[Level]=[];
            var Half=Resolution/2;
            var Minimum=new LongVector3(FloorDivide(Origin[Level].X-Half,BrickSize)*BrickSize,FloorDivide(Origin[Level].Y-Half,BrickSize)*BrickSize,FloorDivide(Origin[Level].Z-Half,BrickSize)*BrickSize);
            var Previous=this.ClipMinimum[Level];
            var WasValid=this.ClipValid[Level];
            for(var Z=0;Z<BricksPerSide;Z++){
                for(var Y=0;Y<BricksPerSide;Y++){
                    for(var X=0;X<BricksPerSide;X++){
                        var Brick=new LongVector3(Minimum.X/BrickSize+X,Minimum.Y/BrickSize+Y,Minimum.Z/BrickSize+Z);
                        if(!WasValid||!InsideClip(Brick,Previous,BricksPerSide)){
                            Dirty[Level].Add(Brick);
                        }
                    }
                }
            }
            this.ClipMinimum[Level]=Minimum;
            this.ClipValid[Level]=true;
        }
        // 前フレームからカメラがワールド上を移動した距離。この分だけ、静止物体のレンダリング空間上の平行移動が逆方向へ移動する。
        var CameraDelta=this.HasPreviousOrigin
            ?new Vector3(Settings.OriginVoxel.X-this.PreviousOrigin.X,Settings.OriginVoxel.Y-this.PreviousOrigin.Y,Settings.OriginVoxel.Z-this.PreviousOrigin.Z)*BaseVoxel
                +(Settings.OriginRemainder-this.PreviousRemainder)
            :Vector3.Zero;
        if(this.Objects.Length<ObjectCount){
            Array.Resize(ref this.Objects,Math.Max(ObjectCount,this.Objects.Length*2));
        }
        List<int>FieldObjects=[];
        for(var Index=0;Index<this.Objects.Length;Index++){
            ref var State=ref this.Objects[Index];
            var Include=Index<ObjectCount&&Active[Index]&&MeshIndices[Index]>=0&&MeshIndices[Index]<this.MeshEntries.Count&&this.MeshEntries[MeshIndices[Index]] is not null
                &&IsFinite(Transforms[Index])&&Matrix4x4.Invert(Transforms[Index],out var Inverse)&&IsFinite(Inverse);
            if(!Include){
                if(State.Tracked){
                    this.MarkDirty(Dirty,State.GlobalMinimum,State.GlobalMaximum,Count);
                    State.Tracked=false;
                }
                continue;
            }
            var Entry=this.MeshEntries[MeshIndices[Index]]!.Value;
            var Matrix=Transforms[Index];
            var (RenderMinimum,RenderMaximum)=TransformBounds(Entry.BoundsMin,Entry.BoundsMax,Matrix);
            var GlobalMinimum=ToGlobal(RenderMinimum,Settings);
            var GlobalMaximum=ToGlobal(RenderMaximum,Settings);
            var Moved=!State.Tracked||State.MeshIndex!=MeshIndices[Index]||!SamePlacement(State.Matrix,Matrix,CameraDelta);
            if(Moved){
                if(State.Tracked){
                    this.MarkDirty(Dirty,State.GlobalMinimum,State.GlobalMaximum,Count);
                }
                this.MarkDirty(Dirty,GlobalMinimum,GlobalMaximum,Count);
            }
            State=new ObjectState(true,MeshIndices[Index],Matrix,GlobalMinimum,GlobalMaximum);
            FieldObjects.Add(Index);
        }
        this.PreviousOrigin=Settings.OriginVoxel;
        this.PreviousRemainder=Settings.OriginRemainder;
        this.HasPreviousOrigin=true;
        this.LastFieldObjects=FieldObjects.Count;
        // このフレームで距離場に含まれるオブジェクトごとに1件ずつ記録し、その後、対象オブジェクトが到達するブリックを記録する。
        this.ObjectRing.Ensure(this,FieldObjects.Count*ObjectRecordSize,WaitForGpuIdle);
        var ObjectSlot=this.ObjectRing.Slot(FrameIndex);
        var RecordOf=new Dictionary<int,int>(FieldObjects.Count);
        for(var Record=0;Record<FieldObjects.Count;Record++){
            var Index=FieldObjects[Record];
            RecordOf[Index]=Record;
            Matrix4x4.Invert(Transforms[Index],out var Inverse);
            var Target=(float*)(ObjectSlot+Record*ObjectRecordSize);
            Target[0]=Inverse.M11;
            Target[1]=Inverse.M21;
            Target[2]=Inverse.M31;
            Target[3]=Inverse.M41;
            Target[4]=Inverse.M12;
            Target[5]=Inverse.M22;
            Target[6]=Inverse.M32;
            Target[7]=Inverse.M42;
            Target[8]=Inverse.M13;
            Target[9]=Inverse.M23;
            Target[10]=Inverse.M33;
            Target[11]=Inverse.M43;
            ((uint*)Target)[12]=(uint)this.MeshEntries[MeshIndices[Index]]!.Value.Record;
            Target[13]=MinimumScale(Transforms[Index]);
            Target[14]=0f;
            Target[15]=0f;
        }
        List<(int Level,LongVector3 Brick,int ObjectStart,int ObjectCount)>Bricks=[];
        List<uint>BrickObjects=[];
        for(var Level=0;Level<Count;Level++){
            var LevelScale=1L<<Level;
            var Reach=(long)TruncationVoxels;
            foreach(var Brick in Dirty[Level].Where(Brick=>InsideClip(Brick,this.ClipMinimum[Level],BricksPerSide)).OrderBy(Brick=>Brick.Z).ThenBy(Brick=>Brick.Y).ThenBy(Brick=>Brick.X)){
                var Start=BrickObjects.Count;
                var VoxelMinimum=new LongVector3(Brick.X*BrickSize,Brick.Y*BrickSize,Brick.Z*BrickSize);
                foreach(var Index in FieldObjects){
                    ref var State=ref this.Objects[Index];
                    var Minimum=new LongVector3(FloorDivide(State.GlobalMinimum.X,LevelScale)-Reach,FloorDivide(State.GlobalMinimum.Y,LevelScale)-Reach,FloorDivide(State.GlobalMinimum.Z,LevelScale)-Reach);
                    var Maximum=new LongVector3(FloorDivide(State.GlobalMaximum.X,LevelScale)+Reach,FloorDivide(State.GlobalMaximum.Y,LevelScale)+Reach,FloorDivide(State.GlobalMaximum.Z,LevelScale)+Reach);
                    if(Maximum.X<VoxelMinimum.X||Minimum.X>=VoxelMinimum.X+BrickSize
                        ||Maximum.Y<VoxelMinimum.Y||Minimum.Y>=VoxelMinimum.Y+BrickSize
                        ||Maximum.Z<VoxelMinimum.Z||Minimum.Z>=VoxelMinimum.Z+BrickSize){
                        continue;
                    }
                    BrickObjects.Add((uint)RecordOf[Index]);
                }
                Bricks.Add((Level,Brick,Start,BrickObjects.Count-Start));
            }
        }
        this.BrickRing.Ensure(this,Bricks.Count*BrickRecordSize,WaitForGpuIdle);
        this.BrickObjectRing.Ensure(this,BrickObjects.Count*4,WaitForGpuIdle);
        var BrickSlot=this.BrickRing.Slot(FrameIndex);
        for(var Index=0;Index<Bricks.Count;Index++){
            var (Level,Brick,Start,Objects)=Bricks[Index];
            var VoxelMinimum=new LongVector3(Brick.X*BrickSize,Brick.Y*BrickSize,Brick.Z*BrickSize);
            var Target=(int*)(BrickSlot+Index*BrickRecordSize);
            Target[0]=(int)(VoxelMinimum.X-Origin[Level].X);
            Target[1]=(int)(VoxelMinimum.Y-Origin[Level].Y);
            Target[2]=(int)(VoxelMinimum.Z-Origin[Level].Z);
            Target[3]=Start;
            Target[4]=(int)PositiveModulo(VoxelMinimum.X,Resolution);
            Target[5]=(int)PositiveModulo(VoxelMinimum.Y,Resolution);
            Target[6]=(int)PositiveModulo(VoxelMinimum.Z,Resolution);
            Target[7]=Objects;
        }
        var BrickObjectSlot=this.BrickObjectRing.Slot(FrameIndex);
        for(var Index=0;Index<BrickObjects.Count;Index++){
            ((uint*)BrickObjectSlot)[Index]=BrickObjects[Index];
        }
        // 定数M0111とトレースで読み取る値：各レベルのボクセル、トランケーション距離、描画空間でのクリップ角、およびその角に対応するテクセル。
        for(var Level=0;Level<MaximumLevels;Level++){
            if(Level>=Count){
                this.ClipmapConstants[Level]=(Vector4.Zero,Vector4.Zero,Vector4.Zero);
                continue;
            }
            var Voxel=BaseVoxel*(1<<Level);
            var Minimum=this.ClipMinimum[Level];
            var Corner=new Vector3(Minimum.X-Origin[Level].X,Minimum.Y-Origin[Level].Y,Minimum.Z-Origin[Level].Z)*Voxel-Remainder[Level];
            this.ClipmapConstants[Level]=(new Vector4(Voxel,Voxel*TruncationVoxels,Resolution,1f/Resolution),new Vector4(Corner,0f),
                new Vector4(PositiveModulo(Minimum.X,Resolution),PositiveModulo(Minimum.Y,Resolution),PositiveModulo(Minimum.Z,Resolution),0f));
        }
        Array.Clear(this.LastUpdatedBricks);
        var First=0;
        for(var Level=0;Level<Count;Level++){
            var LevelBricks=0;
            while(First+LevelBricks<Bricks.Count&&Bricks[First+LevelBricks].Level==Level){
                LevelBricks++;
            }
            this.LastUpdatedBricks[Level]=LevelBricks;
            var Constants=this.WriteConstants(FrameIndex,Level,Pass:0,BrickStart:(uint)First,RayCount:0,BaseVoxel*(1<<Level),Remainder[Level],Count);
            if(LevelBricks>0){
                this.Dispatch(CommandList,FrameIndex,Level,Constants,(uint)LevelBricks);
            }
            First+=LevelBricks;
        }
    }
    private void Dispatch(ID3D12GraphicsCommandList*CommandList,uint FrameIndex,int Level,ulong Constants,uint BrickCount){
        D3D12_RESOURCE_BARRIER ToUav=D3D12_RESOURCE_BARRIER.InitTransition(this.Levels[Level],ShaderResourceState,D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
        CommandList->ResourceBarrier(1,&ToUav);
        this.Bind(CommandList,Constants,FrameIndex,UavSlot:(uint)(MaximumLevels+Level),TraceResults:this.DummyBuffer->GetGPUVirtualAddress(),TraceRays:this.DummyBuffer->GetGPUVirtualAddress());
        CommandList->Dispatch(BrickCount,1,1);
        D3D12_RESOURCE_BARRIER ToSrv=D3D12_RESOURCE_BARRIER.InitTransition(this.Levels[Level],D3D12_RESOURCE_STATE_UNORDERED_ACCESS,ShaderResourceState);
        CommandList->ResourceBarrier(1,&ToSrv);
    }
    private void Bind(ID3D12GraphicsCommandList*CommandList,ulong Constants,uint FrameIndex,uint UavSlot,ulong TraceResults,ulong TraceRays){
        CommandList->SetComputeRootSignature(this.RootSignature);
        CommandList->SetPipelineState(this.PipelineState);
        ID3D12DescriptorHeap*Heap=this.DescriptorHeap;
        CommandList->SetDescriptorHeaps(1,&Heap);
        var Start=this.DescriptorHeap->GetGPUDescriptorHandleForHeapStart();
        CommandList->SetComputeRootConstantBufferView(0,Constants);
        CommandList->SetComputeRootShaderResourceView(1,this.BrickRing.Address(FrameIndex,this.DummyBuffer));
        CommandList->SetComputeRootShaderResourceView(2,this.BrickObjectRing.Address(FrameIndex,this.DummyBuffer));
        CommandList->SetComputeRootShaderResourceView(3,this.ObjectRing.Address(FrameIndex,this.DummyBuffer));
        CommandList->SetComputeRootShaderResourceView(4,(this.MeshRecordBuffer!=null?this.MeshRecordBuffer:this.DummyBuffer)->GetGPUVirtualAddress());
        CommandList->SetComputeRootShaderResourceView(5,(this.MeshDistanceBuffer!=null?this.MeshDistanceBuffer:this.DummyBuffer)->GetGPUVirtualAddress());
        CommandList->SetComputeRootDescriptorTable(6,new D3D12_GPU_DESCRIPTOR_HANDLE(Start,0,this.DescriptorSize));
        CommandList->SetComputeRootDescriptorTable(7,new D3D12_GPU_DESCRIPTOR_HANDLE(Start,(int)UavSlot,this.DescriptorSize));
        CommandList->SetComputeRootUnorderedAccessView(8,TraceResults);
        CommandList->SetComputeRootShaderResourceView(9,TraceRays);
    }
    private ulong WriteConstants(uint FrameIndex,int Slot,uint Pass,uint BrickStart,uint RayCount,float Voxel,Vector3 Remainder,int LevelCount){
        var Index=FrameIndex*ConstantsPerFrame+(uint)Slot;
        var Target=(float*)(this.ConstantsMapped+Index*ConstantsStride);
        Target[0]=Voxel;
        Target[1]=Voxel*TruncationVoxels;
        Target[2]=this.Resolution;
        Target[3]=Slot;
        Target[4]=Remainder.X;
        Target[5]=Remainder.Y;
        Target[6]=Remainder.Z;
        Target[7]=0f;
        var Pass4=(uint*)(Target+8);
        Pass4[0]=Pass;
        Pass4[1]=BrickStart;
        Pass4[2]=RayCount;
        Pass4[3]=(uint)LevelCount;
        for(var Level=0;Level<MaximumLevels;Level++){
            var (Scale,Origin,Texel)=this.ClipmapConstants[Level];
            *(Vector4*)(Target+12+Level*4)=Scale;
            *(Vector4*)(Target+28+Level*4)=Origin;
            *(Vector4*)(Target+44+Level*4)=Texel;
        }
        return this.ConstantsBuffer->GetGPUVirtualAddress()+Index*ConstantsStride;
    }
    /// <summary>すべてのレベルで、レベル0のボクセルボックスのトランケーション範囲内にあるブリックをマークする。</summary>
    private void MarkDirty(HashSet<LongVector3>[]Dirty,LongVector3 Minimum,LongVector3 Maximum,int Count){
        var Reach=(long)TruncationVoxels;
        for(var Level=0;Level<Count;Level++){
            var Scale=1L<<Level;
            var LevelMinimum=new LongVector3(FloorDivide(Minimum.X,Scale)-Reach,FloorDivide(Minimum.Y,Scale)-Reach,FloorDivide(Minimum.Z,Scale)-Reach);
            var LevelMaximum=new LongVector3(FloorDivide(Maximum.X,Scale)+Reach,FloorDivide(Maximum.Y,Scale)+Reach,FloorDivide(Maximum.Z,Scale)+Reach);
            var BricksPerSide=this.Resolution/BrickSize;
            var Clip=this.ClipMinimum[Level];
            // 更新できるのは現在のクリップ内のブリックだけ。巨大なオブジェクトでも処理コストがクリップを超えないよう、ループ範囲を内側に制限する。
            var FromX=Math.Max(FloorDivide(LevelMinimum.X,BrickSize),Clip.X/BrickSize);
            var ToX=Math.Min(FloorDivide(LevelMaximum.X,BrickSize),Clip.X/BrickSize+BricksPerSide-1);
            var FromY=Math.Max(FloorDivide(LevelMinimum.Y,BrickSize),Clip.Y/BrickSize);
            var ToY=Math.Min(FloorDivide(LevelMaximum.Y,BrickSize),Clip.Y/BrickSize+BricksPerSide-1);
            var FromZ=Math.Max(FloorDivide(LevelMinimum.Z,BrickSize),Clip.Z/BrickSize);
            var ToZ=Math.Min(FloorDivide(LevelMaximum.Z,BrickSize),Clip.Z/BrickSize+BricksPerSide-1);
            for(var Z=FromZ;Z<=ToZ;Z++){
                for(var Y=FromY;Y<=ToY;Y++){
                    for(var X=FromX;X<=ToX;X++){
                        Dirty[Level].Add(new LongVector3(X,Y,Z));
                    }
                }
            }
        }
    }
    private static bool InsideClip(LongVector3 Brick,LongVector3 ClipMinimum,int BricksPerSide){
        var X=Brick.X-ClipMinimum.X/BrickSize;
        var Y=Brick.Y-ClipMinimum.Y/BrickSize;
        var Z=Brick.Z-ClipMinimum.Z/BrickSize;
        return X>=0&&X<BricksPerSide&&Y>=0&&Y<BricksPerSide&&Z>=0&&Z<BricksPerSide;
    }
    /// <summary>描画空間の点を含む、レベル0のワールドボクセル。</summary>
    private static LongVector3 ToGlobal(Vector3 Render,in GlobalDistanceFieldSettings Settings){
        var Local=(Render+Settings.OriginRemainder)/Settings.VoxelSizeMeters;
        long Axis(float Value)=>(long)MathF.Floor(Math.Clamp(Value,-1e15f,1e15f));
        return new LongVector3(Settings.OriginVoxel.X+Axis(Local.X),Settings.OriginVoxel.Y+Axis(Local.Y),Settings.OriginVoxel.Z+Axis(Local.Z));
    }
    private static (Vector3 Minimum,Vector3 Maximum)TransformBounds(Vector3 Minimum,Vector3 Maximum,in Matrix4x4 Matrix){
        var Low=new Vector3(float.MaxValue);
        var High=new Vector3(float.MinValue);
        for(var Corner=0;Corner<8;Corner++){
            var Point=new Vector3((Corner&1)==0?Minimum.X:Maximum.X,(Corner&2)==0?Minimum.Y:Maximum.Y,(Corner&4)==0?Minimum.Z:Maximum.Z);
            var Transformed=Vector3.Transform(Point,Matrix);
            Low=Vector3.Min(Low,Transformed);
            High=Vector3.Max(High,Transformed);
        }
        return (Low,High);
    }
    /// <summary>オブジェクトが同じ場所にあるかどうか。回転とスケールが同じで、平行移動がカメラの移動量だけ変化した場合を指す。</summary>
    private static bool SamePlacement(in Matrix4x4 Previous,in Matrix4x4 Current,Vector3 CameraDelta){
        const float Rotation=1e-5f;
        const float Translation=1e-3f;
        return MathF.Abs(Previous.M11-Current.M11)<=Rotation&&MathF.Abs(Previous.M12-Current.M12)<=Rotation&&MathF.Abs(Previous.M13-Current.M13)<=Rotation
            &&MathF.Abs(Previous.M21-Current.M21)<=Rotation&&MathF.Abs(Previous.M22-Current.M22)<=Rotation&&MathF.Abs(Previous.M23-Current.M23)<=Rotation
            &&MathF.Abs(Previous.M31-Current.M31)<=Rotation&&MathF.Abs(Previous.M32-Current.M32)<=Rotation&&MathF.Abs(Previous.M33-Current.M33)<=Rotation
            &&Vector3.Distance(Previous.Translation-CameraDelta,Current.Translation)<=Translation;
    }
    private static bool IsFinite(in Matrix4x4 Matrix)=>
        float.IsFinite(Matrix.M11)&&float.IsFinite(Matrix.M12)&&float.IsFinite(Matrix.M13)&&float.IsFinite(Matrix.M14)
        &&float.IsFinite(Matrix.M21)&&float.IsFinite(Matrix.M22)&&float.IsFinite(Matrix.M23)&&float.IsFinite(Matrix.M24)
        &&float.IsFinite(Matrix.M31)&&float.IsFinite(Matrix.M32)&&float.IsFinite(Matrix.M33)&&float.IsFinite(Matrix.M34)
        &&float.IsFinite(Matrix.M41)&&float.IsFinite(Matrix.M42)&&float.IsFinite(Matrix.M43)&&float.IsFinite(Matrix.M44);
    /// <summary>オブジェクトの最小軸スケール。メッシュ距離にこの値を掛けても、描画空間の距離を過大評価しない。</summary>
    private static float MinimumScale(in Matrix4x4 Matrix)=>MathF.Min(
        new Vector3(Matrix.M11,Matrix.M12,Matrix.M13).Length(),
        MathF.Min(new Vector3(Matrix.M21,Matrix.M22,Matrix.M23).Length(),new Vector3(Matrix.M31,Matrix.M32,Matrix.M33).Length()));
    internal static long FloorDivide(long Value,long Divisor){
        var Quotient=Value/Divisor;
        return Value%Divisor<0?Quotient-1:Quotient;
    }
    internal static long PositiveModulo(long Value,long Divisor){
        var Remainder=Value%Divisor;
        return Remainder<0?Remainder+Divisor:Remainder;
    }
    private void CreateLevels((float VoxelSize,int Count,int Resolution)Wanted){
        this.ReleaseLevels();
        this.Configuration=Wanted;
        Array.Clear(this.ClipValid);
        Array.Clear(this.Objects);
        this.HasPreviousOrigin=false;
        for(var Level=0;Level<Wanted.Count;Level++){
            var Desc=D3D12_RESOURCE_DESC.Tex3D(FieldFormat,(ulong)Wanted.Resolution,(uint)Wanted.Resolution,(ushort)Wanted.Resolution,1);
            Desc.Flags|=D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS;
            D3D12_HEAP_PROPERTIES Heap=new(D3D12_HEAP_TYPE_DEFAULT);
            ID3D12Resource*Resource;
            D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateCommittedResource(&Heap,D3D12_HEAP_FLAG_NONE,&Desc,ShaderResourceState,null,__uuidof<ID3D12Resource>(),(void**)&Resource),
                "ID3D12Device::CreateCommittedResource","creating a global distance field level");
            this.Levels[Level]=Resource;
        }
        // 使用しないレベルもレベル0を繰り返す形で、すべてのレベルスロットにビューを設定し、SRVテーブルを常に完全な状態にする。
        for(var Level=0;Level<MaximumLevels;Level++){
            var Resource=this.Levels[Level<Wanted.Count?Level:0];
            D3D12_SHADER_RESOURCE_VIEW_DESC Srv=new(){ Format=FieldFormat,ViewDimension=D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_TEXTURE3D,Shader4ComponentMapping=(uint)TerraFX.Interop.DirectX.D3D12.D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING };
            Srv.Texture3D.MipLevels=1;
            this.Device.Device->CreateShaderResourceView(Resource,&Srv,this.CpuHandle(Level));
            D3D12_UNORDERED_ACCESS_VIEW_DESC Uav=new(){ Format=FieldFormat,ViewDimension=D3D12_UAV_DIMENSION.D3D12_UAV_DIMENSION_TEXTURE3D };
            Uav.Texture3D.WSize=(uint)Wanted.Resolution;
            this.Device.Device->CreateUnorderedAccessView(Resource,null,&Uav,this.CpuHandle(MaximumLevels+Level));
        }
    }
    private void UploadMeshes(){
        this.ReleaseBuffer(ref this.MeshRecordBuffer);
        this.ReleaseBuffer(ref this.MeshDistanceBuffer);
        var Records=new byte[Math.Max(this.MeshFields.Count,1)*MeshRecordSize];
        var Offset=0;
        for(var Index=0;Index<this.MeshFields.Count;Index++){
            var Field=this.MeshFields[Index];
            fixed(byte*Target=&Records[Index*MeshRecordSize]){
                var Floats=(float*)Target;
                Floats[0]=Field.BoundsMin.X;
                Floats[1]=Field.BoundsMin.Y;
                Floats[2]=Field.BoundsMin.Z;
                Floats[3]=Field.VoxelSize;
                var Integers=(uint*)Target;
                Integers[4]=(uint)Field.ResolutionX;
                Integers[5]=(uint)Field.ResolutionY;
                Integers[6]=(uint)Field.ResolutionZ;
                Integers[7]=(uint)Offset;
            }
            Offset+=Field.Distances.Length;
        }
        this.MeshRecordBuffer=this.CreateUploadBuffer((ulong)Records.Length,out var RecordMapped);
        fixed(byte*Source=Records){
            Buffer.MemoryCopy(Source,RecordMapped,Records.Length,Records.Length);
        }
        this.MeshRecordBuffer->Unmap(0,null);
        // ハーフ精度浮動小数点。バイトアドレス読み取り用に32ビットワード単位へパディングする。
        var Words=Math.Max((this.MeshHalves.Count+1)/2,1);
        this.MeshDistanceBuffer=this.CreateUploadBuffer((ulong)Words*4,out var DistanceMapped);
        var Halves=(ushort*)DistanceMapped;
        for(var Index=0;Index<this.MeshHalves.Count;Index++){
            Halves[Index]=this.MeshHalves[Index];
        }
        if((this.MeshHalves.Count&1)!=0){
            Halves[this.MeshHalves.Count]=0;
        }
        this.MeshDistanceBuffer->Unmap(0,null);
        this.MeshDataDirty=false;
        // メッシュの距離場を取得したばかりのオブジェクトは場へ追加する必要がある。すべてのオブジェクトを忘れて、全ブリックを再計算する。
        Array.Clear(this.Objects);
        Array.Clear(this.ClipValid);
    }
    private ID3D12Resource*CreateUploadBuffer(ulong Size,out byte*Mapped){
        D3D12_HEAP_PROPERTIES Heap=new(D3D12_HEAP_TYPE_UPLOAD);
        var Desc=D3D12_RESOURCE_DESC.Buffer(Math.Max(Size,16UL));
        ID3D12Resource*Resource;
        D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateCommittedResource(&Heap,D3D12_HEAP_FLAG_NONE,&Desc,D3D12_RESOURCE_STATE_GENERIC_READ,null,__uuidof<ID3D12Resource>(),(void**)&Resource),
            "ID3D12Device::CreateCommittedResource","creating a global distance field upload buffer");
        D3D12_RANGE NoRead=new();
        byte*Pointer;
        D3D12Diagnostics.EnsureSuccess(Resource->Map(0,&NoRead,(void**)&Pointer),"ID3D12Resource::Map","mapping a global distance field upload buffer");
        Mapped=Pointer;
        return Resource;
    }
    private void ReleaseBuffer(ref ID3D12Resource*Buffer){
        if(Buffer!=null){
            _=Buffer->Release();
            Buffer=null;
        }
    }
    private void EnsurePipeline(){
        if(this.PipelineState!=null){
            return;
        }
        if(!ShaderArtifactRuntime.TryLoad(this.ArtifactPath,ShaderArtifactTargetKind.Compute,out var Package,out var Error)||Package is null){
            throw new InvalidDataException($"The global distance field requires '{this.ArtifactPath}': {Error}");
        }
        var Bytecode=Package.GetBytecode("CSMain");
        this.RootSignature=this.CreateRootSignature();
        fixed(byte*Code=Bytecode){
            D3D12_COMPUTE_PIPELINE_STATE_DESC Desc=new(){
                pRootSignature=this.RootSignature,
                CS=new D3D12_SHADER_BYTECODE { pShaderBytecode=Code,BytecodeLength=(nuint)Bytecode.Length },
            };
            ID3D12PipelineState*Pipeline;
            D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateComputePipelineState(&Desc,__uuidof<ID3D12PipelineState>(),(void**)&Pipeline),
                "ID3D12Device::CreateComputePipelineState","creating the global distance field pipeline");
            this.PipelineState=Pipeline;
        }
        D3D12_DESCRIPTOR_HEAP_DESC HeapDesc=new(){ Type=D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV,NumDescriptors=MaximumLevels*2,Flags=D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE };
        ID3D12DescriptorHeap*DescriptorHeap;
        D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateDescriptorHeap(&HeapDesc,__uuidof<ID3D12DescriptorHeap>(),(void**)&DescriptorHeap),
            "ID3D12Device::CreateDescriptorHeap","creating the global distance field descriptor heap");
        this.DescriptorHeap=DescriptorHeap;
        this.DescriptorSize=this.Device.Device->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV);
        this.ConstantsBuffer=this.CreateUploadBuffer((ulong)this.FrameCount*ConstantsPerFrame*ConstantsStride+ConstantsStride,out this.ConstantsMapped);
        // パスが読み取らないルートディスクリプター用の小さなバッファ（空フレームのレコードや更新中のトレースバッファなど）。
        D3D12_HEAP_PROPERTIES DefaultHeap=new(D3D12_HEAP_TYPE_DEFAULT);
        var DummyDesc=D3D12_RESOURCE_DESC.Buffer(256,D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS);
        ID3D12Resource*Dummy;
        D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateCommittedResource(&DefaultHeap,D3D12_HEAP_FLAG_NONE,&DummyDesc,D3D12_RESOURCE_STATE_UNORDERED_ACCESS,null,__uuidof<ID3D12Resource>(),(void**)&Dummy),
            "ID3D12Device::CreateCommittedResource","creating the global distance field placeholder buffer");
        this.DummyBuffer=Dummy;
    }
    private ID3D12RootSignature*CreateRootSignature(){
        D3D12_DESCRIPTOR_RANGE SrvRange=new(){ RangeType=D3D12_DESCRIPTOR_RANGE_TYPE_SRV,NumDescriptors=MaximumLevels,BaseShaderRegister=5 };
        D3D12_DESCRIPTOR_RANGE UavRange=new(){ RangeType=D3D12_DESCRIPTOR_RANGE_TYPE_UAV,NumDescriptors=1,BaseShaderRegister=0 };
        D3D12_ROOT_PARAMETER*Parameters=stackalloc D3D12_ROOT_PARAMETER[10];
        Parameters[0]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_CBV,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
        Parameters[0].Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=0 };
        for(var Index=0;Index<5;Index++){
            Parameters[1+Index]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_SRV,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
            Parameters[1+Index].Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=(uint)Index };
        }
        Parameters[6]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
        Parameters[6].DescriptorTable=new D3D12_ROOT_DESCRIPTOR_TABLE { NumDescriptorRanges=1,pDescriptorRanges=&SrvRange };
        Parameters[7]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
        Parameters[7].DescriptorTable=new D3D12_ROOT_DESCRIPTOR_TABLE { NumDescriptorRanges=1,pDescriptorRanges=&UavRange };
        Parameters[8]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_UAV,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
        Parameters[8].Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=1 };
        Parameters[9]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_SRV,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
        Parameters[9].Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=9 };
        D3D12_STATIC_SAMPLER_DESC Sampler=new(){
            Filter=D3D12_FILTER.D3D12_FILTER_MIN_MAG_MIP_LINEAR,
            AddressU=D3D12_TEXTURE_ADDRESS_MODE.D3D12_TEXTURE_ADDRESS_MODE_WRAP,
            AddressV=D3D12_TEXTURE_ADDRESS_MODE.D3D12_TEXTURE_ADDRESS_MODE_WRAP,
            AddressW=D3D12_TEXTURE_ADDRESS_MODE.D3D12_TEXTURE_ADDRESS_MODE_WRAP,
            ComparisonFunc=D3D12_COMPARISON_FUNC.D3D12_COMPARISON_FUNC_NEVER,
            MaxLOD=float.MaxValue,
            ShaderRegister=0,
            ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL,
        };
        D3D12_ROOT_SIGNATURE_DESC Desc=new(){ NumParameters=10,pParameters=Parameters,NumStaticSamplers=1,pStaticSamplers=&Sampler };
        ID3DBlob*Signature=null;
        ID3DBlob*Error=null;
        try {
            D3D12Diagnostics.EnsureSuccess(D3D12SerializeRootSignature(&Desc,D3D_ROOT_SIGNATURE_VERSION.D3D_ROOT_SIGNATURE_VERSION_1,&Signature,&Error),
                "D3D12SerializeRootSignature","serializing the global distance field root signature");
            ID3D12RootSignature*Result;
            D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateRootSignature(0,Signature->GetBufferPointer(),Signature->GetBufferSize(),__uuidof<ID3D12RootSignature>(),(void**)&Result),
                "ID3D12Device::CreateRootSignature","creating the global distance field root signature");
            return Result;
        } finally {
            if(Signature!=null){ _=Signature->Release(); }
            if(Error!=null){ _=Error->Release(); }
        }
    }
    private D3D12_CPU_DESCRIPTOR_HANDLE CpuHandle(int Slot)=>new(this.DescriptorHeap->GetCPUDescriptorHandleForHeapStart(),Slot,this.DescriptorSize);
    /// <summary>テスト専用のレベルコピー。テクセル順（xが最速）で格納したResolution^3個の値と、レベルのグローバル格子上にあるクリップ最下端のボクセル。</summary>
    internal (float[]Values,int Resolution,LongVector3 ClipMinimum)ReadLevelForTesting(int Level){
        var Resolution=this.Resolution;
        var (Data,RowPitch,_,_)=GpuTextureReadback.Read(this.Device,this.Levels[Level],ShaderResourceState);
        var Result=new float[Resolution*Resolution*Resolution];
        var SlicePitch=RowPitch*Resolution;
        fixed(byte*Bytes=Data){
            for(var Z=0;Z<Resolution;Z++){
                for(var Y=0;Y<Resolution;Y++){
                    var Row=(Half*)(Bytes+Z*SlicePitch+Y*RowPitch);
                    for(var X=0;X<Resolution;X++){
                        Result[(Z*Resolution+Y)*Resolution+X]=(float)Row[X];
                    }
                }
            }
        }
        return (Result,Resolution,this.ClipMinimum[Level]);
    }
    /// <summary>
    /// M0111が使う方法（TraceGlobalDistanceField）で、距離場を通る最大512本のレイをテスト用にトレースする。
    /// 各レイについて、ヒットまでの距離（ヒットしなければレイの長さ）、歩数、ヒットの有無を返す。
    /// 呼び出し側がレンダラーのキューをアイドル状態にしておくこと。距離場は最後に更新されたものを使う。
    /// </summary>
    internal Vector4[]TraceForTesting(ReadOnlySpan<(Vector3 Origin,Vector3 Direction,float Length)>Rays){
        if(Rays.Length is 0 or>MaximumTraceRays){
            throw new ArgumentOutOfRangeException(nameof(Rays));
        }
        ID3D12Resource*RayBuffer=null;
        ID3D12Resource*ResultBuffer=null;
        ID3D12CommandAllocator*Allocator=null;
        ID3D12GraphicsCommandList*List=null;
        ID3D12Fence*Fence=null;
        HANDLE Event=HANDLE.NULL;
        try {
            RayBuffer=this.CreateUploadBuffer((ulong)Rays.Length*32,out var RayMapped);
            for(var Index=0;Index<Rays.Length;Index++){
                ((Vector4*)RayMapped)[Index*2]=new Vector4(Rays[Index].Origin,Rays[Index].Length);
                ((Vector4*)RayMapped)[Index*2+1]=new Vector4(Rays[Index].Direction,0f);
            }
            RayBuffer->Unmap(0,null);
            D3D12_HEAP_PROPERTIES Heap=new(D3D12_HEAP_TYPE_DEFAULT);
            var ResultDesc=D3D12_RESOURCE_DESC.Buffer((ulong)Rays.Length*16,D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS);
            D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateCommittedResource(&Heap,D3D12_HEAP_FLAG_NONE,&ResultDesc,D3D12_RESOURCE_STATE_UNORDERED_ACCESS,null,__uuidof<ID3D12Resource>(),(void**)&ResultBuffer),
                "ID3D12Device::CreateCommittedResource","global distance field trace results");
            D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE.D3D12_COMMAND_LIST_TYPE_DIRECT,__uuidof<ID3D12CommandAllocator>(),(void**)&Allocator),"CreateCommandAllocator","global distance field trace");
            D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateCommandList(0,D3D12_COMMAND_LIST_TYPE.D3D12_COMMAND_LIST_TYPE_DIRECT,Allocator,null,__uuidof<ID3D12GraphicsCommandList>(),(void**)&List),"CreateCommandList","global distance field trace");
            // トレース定数は各フレームのスロット群の後（フレームインデックスFrameCount、スロット0）に置くため、実行中のフレームから読み取られない。
            var Constants=this.WriteConstants(this.FrameCount,0,Pass:1,BrickStart:0,RayCount:(uint)Rays.Length,this.ClipmapConstants[0].Scale.X,Vector3.Zero,this.LevelCount);
            this.Bind(List,Constants,0,UavSlot:(uint)MaximumLevels,TraceResults:ResultBuffer->GetGPUVirtualAddress(),TraceRays:RayBuffer->GetGPUVirtualAddress());
            List->Dispatch(1,1,1);
            D3D12Diagnostics.EnsureSuccess(List->Close(),"Close","global distance field trace");
            ID3D12CommandList*Base=(ID3D12CommandList*)List;
            this.Device.CommandQueue->ExecuteCommandLists(1,&Base);
            D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateFence(0,D3D12_FENCE_FLAGS.D3D12_FENCE_FLAG_NONE,__uuidof<ID3D12Fence>(),(void**)&Fence),"CreateFence","global distance field trace");
            Event=CreateEventW(null,FALSE,FALSE,null);
            D3D12Diagnostics.EnsureSuccess(this.Device.CommandQueue->Signal(Fence,1),"Signal","global distance field trace");
            if(Fence->GetCompletedValue()<1){
                D3D12Diagnostics.EnsureSuccess(Fence->SetEventOnCompletion(1,Event),"SetEventOnCompletion","global distance field trace");
                _=WaitForSingleObjectEx(Event,INFINITE,FALSE);
            }
            var Bytes=GpuBufferReadback.Read(this.Device,ResultBuffer,(ulong)Rays.Length*16,D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
            var Result=new Vector4[Rays.Length];
            fixed(byte*Source=Bytes){
                for(var Index=0;Index<Rays.Length;Index++){
                    Result[Index]=((Vector4*)Source)[Index];
                }
            }
            return Result;
        } finally {
            if(Event!=HANDLE.NULL){ _=CloseHandle(Event); }
            if(Fence!=null){ _=Fence->Release(); }
            if(List!=null){ _=List->Release(); }
            if(Allocator!=null){ _=Allocator->Release(); }
            if(ResultBuffer!=null){ _=ResultBuffer->Release(); }
            if(RayBuffer!=null){ _=RayBuffer->Release(); }
        }
    }
    private void ReleaseLevels(){
        for(var Level=0;Level<MaximumLevels;Level++){
            if(this.Levels[Level]!=null){
                _=this.Levels[Level]->Release();
                this.Levels[Level]=null;
            }
        }
    }
    public void Dispose(){
        if(this.Disposed){
            return;
        }
        this.Disposed=true;
        this.ReleaseLevels();
        this.ReleaseBuffer(ref this.MeshRecordBuffer);
        this.ReleaseBuffer(ref this.MeshDistanceBuffer);
        this.ObjectRing.Release();
        this.BrickRing.Release();
        this.BrickObjectRing.Release();
        if(this.ConstantsBuffer!=null){
            this.ConstantsBuffer->Unmap(0,null);
            _=this.ConstantsBuffer->Release();
        }
        this.ReleaseBuffer(ref this.DummyBuffer);
        if(this.PipelineState!=null){ _=this.PipelineState->Release(); }
        if(this.RootSignature!=null){ _=this.RootSignature->Release(); }
        if(this.DescriptorHeap!=null){ _=this.DescriptorHeap->Release(); }
    }
    private readonly record struct MeshEntry(int Record,int Offset,Vector3 BoundsMin,Vector3 BoundsMax);
    private record struct ObjectState(bool Tracked,int MeshIndex,Matrix4x4 Matrix,LongVector3 GlobalMinimum,LongVector3 GlobalMaximum);
    /// <summary>処理中のフレームごとに領域を持つマップ済みアップロードバッファ。フレームで容量が足りなくなれば、GPUアイドル後に拡張する。</summary>
    private struct UploadRing {
        private ID3D12Resource*Buffer;
        private byte*Mapped;
        private int BytesPerFrame;
        private uint Frames;
        internal void Ensure(StageGlobalDistanceField Owner,int Bytes,Action WaitForGpuIdle){
            if(this.Buffer!=null&&Bytes<=this.BytesPerFrame){
                return;
            }
            if(this.Buffer!=null){
                WaitForGpuIdle();
                this.Release();
            }
            this.Frames=Owner.FrameCount;
            this.BytesPerFrame=Math.Max(4096,(int)BitOperations.RoundUpToPowerOf2((uint)Math.Max(Bytes,1)));
            this.Buffer=Owner.CreateUploadBuffer((ulong)this.BytesPerFrame*this.Frames,out this.Mapped);
        }
        internal readonly byte*Slot(uint FrameIndex)=>this.Mapped+FrameIndex*(uint)this.BytesPerFrame;
        internal readonly ulong Address(uint FrameIndex,ID3D12Resource*Fallback)=>this.Buffer!=null?this.Buffer->GetGPUVirtualAddress()+FrameIndex*(ulong)this.BytesPerFrame:Fallback->GetGPUVirtualAddress();
        internal void Release(){
            if(this.Buffer!=null){
                this.Buffer->Unmap(0,null);
                _=this.Buffer->Release();
                this.Buffer=null;
                this.Mapped=null;
            }
        }
    }
}
