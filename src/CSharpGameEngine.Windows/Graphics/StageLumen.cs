using System.Numerics;
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
/// The Lumen-like dynamic global illumination and reflections (ADR-0078/M0111), recomputed every frame (nothing is baked):
/// <list type="bullet">
/// <item>the surface cache: six cards per mesh that has a distance field (its box's sides in its own space), captured once with the Stage program's
/// LumenCardPSMain (albedo, normal, emission) and lit every few frames by the sun (shadow map, or the global distance field off the cascades), the
/// local lights and the radiance cache, so light bounces through it;</item>
/// <item>the radiance cache: probes in clipmaps around the camera, fixed to the world's voxel grid, that trace the global distance field (a hit reads
/// the hit object's cards after a few steps on its mesh distance field, a miss the sky radiance map);</item>
/// <item>the final gather: a probe every 16 or 32 pixels on the surface prepass (LumenSurfacePSMain) traces the screen, then the distance field near it,
/// then reads the radiance cache; the probes are filtered with their neighbours and each pixel interpolates them and blends with last frame's;</item>
/// <item>the reflections: a GGX-sampled ray per pixel (or per 2x2 at High) traces the screen, then the distance field, then the sky; rough lobes read the
/// probes. The traces are filtered and blended with last frame's.</item>
/// </list>
/// The Stage program reads the diffuse irradiance at t16 and the reflected radiance at t17. The compute passes are Lumen.hlsl; its header lists the
/// root signature.
/// </summary>
internal sealed unsafe class StageLumen:IDisposable {
    internal const string ArtifactFileName="Lumen.cgeshaderartifact";
    internal const int OctahedronTexels=8;
    internal const int CacheTile=OctahedronTexels+2;
    internal const D3D12_RESOURCE_STATES ShaderResourceState=D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE|D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
    internal const DXGI_FORMAT CardAlbedoFormat=DXGI_FORMAT_R8G8B8A8_UNORM;
    internal const DXGI_FORMAT CardNormalFormat=DXGI_FORMAT_R8G8B8A8_UNORM;
    internal const DXGI_FORMAT CardEmissiveFormat=DXGI_FORMAT_R16G16B16A16_FLOAT;
    internal const DXGI_FORMAT SurfaceNormalFormat=DXGI_FORMAT_R16G16B16A16_FLOAT;
    internal const DXGI_FORMAT SurfaceDistanceFormat=DXGI_FORMAT_R32_FLOAT;
    private const DXGI_FORMAT RadianceFormat=DXGI_FORMAT_R16G16B16A16_FLOAT;
    /// <summary>The diffuse irradiance and its history: a sunlit white floor gives its neighbours more than half precision holds (65504).</summary>
    private const DXGI_FORMAT IrradianceFormat=DXGI_FORMAT_R32G32B32A32_FLOAT;
    private const uint PassCardLighting=0;
    private const uint PassRadianceCache=1;
    private const uint PassScreenProbeTrace=2;
    private const uint PassScreenProbeFilter=3;
    private const uint PassIntegrate=4;
    private const uint PassReflectionTrace=5;
    private const uint PassReflectionResolve=6;
    private const uint PassVisualize=7;
    private const uint PassCount=8;
    private const uint ConstantsStride=1024;
    private const uint FlagHistory=1;
    private const uint FlagSkyMap=2;
    private const uint FlagGatherScreenTraces=4;
    private const uint FlagReflectionScreenTraces=8;
    private const uint FlagAutoExposure=16;
    private const uint FlagSceneColorHistory=32;
    private const int ObjectRecordSize=144;
    private const int GridSide=128;
    private const float GridCellMeters=4f;
    private const int SrvClipmap=0;
    private const int SrvCardAlbedo=4;
    private const int SrvCardNormal=5;
    private const int SrvCardEmissive=6;
    private const int SrvCardDepth=7;
    private const int SrvCardRadiance=8;
    private const int SrvShadowAtlas=9;
    private const int SrvSkyRadiance=10;
    private const int SrvCacheRadiance=11;
    private const int SrvCacheIrradiance=12;
    private const int SrvSurfaceNormal=13;
    private const int SrvSurfaceDistance=14;
    private const int SrvPreviousSurfaceNormal=15;
    private const int SrvPreviousSurfaceDistance=16;
    private const int SrvSceneColorHistory=17;
    private const int SrvProbeRadiance=18;
    private const int SrvProbeInfo=19;
    private const int SrvProbeSh=20;
    private const int SrvDiffuseHistory=21;
    private const int SrvReflectionHistory=22;
    private const int SrvReflectionTrace=23;
    private const int SrvHistoryExposure=24;
    private const int SrvCount=25;
    private const int UavCardRadiance=25;
    private const int UavCacheRadiance=26;
    private const int UavCacheIrradiance=27;
    private const int UavProbeRadiance=28;
    private const int UavProbeInfo=29;
    private const int UavProbeSh=30;
    private const int UavDiffuse=31;
    private const int UavReflectionTrace=32;
    private const int UavReflection=33;
    private const int UavVisualize=34;
    private const int DescriptorCount=35;
    private const int RtvCardAlbedo=0;
    private const int RtvSurfaceNormal=3;
    private const int DsvCard=0;
    private const int DsvSurface=1;
    private readonly GraphicsDevice Device;
    private readonly string ArtifactPath;
    private readonly uint FrameCount;
    private readonly uint FrameConstantsStride;
    private readonly uint ObjectConstantsStride;
    private ID3D12RootSignature*RootSignature;
    private ID3D12PipelineState*PipelineState;
    private ID3D12DescriptorHeap*DescriptorHeap;
    private ID3D12DescriptorHeap*RtvHeap;
    private ID3D12DescriptorHeap*DsvHeap;
    private uint DescriptorSize;
    private uint RtvSize;
    private uint DsvSize;
    private ID3D12Resource*ConstantsBuffer;
    private byte*ConstantsMapped;
    private ID3D12Resource*CaptureConstants;
    private byte*CaptureConstantsMapped;
    private ID3D12Resource*DummyBuffer;
    /// <summary>The auto exposure state the scene colour history was drawn with (copied before the frame's metering).</summary>
    private ID3D12Resource*HistoryExposure;
    private UploadRing ObjectRing;
    private UploadRing CardRing;
    private UploadRing CellRing;
    private UploadRing CellObjectRing;
    private UploadRing WorkRing;
    private UploadRing ValidRing;
    // The surface cache.
    private LumenCardAtlas?Atlas;
    private ID3D12Resource*CardAlbedo;
    private ID3D12Resource*CardNormal;
    private ID3D12Resource*CardEmissive;
    private ID3D12Resource*CardDepth;
    private ID3D12Resource*CardRadiance;
    private ObjectState[]Objects=[];
    // The radiance cache.
    private ID3D12Resource*CacheRadiance;
    private ID3D12Resource*CacheIrradiance;
    private int CacheProbeCount;
    private int CacheProbesPerRow;
    private LongVector3[]CacheSlotCoordinate=[];
    private bool[]CacheSlotKnown=[];
    private uint[]CacheValid=[];
    private int[]CacheStamp=[];
    private int CacheStampValue;
    private readonly int[]CacheCursor=new int[GlobalDistanceFieldSettings.MaximumClipmapCount];
    private TierKey?Tier;
    // The screen.
    private ID3D12Resource*SurfaceNormal;
    private ID3D12Resource*SurfaceDistance;
    private ID3D12Resource*PreviousSurfaceNormal;
    private ID3D12Resource*PreviousSurfaceDistance;
    private ID3D12Resource*SurfaceDepth;
    private ID3D12Resource*SceneColorHistory;
    private ID3D12Resource*ProbeRadiance;
    private ID3D12Resource*ProbeInfo;
    private ID3D12Resource*ProbeSh;
    private ID3D12Resource*Diffuse;
    private ID3D12Resource*DiffuseHistory;
    private ID3D12Resource*ReflectionTrace;
    private ID3D12Resource*Reflection;
    private ID3D12Resource*ReflectionHistory;
    private (int Width,int Height,int ProbeTexels,int ReflectionDownsample)?ScreenKey;
    private int ProbeCountX;
    private int ProbeCountY;
    private int TraceWidth;
    private int TraceHeight;
    // Descriptors written for resources the renderer owns.
    private nint DescribedShadowAtlas;
    private nint DescribedSkyMap;
    private nint DescribedSceneColor;
    private LumenComputeBindings LastBindings;
    private readonly nint[]DescribedLevels=new nint[GlobalDistanceFieldSettings.MaximumClipmapCount];
    // The frame.
    private readonly List<int>Records=[];
    private readonly List<Capture>Captures=[];
    private readonly List<(int Card,int Object,int Face,int Width,int Height)>LightCards=[];
    private int LightCursor;
    private int[]CellCounts=new int[GridSide*GridSide];
    private int[]CellStarts=new int[GridSide*GridSide];
    private readonly List<int>CellEntries=[];
    private readonly List<(uint Level,uint Probe,uint HadData,uint Local)>CacheWork=[];
    private FramePlan Plan;
    private bool HistoryValid;
    /// <summary>Frames the history has accumulated since it was last reset: a young history takes new samples at 1 / (frames + 1).</summary>
    private int HistoryFrames;
    private bool SceneColorValid;
    private bool SurfaceValid;
    private Matrix4x4 PreviousViewProjection;
    private Vector3 PreviousCameraPosition;
    private LongVector3 PreviousOriginVoxel;
    private Vector3 PreviousOriginRemainder;
    private float PreviousExposure;
    private bool Disposed;
    internal StageLumen(GraphicsDevice Device,string StageArtifactPath,uint FrameCount,uint FrameConstantsStride,uint ObjectConstantsStride){
        this.Device=Device;
        this.FrameCount=FrameCount;
        this.FrameConstantsStride=FrameConstantsStride;
        this.ObjectConstantsStride=ObjectConstantsStride;
        this.ArtifactPath=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(StageArtifactPath))??string.Empty,ArtifactFileName);
        D3D12_FEATURE_DATA_D3D12_OPTIONS Options=default;
        this.IsSupported=Device.Device->CheckFeatureSupport(D3D12_FEATURE.D3D12_FEATURE_D3D12_OPTIONS,&Options,(uint)sizeof(D3D12_FEATURE_DATA_D3D12_OPTIONS)).SUCCEEDED
            &&Options.TypedUAVLoadAdditionalFormats!=0;
    }
    /// <summary>The radiance cache blends each refresh with the probe's previous radiance through typed UAV loads of RGBA16F.</summary>
    internal bool IsSupported { get; }
    /// <summary>Incremented whenever the diffuse or reflection output is (re)created, so the Stage t16/t17 views can be refreshed.</summary>
    internal int Generation { get;private set; }
    internal bool HasOutputs=>this.Diffuse!=null;
    internal int CaptureCount=>this.Captures.Count;
    internal LumenStatistics Statistics { get;private set; }
    /// <summary>The tier's budgets and sizes (ADR-0078/M0111): Unreal's High and Epic scalability with the PostProcessVolume's qualities applied.</summary>
    internal static LumenTier ResolveTier(in LumenSettings Settings,RenderScalability Scalability){
        var Epic=Scalability.GlobalIllumination>=ScalabilityLevel.Epic;
        var ProbeTexels=(int)Math.Clamp(BitOperations.RoundUpToPowerOf2((uint)MathF.Round((Epic?16f:32f)/Settings.FinalGatherQuality)),8u,64u);
        var Reflections=Settings.Reflections&&Scalability.AllowsLumenReflections;
        var ReflectionDownsample=Scalability.Reflections>=ScalabilityLevel.Epic&&Settings.ReflectionQuality>=0.75f?1:2;
        return new LumenTier(
            AtlasTexels:Epic?2048:1024,
            MaximumCardTexels:Epic?128:64,
            TexelsPerMeter:4f*Settings.SceneDetail*Settings.SurfaceCacheResolution,
            CardTexelsPerFrame:(int)((Epic?262144:65536)*Settings.SceneLightingQuality*Settings.SceneLightingUpdateSpeed),
            CapturesPerFrame:Epic?512:256,
            CacheLevels:Epic?4:3,
            CacheResolution:Epic?16:12,
            CacheSpacingMeters:2f,
            CacheProbesPerFrame:(int)((Epic?1024:256)*Settings.FinalGatherLightingUpdateSpeed),
            ProbeTexels:ProbeTexels,
            Reflections:Reflections,
            ReflectionDownsample:ReflectionDownsample);
    }
    // ---------------------------------------------------------------------------------------------------------------------------------------
    // The frame's plan (CPU): objects and their cards, captures, the cards to light, the object grid and the radiance cache's probes.
    /// <summary>
    /// Plans this frame and uploads its records. <paramref name="MeshRecord"/> gives a renderer mesh's record in the global distance field (or -1) and
    /// <paramref name="MeshBounds"/> its vertex bounds. Returns false when Lumen cannot run this frame (no field yet).
    /// </summary>
    internal bool Prepare(uint FrameIndex,in LumenFrameInput Input,ReadOnlySpan<bool>Active,ReadOnlySpan<int>MeshIndices,ReadOnlySpan<int>MaterialIndices,
        ReadOnlySpan<Matrix4x4>Transforms,ReadOnlySpan<float>EmissiveScales,int ObjectCount,Func<int,int>MeshRecord,Func<int,(Vector3 Minimum,Vector3 Maximum)>MeshBounds,
        Action WaitForGpuIdle){
        this.EnsurePipeline();
        var Tier=ResolveTier(Input.Settings,Input.Scalability);
        this.EnsureTier(Tier,WaitForGpuIdle);
        this.EnsureScreen(Input.Width,Input.Height,Tier,WaitForGpuIdle);
        var Field=Input.Field;
        // How far the render origin (the camera) moved over the world since the last frame: last frame's matrices apply to this frame's render
        // space after adding it.
        var OriginDelta=new Vector3(Field.OriginVoxel.X-this.PreviousOriginVoxel.X,Field.OriginVoxel.Y-this.PreviousOriginVoxel.Y,Field.OriginVoxel.Z-this.PreviousOriginVoxel.Z)*Field.VoxelSizeMeters
            +(Field.OriginRemainder-this.PreviousOriginRemainder);
        var PreviousViewProjection=Matrix4x4.CreateTranslation(OriginDelta)*this.PreviousViewProjection;
        var PreviousCamera=this.PreviousCameraPosition-OriginDelta;
        var HistoryUsable=this.HistoryValid&&float.IsFinite(OriginDelta.X)&&float.IsFinite(OriginDelta.Y)&&float.IsFinite(OriginDelta.Z);
        this.HistoryFrames=HistoryUsable?this.HistoryFrames+1:0;
        // Objects.
        if(this.Objects.Length<ObjectCount){
            Array.Resize(ref this.Objects,Math.Max(ObjectCount,this.Objects.Length*2));
        }
        this.Records.Clear();
        this.Captures.Clear();
        this.LightCards.Clear();
        var Statistics=new LumenStatistics();
        List<(float Distance,int Object,int Face)>Pending=[];
        for(var Index=0;Index<this.Objects.Length;Index++){
            ref var State=ref this.Objects[Index];
            var Record=Index<ObjectCount&&Active[Index]?MeshRecord(MeshIndices[Index]):-1;
            var Eligible=Record>=0&&IsFinite(Transforms[Index])&&Matrix4x4.Invert(Transforms[Index],out _);
            Vector3 BoxMinimum=default;
            Vector3 BoxMaximum=default;
            if(Eligible){
                var Bounds=MeshBounds(MeshIndices[Index]);
                (BoxMinimum,BoxMaximum)=LumenCards.CardBox(Bounds.Minimum,Bounds.Maximum);
                var Centre=Vector3.Transform((BoxMinimum+BoxMaximum)*0.5f,Transforms[Index]);
                var Radius=((BoxMaximum-BoxMinimum)*AxisScales(Transforms[Index])).Length()*0.5f;
                Eligible=(Centre-Input.CameraPosition).Length()-Radius<=Input.Settings.SceneViewDistanceMeters;
            }
            if(State.Tracked&&(!Eligible||State.Mesh!=MeshIndices[Index]||State.Material!=MaterialIndices[Index])){
                this.Release(ref State);
            }
            if(!Eligible){
                continue;
            }
            if(!State.Tracked){
                State=new ObjectState { Tracked=true,Mesh=MeshIndices[Index],Material=MaterialIndices[Index],Faces=new CardAllocation[LumenCards.FaceCount],EmissiveScale=EmissiveScales[Index] };
            }
            State.BoxMinimum=BoxMinimum;
            State.BoxMaximum=BoxMaximum;
            var Scale=AxisScales(Transforms[Index]);
            for(var Face=0;Face<LumenCards.FaceCount;Face++){
                var (Width,Height)=LumenCards.Resolution(Face,BoxMinimum,BoxMaximum,Scale,Tier.TexelsPerMeter,Tier.MaximumCardTexels);
                ref var Card=ref State.Faces[Face];
                // A card is reallocated only when its size is off by more than one power of two, so a slowly scaled object keeps its cards.
                if(Card.Allocated&&(Card.Width*4<=Width||Card.Width>=Width*4||Card.Height*4<=Height||Card.Height>=Height*4)){
                    this.Atlas!.Free(Card.X,Card.Y,Card.Width,Card.Height);
                    Card=default;
                    State.Captured&=(byte)~(1<<Face);
                }
                if(!Card.Allocated&&!Card.Failed){
                    if(this.Atlas!.TryAllocate(Width,Height,out var X,out var Y)){
                        Card=new CardAllocation { Allocated=true,X=X,Y=Y,Width=Width,Height=Height };
                    } else {
                        Card.Failed=true;
                    }
                }
                if(Card.Allocated&&(State.Captured&(1<<Face))==0){
                    Pending.Add(((Vector3.Transform((BoxMinimum+BoxMaximum)*0.5f,Transforms[Index])-Input.CameraPosition).Length(),Index,Face));
                }
            }
            if(State.EmissiveScale!=EmissiveScales[Index]){
                State.EmissiveScale=EmissiveScales[Index];
                State.RelightAll=true;
            }
            State.RecordIndex=this.Records.Count;
            State.MeshRecord=Record;
            this.Records.Add(Index);
        }
        // Captures: the nearest new cards first, within the frame's budget.
        Pending.Sort(static (A,B)=>A.Distance.CompareTo(B.Distance));
        for(var Index=0;Index<Pending.Count&&this.Captures.Count<Tier.CapturesPerFrame;Index++){
            var (_,ObjectIndex,Face)=Pending[Index];
            ref var State=ref this.Objects[ObjectIndex];
            ref var Card=ref State.Faces[Face];
            this.Captures.Add(new Capture(ObjectIndex,Face,LumenCards.CaptureProjection(Face,State.BoxMinimum,State.BoxMaximum),Card.X,Card.Y,Card.Width,Card.Height));
            State.Captured|=(byte)(1<<Face);
            this.LightCards.Add((State.RecordIndex*LumenCards.FaceCount+Face,ObjectIndex,Face,Card.Width,Card.Height));
        }
        Statistics.Captures=this.Captures.Count;
        Statistics.PendingCaptures=Pending.Count-this.Captures.Count;
        // Cards to light: this frame's captures, objects whose emission changed, then round robin over every captured card within the budget.
        var Texels=0;
        foreach(var Light in this.LightCards){
            Texels+=Light.Width*Light.Height;
        }
        var Candidates=0;
        foreach(var ObjectIndex in this.Records){
            ref var State=ref this.Objects[ObjectIndex];
            for(var Face=0;Face<LumenCards.FaceCount;Face++){
                if((State.Captured&(1<<Face))!=0){
                    Candidates++;
                }
            }
            if(!State.RelightAll){
                continue;
            }
            for(var Face=0;Face<LumenCards.FaceCount;Face++){
                if((State.Captured&(1<<Face))!=0&&!this.LightCards.Exists(Light=>Light.Object==ObjectIndex&&Light.Face==Face)){
                    var Card=State.Faces[Face];
                    this.LightCards.Add((State.RecordIndex*LumenCards.FaceCount+Face,ObjectIndex,Face,Card.Width,Card.Height));
                    Texels+=Card.Width*Card.Height;
                }
            }
            State.RelightAll=false;
        }
        if(Candidates>0){
            var Visited=0;
            while(Texels<Tier.CardTexelsPerFrame&&Visited<Candidates){
                this.LightCursor%=Candidates;
                var Ordinal=0;
                var Found=false;
                foreach(var ObjectIndex in this.Records){
                    ref var State=ref this.Objects[ObjectIndex];
                    for(var Face=0;Face<LumenCards.FaceCount&&!Found;Face++){
                        if((State.Captured&(1<<Face))==0){
                            continue;
                        }
                        if(Ordinal++==this.LightCursor){
                            Found=true;
                            if(!this.LightCards.Exists(Light=>Light.Object==ObjectIndex&&Light.Face==Face)){
                                var Card=State.Faces[Face];
                                this.LightCards.Add((State.RecordIndex*LumenCards.FaceCount+Face,ObjectIndex,Face,Card.Width,Card.Height));
                                Texels+=Card.Width*Card.Height;
                            }
                        }
                    }
                    if(Found){
                        break;
                    }
                }
                this.LightCursor++;
                Visited++;
            }
        }
        Statistics.LitCards=this.LightCards.Count;
        Statistics.LitTexels=Texels;
        // Records and cards.
        this.ObjectRing.Ensure(this,Math.Max(this.Records.Count,1)*ObjectRecordSize,WaitForGpuIdle);
        this.CardRing.Ensure(this,Math.Max(this.Records.Count,1)*LumenCards.FaceCount*16,WaitForGpuIdle);
        var ObjectSlot=this.ObjectRing.Slot(FrameIndex);
        var CardSlot=(uint*)this.CardRing.Slot(FrameIndex);
        for(var RecordIndex=0;RecordIndex<this.Records.Count;RecordIndex++){
            var ObjectIndex=this.Records[RecordIndex];
            ref var State=ref this.Objects[ObjectIndex];
            var Matrix=Transforms[ObjectIndex];
            Matrix4x4.Invert(Matrix,out var Inverse);
            var Target=(float*)(ObjectSlot+RecordIndex*ObjectRecordSize);
            WriteRows(Target,Inverse);
            WriteRows(Target+12,Matrix);
            Target[24]=State.BoxMinimum.X;
            Target[25]=State.BoxMinimum.Y;
            Target[26]=State.BoxMinimum.Z;
            ((uint*)Target)[27]=(uint)State.MeshRecord;
            Target[28]=State.BoxMaximum.X;
            Target[29]=State.BoxMaximum.Y;
            Target[30]=State.BoxMaximum.Z;
            var Scale=AxisScales(Matrix);
            Target[31]=MathF.Min(Scale.X,MathF.Min(Scale.Y,Scale.Z));
            var Ready=0u;
            for(var Face=0;Face<LumenCards.FaceCount;Face++){
                var Card=State.Faces[Face];
                var CardTarget=CardSlot+(RecordIndex*LumenCards.FaceCount+Face)*4;
                CardTarget[0]=(uint)Card.X;
                CardTarget[1]=(uint)Card.Y;
                CardTarget[2]=(uint)Card.Width;
                CardTarget[3]=(uint)Card.Height;
                if(Card.Allocated&&(State.Captured&(1<<Face))!=0){
                    Ready|=1u<<Face;
                }
            }
            var CardsTarget=(uint*)(Target+32);
            CardsTarget[0]=(uint)(RecordIndex*LumenCards.FaceCount);
            CardsTarget[1]=Ready;
            CardsTarget[2]=0;
            ((float*)CardsTarget)[3]=State.EmissiveScale;
            Statistics.Cards+=BitOperations.PopCount(Ready);
        }
        Statistics.Objects=this.Records.Count;
        Statistics.FreeAtlasPages=this.Atlas!.FreePageCount;
        // The object grid: a column of cells around the camera, each listing the objects whose box reaches it.
        var GridOrigin=new Vector2(MathF.Floor(Input.CameraPosition.X/GridCellMeters)*GridCellMeters-GridSide*GridCellMeters*0.5f,
            MathF.Floor(Input.CameraPosition.Z/GridCellMeters)*GridCellMeters-GridSide*GridCellMeters*0.5f);
        Array.Clear(this.CellCounts);
        Span<(int X0,int Z0,int X1,int Z1)>Ranges=this.Records.Count<=1024?stackalloc (int,int,int,int)[this.Records.Count]:new (int,int,int,int)[this.Records.Count];
        for(var RecordIndex=0;RecordIndex<this.Records.Count;RecordIndex++){
            var ObjectIndex=this.Records[RecordIndex];
            ref var State=ref this.Objects[ObjectIndex];
            var (Low,High)=TransformBounds(State.BoxMinimum,State.BoxMaximum,Transforms[ObjectIndex]);
            var X0=Math.Clamp((int)MathF.Floor((Low.X-GridOrigin.X)/GridCellMeters)-1,0,GridSide);
            var Z0=Math.Clamp((int)MathF.Floor((Low.Z-GridOrigin.Y)/GridCellMeters)-1,0,GridSide);
            var X1=Math.Clamp((int)MathF.Floor((High.X-GridOrigin.X)/GridCellMeters)+1,-1,GridSide-1);
            var Z1=Math.Clamp((int)MathF.Floor((High.Z-GridOrigin.Y)/GridCellMeters)+1,-1,GridSide-1);
            Ranges[RecordIndex]=(X0,Z0,X1,Z1);
            for(var Z=Z0;Z<=Z1;Z++){
                for(var X=X0;X<=X1;X++){
                    this.CellCounts[Z*GridSide+X]++;
                }
            }
        }
        var Running=0;
        for(var Cell=0;Cell<GridSide*GridSide;Cell++){
            this.CellStarts[Cell]=Running;
            Running+=this.CellCounts[Cell];
        }
        this.CellRing.Ensure(this,GridSide*GridSide*8,WaitForGpuIdle);
        this.CellObjectRing.Ensure(this,Math.Max(Running,1)*4,WaitForGpuIdle);
        var CellSlot=(uint*)this.CellRing.Slot(FrameIndex);
        var CellObjectSlot=(uint*)this.CellObjectRing.Slot(FrameIndex);
        for(var Cell=0;Cell<GridSide*GridSide;Cell++){
            CellSlot[Cell*2]=(uint)this.CellStarts[Cell];
            CellSlot[Cell*2+1]=0;
        }
        for(var RecordIndex=0;RecordIndex<this.Records.Count;RecordIndex++){
            var (X0,Z0,X1,Z1)=Ranges[RecordIndex];
            for(var Z=Z0;Z<=Z1;Z++){
                for(var X=X0;X<=X1;X++){
                    var Cell=Z*GridSide+X;
                    CellObjectSlot[CellSlot[Cell*2]+CellSlot[Cell*2+1]]=(uint)RecordIndex;
                    CellSlot[Cell*2+1]++;
                }
            }
        }
        // The radiance cache: clipmaps of probes on the world's voxel grid; probes a clip newly covers are traced first.
        this.CacheWork.Clear();
        var Levels=Tier.CacheLevels;
        var Resolution=Tier.CacheResolution;
        var VoxelsPerProbe=Math.Max(1L,(long)MathF.Round(Tier.CacheSpacingMeters/Field.VoxelSizeMeters));
        var CacheOrigins=new Vector4[GlobalDistanceFieldSettings.MaximumClipmapCount];
        var CacheSlots=new uint[GlobalDistanceFieldSettings.MaximumClipmapCount*4];
        var CacheScales=new Vector4[GlobalDistanceFieldSettings.MaximumClipmapCount];
        List<(uint Level,uint Probe,uint Local,float Order)>Fresh=[];
        for(var Level=0;Level<Levels;Level++){
            var Step=VoxelsPerProbe<<Level;
            var Spacing=Step*Field.VoxelSizeMeters;
            var Minimum=new LongVector3(StageGlobalDistanceField.FloorDivide(Field.OriginVoxel.X,Step)-Resolution/2,StageGlobalDistanceField.FloorDivide(Field.OriginVoxel.Y,Step)-Resolution/2,
                StageGlobalDistanceField.FloorDivide(Field.OriginVoxel.Z,Step)-Resolution/2);
            CacheOrigins[Level]=new Vector4(new Vector3(Minimum.X*Step-Field.OriginVoxel.X,Minimum.Y*Step-Field.OriginVoxel.Y,Minimum.Z*Step-Field.OriginVoxel.Z)*Field.VoxelSizeMeters-Field.OriginRemainder,0f);
            CacheScales[Level]=new Vector4(Spacing,Resolution,1f/Resolution,1f);
            var SlotX=(int)StageGlobalDistanceField.PositiveModulo(Minimum.X,Resolution);
            var SlotY=(int)StageGlobalDistanceField.PositiveModulo(Minimum.Y,Resolution);
            var SlotZ=(int)StageGlobalDistanceField.PositiveModulo(Minimum.Z,Resolution);
            var First=Level*Resolution*Resolution*Resolution;
            CacheSlots[Level*4]=(uint)SlotX;
            CacheSlots[Level*4+1]=(uint)SlotY;
            CacheSlots[Level*4+2]=(uint)SlotZ;
            CacheSlots[Level*4+3]=(uint)First;
            for(var Z=0;Z<Resolution;Z++){
                for(var Y=0;Y<Resolution;Y++){
                    for(var X=0;X<Resolution;X++){
                        var Probe=First+(((Z+SlotZ)%Resolution)*Resolution+(Y+SlotY)%Resolution)*Resolution+(X+SlotX)%Resolution;
                        var Coordinate=new LongVector3(Minimum.X+X,Minimum.Y+Y,Minimum.Z+Z);
                        if(!this.CacheSlotKnown[Probe]||this.CacheSlotCoordinate[Probe]!=Coordinate){
                            this.CacheSlotKnown[Probe]=true;
                            this.CacheSlotCoordinate[Probe]=Coordinate;
                            this.CacheValid[Probe]=0;
                            var Offset=new Vector3(X,Y,Z)-new Vector3(Resolution*0.5f);
                            Fresh.Add(((uint)Level,(uint)Probe,(uint)(X|(Y<<8)|(Z<<16)),Level*1e6f+Offset.LengthSquared()));
                        }
                    }
                }
            }
        }
        var CacheBudget=Tier.CacheProbesPerFrame;
        // The finest level first and, within a level, the probes nearest the camera.
        Fresh.Sort(static (A,B)=>A.Order.CompareTo(B.Order));
        this.CacheStampValue++;
        foreach(var Item in Fresh){
            if(this.CacheWork.Count>=CacheBudget){
                break;
            }
            this.CacheWork.Add((Item.Level,Item.Probe,0u,Item.Local));
            this.CacheStamp[Item.Probe]=this.CacheStampValue;
        }
        Statistics.FreshProbes=Fresh.Count;
        // The rest of the budget refreshes the probes round robin (a fresh one the budget did not reach is traced when its turn comes), the finer
        // levels more often: two thirds of what remains to level 0, two thirds of the rest to level 1, and so on.
        var Remaining=CacheBudget-this.CacheWork.Count;
        for(var Level=0;Level<Levels&&Remaining>0;Level++){
            var Share=Level==Levels-1?Remaining:Math.Max(1,Remaining*2/3);
            var PerLevel=Resolution*Resolution*Resolution;
            var First=Level*PerLevel;
            for(var Count=0;Count<Math.Min(Share,PerLevel);Count++){
                var Local=this.CacheCursor[Level]%PerLevel;
                this.CacheCursor[Level]=Local+1;
                var X=Local%Resolution;
                var Y=Local/Resolution%Resolution;
                var Z=Local/(Resolution*Resolution);
                var Probe=First+(((Z+(int)CacheSlots[Level*4+2])%Resolution)*Resolution+(Y+(int)CacheSlots[Level*4+1])%Resolution)*Resolution+(X+(int)CacheSlots[Level*4])%Resolution;
                if(this.CacheStamp[Probe]==this.CacheStampValue){
                    continue;
                }
                this.CacheStamp[Probe]=this.CacheStampValue;
                this.CacheWork.Add(((uint)Level,(uint)Probe,this.CacheValid[Probe],(uint)(X|(Y<<8)|(Z<<16))));
                Remaining--;
            }
        }
        Statistics.TracedProbes=this.CacheWork.Count;
        // The validity the passes read is the one before this frame's traces (card lighting runs before them); a fresh probe is used from the next frame.
        this.ValidRing.Ensure(this,this.CacheProbeCount*4,WaitForGpuIdle);
        fixed(uint*Source=this.CacheValid){
            Buffer.MemoryCopy(Source,this.ValidRing.Slot(FrameIndex),this.CacheProbeCount*4,this.CacheProbeCount*4);
        }
        foreach(var Item in this.CacheWork){
            this.CacheValid[Item.Probe]=1;
        }
        // Work items: the card tiles (8 x 8 texels), then the probes.
        var TileCount=0;
        foreach(var Light in this.LightCards){
            TileCount+=((Light.Width+7)/8)*((Light.Height+7)/8);
        }
        this.WorkRing.Ensure(this,Math.Max(TileCount+this.CacheWork.Count,1)*16,WaitForGpuIdle);
        var Work=(uint*)this.WorkRing.Slot(FrameIndex);
        var WorkIndex=0;
        foreach(var Light in this.LightCards){
            ref var State=ref this.Objects[Light.Object];
            for(var TileY=0;TileY<(Light.Height+7)/8;TileY++){
                for(var TileX=0;TileX<(Light.Width+7)/8;TileX++){
                    Work[WorkIndex*4]=(uint)Light.Card;
                    Work[WorkIndex*4+1]=(uint)(TileX|(TileY<<16));
                    Work[WorkIndex*4+2]=(uint)State.RecordIndex;
                    Work[WorkIndex*4+3]=(uint)Light.Face;
                    WorkIndex++;
                }
            }
        }
        foreach(var Item in this.CacheWork){
            Work[WorkIndex*4]=Item.Level;
            Work[WorkIndex*4+1]=Item.Probe;
            Work[WorkIndex*4+2]=Item.HadData;
            Work[WorkIndex*4+3]=Item.Local;
            WorkIndex++;
        }
        this.Plan=new FramePlan(Tier,Input,TileCount,this.CacheWork.Count,GridOrigin,PreviousViewProjection,PreviousCamera,HistoryUsable,
            HistoryUsable&&this.SceneColorValid,CacheOrigins,CacheSlots,CacheScales,Levels);
        this.Statistics=Statistics;
        // What the next frame reprojects with.
        this.PreviousViewProjection=Input.ViewProjection;
        this.PreviousCameraPosition=Input.CameraPosition;
        this.PreviousOriginVoxel=Field.OriginVoxel;
        this.PreviousOriginRemainder=Field.OriginRemainder;
        this.PlanPreviousExposure=this.PreviousExposure;
        this.PreviousExposure=Input.ManualExposure;
        return true;
    }
    private void Release(ref ObjectState State){
        for(var Face=0;Face<LumenCards.FaceCount;Face++){
            var Card=State.Faces[Face];
            if(Card.Allocated){
                this.Atlas!.Free(Card.X,Card.Y,Card.Width,Card.Height);
            }
        }
        State=default;
    }
    private static Vector3 AxisScales(in Matrix4x4 Matrix)=>new(
        new Vector3(Matrix.M11,Matrix.M12,Matrix.M13).Length(),new Vector3(Matrix.M21,Matrix.M22,Matrix.M23).Length(),new Vector3(Matrix.M31,Matrix.M32,Matrix.M33).Length());
    /// <summary>Writes the 3x4 rows that map a point by a row-vector matrix: (dot(row.xyz, p) + row.w) per output axis.</summary>
    private static void WriteRows(float*Target,in Matrix4x4 Matrix){
        Target[0]=Matrix.M11;
        Target[1]=Matrix.M21;
        Target[2]=Matrix.M31;
        Target[3]=Matrix.M41;
        Target[4]=Matrix.M12;
        Target[5]=Matrix.M22;
        Target[6]=Matrix.M32;
        Target[7]=Matrix.M42;
        Target[8]=Matrix.M13;
        Target[9]=Matrix.M23;
        Target[10]=Matrix.M33;
        Target[11]=Matrix.M43;
    }
    private static (Vector3 Low,Vector3 High)TransformBounds(Vector3 Minimum,Vector3 Maximum,in Matrix4x4 Matrix){
        var Low=new Vector3(float.MaxValue);
        var High=new Vector3(float.MinValue);
        for(var Corner=0;Corner<8;Corner++){
            var Point=Vector3.Transform(new Vector3((Corner&1)==0?Minimum.X:Maximum.X,(Corner&2)==0?Minimum.Y:Maximum.Y,(Corner&4)==0?Minimum.Z:Maximum.Z),Matrix);
            Low=Vector3.Min(Low,Point);
            High=Vector3.Max(High,Point);
        }
        return (Low,High);
    }
    private static bool IsFinite(in Matrix4x4 Matrix)=>
        float.IsFinite(Matrix.M11)&&float.IsFinite(Matrix.M12)&&float.IsFinite(Matrix.M13)&&float.IsFinite(Matrix.M14)
        &&float.IsFinite(Matrix.M21)&&float.IsFinite(Matrix.M22)&&float.IsFinite(Matrix.M23)&&float.IsFinite(Matrix.M24)
        &&float.IsFinite(Matrix.M31)&&float.IsFinite(Matrix.M32)&&float.IsFinite(Matrix.M33)&&float.IsFinite(Matrix.M34)
        &&float.IsFinite(Matrix.M41)&&float.IsFinite(Matrix.M42)&&float.IsFinite(Matrix.M43)&&float.IsFinite(Matrix.M44);
    // ---------------------------------------------------------------------------------------------------------------------------------------
    // Raster: the surface prepass and the card captures, drawn by the renderer with the Stage root signature into these targets.
    internal Capture CaptureAt(int Index)=>this.Captures[Index];
    internal byte*CaptureFrameSlot(uint FrameIndex,int Index)=>this.CaptureConstantsMapped+this.CaptureOffset(FrameIndex,Index);
    internal ulong CaptureFrameAddress(uint FrameIndex,int Index)=>this.CaptureConstants->GetGPUVirtualAddress()+this.CaptureOffset(FrameIndex,Index);
    internal byte*CaptureObjectSlot(uint FrameIndex,int Index)=>this.CaptureConstantsMapped+this.CaptureOffset(FrameIndex,Index)+this.FrameConstantsStride;
    internal ulong CaptureObjectAddress(uint FrameIndex,int Index)=>this.CaptureFrameAddress(FrameIndex,Index)+this.FrameConstantsStride;
    private ulong CaptureOffset(uint FrameIndex,int Index)=>((ulong)FrameIndex*MaximumCaptures+(ulong)Index)*(this.FrameConstantsStride+this.ObjectConstantsStride);
    private const int MaximumCaptures=512;
    internal D3D12_CPU_DESCRIPTOR_HANDLE CardRtv(int Target)=>new(this.RtvHeap->GetCPUDescriptorHandleForHeapStart(),RtvCardAlbedo+Target,this.RtvSize);
    internal D3D12_CPU_DESCRIPTOR_HANDLE CardDsv=>new(this.DsvHeap->GetCPUDescriptorHandleForHeapStart(),DsvCard,this.DsvSize);
    internal D3D12_CPU_DESCRIPTOR_HANDLE SurfaceRtv(int Target)=>new(this.RtvHeap->GetCPUDescriptorHandleForHeapStart(),RtvSurfaceNormal+Target,this.RtvSize);
    internal D3D12_CPU_DESCRIPTOR_HANDLE SurfaceDsv=>new(this.DsvHeap->GetCPUDescriptorHandleForHeapStart(),DsvSurface,this.DsvSize);
    /// <summary>Keeps last frame's surface for the reprojection, then makes the surface targets writable and clears them (distance 0 = nothing).</summary>
    internal void BeginSurface(ID3D12GraphicsCommandList*CommandList){
        if(this.SurfaceValid){
            D3D12_RESOURCE_BARRIER*ToCopy=stackalloc D3D12_RESOURCE_BARRIER[4];
            ToCopy[0]=D3D12_RESOURCE_BARRIER.InitTransition(this.SurfaceNormal,ShaderResourceState,D3D12_RESOURCE_STATE_COPY_SOURCE);
            ToCopy[1]=D3D12_RESOURCE_BARRIER.InitTransition(this.SurfaceDistance,ShaderResourceState,D3D12_RESOURCE_STATE_COPY_SOURCE);
            ToCopy[2]=D3D12_RESOURCE_BARRIER.InitTransition(this.PreviousSurfaceNormal,ShaderResourceState,D3D12_RESOURCE_STATE_COPY_DEST);
            ToCopy[3]=D3D12_RESOURCE_BARRIER.InitTransition(this.PreviousSurfaceDistance,ShaderResourceState,D3D12_RESOURCE_STATE_COPY_DEST);
            CommandList->ResourceBarrier(4,ToCopy);
            CommandList->CopyResource(this.PreviousSurfaceNormal,this.SurfaceNormal);
            CommandList->CopyResource(this.PreviousSurfaceDistance,this.SurfaceDistance);
            D3D12_RESOURCE_BARRIER*Back=stackalloc D3D12_RESOURCE_BARRIER[4];
            Back[0]=D3D12_RESOURCE_BARRIER.InitTransition(this.SurfaceNormal,D3D12_RESOURCE_STATE_COPY_SOURCE,D3D12_RESOURCE_STATE_RENDER_TARGET);
            Back[1]=D3D12_RESOURCE_BARRIER.InitTransition(this.SurfaceDistance,D3D12_RESOURCE_STATE_COPY_SOURCE,D3D12_RESOURCE_STATE_RENDER_TARGET);
            Back[2]=D3D12_RESOURCE_BARRIER.InitTransition(this.PreviousSurfaceNormal,D3D12_RESOURCE_STATE_COPY_DEST,ShaderResourceState);
            Back[3]=D3D12_RESOURCE_BARRIER.InitTransition(this.PreviousSurfaceDistance,D3D12_RESOURCE_STATE_COPY_DEST,ShaderResourceState);
            CommandList->ResourceBarrier(4,Back);
        } else {
            D3D12_RESOURCE_BARRIER*ToTarget=stackalloc D3D12_RESOURCE_BARRIER[2];
            ToTarget[0]=D3D12_RESOURCE_BARRIER.InitTransition(this.SurfaceNormal,ShaderResourceState,D3D12_RESOURCE_STATE_RENDER_TARGET);
            ToTarget[1]=D3D12_RESOURCE_BARRIER.InitTransition(this.SurfaceDistance,ShaderResourceState,D3D12_RESOURCE_STATE_RENDER_TARGET);
            CommandList->ResourceBarrier(2,ToTarget);
        }
        var Zero=Vector4.Zero;
        CommandList->ClearRenderTargetView(this.SurfaceRtv(0),(float*)&Zero,0,null);
        CommandList->ClearRenderTargetView(this.SurfaceRtv(1),(float*)&Zero,0,null);
        CommandList->ClearDepthStencilView(this.SurfaceDsv,D3D12_CLEAR_FLAGS.D3D12_CLEAR_FLAG_DEPTH,1f,0,0,null);
    }
    internal void EndSurface(ID3D12GraphicsCommandList*CommandList){
        D3D12_RESOURCE_BARRIER*Back=stackalloc D3D12_RESOURCE_BARRIER[2];
        Back[0]=D3D12_RESOURCE_BARRIER.InitTransition(this.SurfaceNormal,D3D12_RESOURCE_STATE_RENDER_TARGET,ShaderResourceState);
        Back[1]=D3D12_RESOURCE_BARRIER.InitTransition(this.SurfaceDistance,D3D12_RESOURCE_STATE_RENDER_TARGET,ShaderResourceState);
        CommandList->ResourceBarrier(2,Back);
        this.SurfaceValid=true;
    }
    internal void BeginCaptures(ID3D12GraphicsCommandList*CommandList){
        D3D12_RESOURCE_BARRIER*ToTarget=stackalloc D3D12_RESOURCE_BARRIER[4];
        ToTarget[0]=D3D12_RESOURCE_BARRIER.InitTransition(this.CardAlbedo,ShaderResourceState,D3D12_RESOURCE_STATE_RENDER_TARGET);
        ToTarget[1]=D3D12_RESOURCE_BARRIER.InitTransition(this.CardNormal,ShaderResourceState,D3D12_RESOURCE_STATE_RENDER_TARGET);
        ToTarget[2]=D3D12_RESOURCE_BARRIER.InitTransition(this.CardEmissive,ShaderResourceState,D3D12_RESOURCE_STATE_RENDER_TARGET);
        ToTarget[3]=D3D12_RESOURCE_BARRIER.InitTransition(this.CardDepth,ShaderResourceState,D3D12_RESOURCE_STATE_DEPTH_WRITE);
        CommandList->ResourceBarrier(4,ToTarget);
    }
    /// <summary>Clears one card's tile in every target before its mesh is drawn into it.</summary>
    internal void ClearCard(ID3D12GraphicsCommandList*CommandList,in Capture Card){
        RECT Rect=new(){ left=Card.X,top=Card.Y,right=Card.X+Card.Width,bottom=Card.Y+Card.Height };
        var Zero=Vector4.Zero;
        for(var Target=0;Target<3;Target++){
            CommandList->ClearRenderTargetView(this.CardRtv(Target),(float*)&Zero,1,&Rect);
        }
        CommandList->ClearDepthStencilView(this.CardDsv,D3D12_CLEAR_FLAGS.D3D12_CLEAR_FLAG_DEPTH,1f,0,1,&Rect);
    }
    internal void EndCaptures(ID3D12GraphicsCommandList*CommandList){
        D3D12_RESOURCE_BARRIER*Back=stackalloc D3D12_RESOURCE_BARRIER[4];
        Back[0]=D3D12_RESOURCE_BARRIER.InitTransition(this.CardAlbedo,D3D12_RESOURCE_STATE_RENDER_TARGET,ShaderResourceState);
        Back[1]=D3D12_RESOURCE_BARRIER.InitTransition(this.CardNormal,D3D12_RESOURCE_STATE_RENDER_TARGET,ShaderResourceState);
        Back[2]=D3D12_RESOURCE_BARRIER.InitTransition(this.CardEmissive,D3D12_RESOURCE_STATE_RENDER_TARGET,ShaderResourceState);
        Back[3]=D3D12_RESOURCE_BARRIER.InitTransition(this.CardDepth,D3D12_RESOURCE_STATE_DEPTH_WRITE,ShaderResourceState);
        CommandList->ResourceBarrier(4,Back);
    }
    // ---------------------------------------------------------------------------------------------------------------------------------------
    // Compute.
    /// <summary>
    /// Records the passes of this frame (after <see cref="Prepare"/> and the raster parts). Replaces the compute root signature, pipeline and
    /// descriptor heap. Leaves the diffuse and reflection outputs readable by the Stage program.
    /// </summary>
    internal void RecordCompute(ID3D12GraphicsCommandList*CommandList,uint FrameIndex,in LumenComputeBindings Bindings){
        this.DescribeExternal(Bindings);
        this.LastBindings=Bindings;
        var Flags=this.Bind(CommandList,FrameIndex,Bindings);
        this.RecordPasses(CommandList,FrameIndex,Bindings,Flags,this.WorkRing.Address(FrameIndex,this.DummyBuffer));
    }
    /// <summary>
    /// Debugging (Unreal's r.Lumen.Visualize): overwrites the lit frame with the diffuse irradiance on a white surface, the reflections or the surface
    /// cache, through this frame's exposure. The HDR target is a render target before and after.
    /// </summary>
    internal void RecordVisualize(ID3D12GraphicsCommandList*CommandList,uint FrameIndex,LumenVisualization Mode,ID3D12Resource*HdrColor){
        if(this.DescribedSceneColor!=(nint)HdrColor){
            this.DescribedSceneColor=(nint)HdrColor;
            this.WriteTextureUav(HdrColor,RadianceFormat,UavVisualize);
        }
        var Flags=this.Bind(CommandList,FrameIndex,this.LastBindings);
        D3D12_RESOURCE_BARRIER ToUav=D3D12_RESOURCE_BARRIER.InitTransition(HdrColor,D3D12_RESOURCE_STATE_RENDER_TARGET,D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
        CommandList->ResourceBarrier(1,&ToUav);
        this.VisualizeMode=(uint)Mode;
        this.Dispatch(CommandList,FrameIndex,PassVisualize,Flags,((uint)this.Plan.Input.Width+7u)/8u,((uint)this.Plan.Input.Height+7u)/8u,1);
        D3D12_RESOURCE_BARRIER ToTarget=D3D12_RESOURCE_BARRIER.InitTransition(HdrColor,D3D12_RESOURCE_STATE_UNORDERED_ACCESS,D3D12_RESOURCE_STATE_RENDER_TARGET);
        CommandList->ResourceBarrier(1,&ToTarget);
    }
    private uint VisualizeMode;
    private uint Bind(ID3D12GraphicsCommandList*CommandList,uint FrameIndex,in LumenComputeBindings Bindings){
        var Plan=this.Plan;
        var Input=Plan.Input;
        CommandList->SetComputeRootSignature(this.RootSignature);
        CommandList->SetPipelineState(this.PipelineState);
        ID3D12DescriptorHeap*Heap=this.DescriptorHeap;
        CommandList->SetDescriptorHeaps(1,&Heap);
        var Start=this.DescriptorHeap->GetGPUDescriptorHandleForHeapStart();
        CommandList->SetComputeRootConstantBufferView(1,Bindings.FrameConstants);
        CommandList->SetComputeRootShaderResourceView(2,this.ObjectRing.Address(FrameIndex,this.DummyBuffer));
        CommandList->SetComputeRootShaderResourceView(3,this.CardRing.Address(FrameIndex,this.DummyBuffer));
        CommandList->SetComputeRootShaderResourceView(4,this.CellRing.Address(FrameIndex,this.DummyBuffer));
        CommandList->SetComputeRootShaderResourceView(5,this.CellObjectRing.Address(FrameIndex,this.DummyBuffer));
        CommandList->SetComputeRootShaderResourceView(6,Bindings.Field.MeshRecordAddress!=0?Bindings.Field.MeshRecordAddress:this.DummyBuffer->GetGPUVirtualAddress());
        CommandList->SetComputeRootShaderResourceView(7,Bindings.Field.MeshDistanceAddress!=0?Bindings.Field.MeshDistanceAddress:this.DummyBuffer->GetGPUVirtualAddress());
        CommandList->SetComputeRootShaderResourceView(8,Bindings.LocalLights);
        CommandList->SetComputeRootShaderResourceView(9,Bindings.SpotShadowProjections);
        CommandList->SetComputeRootShaderResourceView(10,Bindings.PointShadowProjections);
        CommandList->SetComputeRootShaderResourceView(11,Bindings.SkyIrradianceSh);
        CommandList->SetComputeRootShaderResourceView(12,this.WorkRing.Address(FrameIndex,this.DummyBuffer));
        CommandList->SetComputeRootShaderResourceView(13,Bindings.ExposureState);
        CommandList->SetComputeRootShaderResourceView(14,this.ValidRing.Address(FrameIndex,this.DummyBuffer));
        CommandList->SetComputeRootDescriptorTable(15,Start);
        CommandList->SetComputeRootDescriptorTable(16,new D3D12_GPU_DESCRIPTOR_HANDLE(Start,UavCardRadiance,this.DescriptorSize));
        var Flags=0u;
        if(Plan.HistoryValid){ Flags|=FlagHistory; }
        if(Plan.SceneColorValid){ Flags|=FlagSceneColorHistory; }
        if(Bindings.SkyMap!=null){ Flags|=FlagSkyMap; }
        if(Input.Settings.FinalGatherScreenTraces){ Flags|=FlagGatherScreenTraces; }
        if(Input.Settings.ReflectionScreenTraces){ Flags|=FlagReflectionScreenTraces; }
        if(Input.AutoExposure){ Flags|=FlagAutoExposure; }
        return Flags;
    }
    private void RecordPasses(ID3D12GraphicsCommandList*CommandList,uint FrameIndex,in LumenComputeBindings Bindings,uint Flags,ulong WorkAddress){
        var Plan=this.Plan;
        var Tier=Plan.Tier;
        var Input=Plan.Input;
        // 0: card lighting (the shadow atlas is read by compute too).
        if(Plan.CardTiles>0){
            D3D12_RESOURCE_BARRIER*Before=stackalloc D3D12_RESOURCE_BARRIER[2];
            Before[0]=D3D12_RESOURCE_BARRIER.InitTransition(this.CardRadiance,ShaderResourceState,D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
            Before[1]=D3D12_RESOURCE_BARRIER.InitTransition(Bindings.ShadowAtlas,D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE,ShaderResourceState);
            CommandList->ResourceBarrier(2,Before);
            CommandList->SetComputeRootShaderResourceView(12,WorkAddress);
            this.Dispatch(CommandList,FrameIndex,PassCardLighting,Flags,(uint)Plan.CardTiles,1,1);
            D3D12_RESOURCE_BARRIER*After=stackalloc D3D12_RESOURCE_BARRIER[2];
            After[0]=D3D12_RESOURCE_BARRIER.InitTransition(this.CardRadiance,D3D12_RESOURCE_STATE_UNORDERED_ACCESS,ShaderResourceState);
            After[1]=D3D12_RESOURCE_BARRIER.InitTransition(Bindings.ShadowAtlas,ShaderResourceState,D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE);
            CommandList->ResourceBarrier(2,After);
        }
        // 1: the radiance cache.
        if(Plan.CacheProbes>0){
            D3D12_RESOURCE_BARRIER*Before=stackalloc D3D12_RESOURCE_BARRIER[2];
            Before[0]=D3D12_RESOURCE_BARRIER.InitTransition(this.CacheRadiance,ShaderResourceState,D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
            Before[1]=D3D12_RESOURCE_BARRIER.InitTransition(this.CacheIrradiance,ShaderResourceState,D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
            CommandList->ResourceBarrier(2,Before);
            CommandList->SetComputeRootShaderResourceView(12,WorkAddress+(ulong)Plan.CardTiles*16);
            this.Dispatch(CommandList,FrameIndex,PassRadianceCache,Flags,(uint)Plan.CacheProbes,1,1);
            D3D12_RESOURCE_BARRIER*After=stackalloc D3D12_RESOURCE_BARRIER[2];
            After[0]=D3D12_RESOURCE_BARRIER.InitTransition(this.CacheRadiance,D3D12_RESOURCE_STATE_UNORDERED_ACCESS,ShaderResourceState);
            After[1]=D3D12_RESOURCE_BARRIER.InitTransition(this.CacheIrradiance,D3D12_RESOURCE_STATE_UNORDERED_ACCESS,ShaderResourceState);
            CommandList->ResourceBarrier(2,After);
        }
        CommandList->SetComputeRootShaderResourceView(12,WorkAddress);
        // 2-3: the screen probes.
        this.Transition(CommandList,ShaderResourceState,D3D12_RESOURCE_STATE_UNORDERED_ACCESS,this.ProbeRadiance,this.ProbeInfo);
        this.Dispatch(CommandList,FrameIndex,PassScreenProbeTrace,Flags,(uint)this.ProbeCountX,(uint)this.ProbeCountY,1);
        this.Transition(CommandList,D3D12_RESOURCE_STATE_UNORDERED_ACCESS,ShaderResourceState,this.ProbeRadiance,this.ProbeInfo);
        this.Transition(CommandList,ShaderResourceState,D3D12_RESOURCE_STATE_UNORDERED_ACCESS,this.ProbeSh,null);
        this.Dispatch(CommandList,FrameIndex,PassScreenProbeFilter,Flags,(uint)this.ProbeCountX,(uint)this.ProbeCountY,1);
        this.Transition(CommandList,D3D12_RESOURCE_STATE_UNORDERED_ACCESS,ShaderResourceState,this.ProbeSh,null);
        // 4: integrate, then keep the result as next frame's history.
        this.Transition(CommandList,ShaderResourceState,D3D12_RESOURCE_STATE_UNORDERED_ACCESS,this.Diffuse,null);
        this.Dispatch(CommandList,FrameIndex,PassIntegrate,Flags,((uint)Input.Width+7u)/8u,((uint)Input.Height+7u)/8u,1);
        this.CopyToHistory(CommandList,this.Diffuse,this.DiffuseHistory,D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
        // 5-6: the reflections.
        if(Tier.Reflections&&Input.Settings.Reflections){
            this.Transition(CommandList,ShaderResourceState,D3D12_RESOURCE_STATE_UNORDERED_ACCESS,this.ReflectionTrace,null);
            this.Dispatch(CommandList,FrameIndex,PassReflectionTrace,Flags,((uint)this.TraceWidth+7u)/8u,((uint)this.TraceHeight+7u)/8u,1);
            this.Transition(CommandList,D3D12_RESOURCE_STATE_UNORDERED_ACCESS,ShaderResourceState,this.ReflectionTrace,null);
            this.Transition(CommandList,ShaderResourceState,D3D12_RESOURCE_STATE_UNORDERED_ACCESS,this.Reflection,null);
            this.Dispatch(CommandList,FrameIndex,PassReflectionResolve,Flags,((uint)Input.Width+7u)/8u,((uint)Input.Height+7u)/8u,1);
            this.CopyToHistory(CommandList,this.Reflection,this.ReflectionHistory,D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
        }
        // The exposure this frame is drawn with (the metering comes after the main pass) is the scene colour history's in the next frame.
        D3D12_RESOURCE_BARRIER*ToCopy=stackalloc D3D12_RESOURCE_BARRIER[2];
        ToCopy[0]=D3D12_RESOURCE_BARRIER.InitTransition(Bindings.ExposureStateResource,ShaderResourceState,D3D12_RESOURCE_STATE_COPY_SOURCE);
        ToCopy[1]=D3D12_RESOURCE_BARRIER.InitTransition(this.HistoryExposure,ShaderResourceState,D3D12_RESOURCE_STATE_COPY_DEST);
        CommandList->ResourceBarrier(2,ToCopy);
        CommandList->CopyBufferRegion(this.HistoryExposure,0,Bindings.ExposureStateResource,0,16);
        D3D12_RESOURCE_BARRIER*Back=stackalloc D3D12_RESOURCE_BARRIER[2];
        Back[0]=D3D12_RESOURCE_BARRIER.InitTransition(Bindings.ExposureStateResource,D3D12_RESOURCE_STATE_COPY_SOURCE,ShaderResourceState);
        Back[1]=D3D12_RESOURCE_BARRIER.InitTransition(this.HistoryExposure,D3D12_RESOURCE_STATE_COPY_DEST,ShaderResourceState);
        CommandList->ResourceBarrier(2,Back);
        this.HistoryValid=true;
    }
    /// <summary>Copies the lit frame (sky included, before the camera previews) as next frame's scene colour history. The HDR target is a render target.</summary>
    internal void CopySceneColor(ID3D12GraphicsCommandList*CommandList,ID3D12Resource*HdrColor){
        D3D12_RESOURCE_BARRIER*Before=stackalloc D3D12_RESOURCE_BARRIER[2];
        Before[0]=D3D12_RESOURCE_BARRIER.InitTransition(HdrColor,D3D12_RESOURCE_STATE_RENDER_TARGET,D3D12_RESOURCE_STATE_COPY_SOURCE);
        Before[1]=D3D12_RESOURCE_BARRIER.InitTransition(this.SceneColorHistory,ShaderResourceState,D3D12_RESOURCE_STATE_COPY_DEST);
        CommandList->ResourceBarrier(2,Before);
        CommandList->CopyResource(this.SceneColorHistory,HdrColor);
        D3D12_RESOURCE_BARRIER*After=stackalloc D3D12_RESOURCE_BARRIER[2];
        After[0]=D3D12_RESOURCE_BARRIER.InitTransition(HdrColor,D3D12_RESOURCE_STATE_COPY_SOURCE,D3D12_RESOURCE_STATE_RENDER_TARGET);
        After[1]=D3D12_RESOURCE_BARRIER.InitTransition(this.SceneColorHistory,D3D12_RESOURCE_STATE_COPY_DEST,ShaderResourceState);
        CommandList->ResourceBarrier(2,After);
        this.SceneColorValid=true;
    }
    /// <summary>Forgets the history (a frame without Lumen, a camera cut or a resize): the next frame starts from its own samples.</summary>
    internal void InvalidateHistory(){
        this.HistoryValid=false;
        this.SceneColorValid=false;
        this.SurfaceValid=false;
    }
    private void CopyToHistory(ID3D12GraphicsCommandList*CommandList,ID3D12Resource*Current,ID3D12Resource*History,D3D12_RESOURCE_STATES CurrentState){
        D3D12_RESOURCE_BARRIER*Before=stackalloc D3D12_RESOURCE_BARRIER[2];
        Before[0]=D3D12_RESOURCE_BARRIER.InitTransition(Current,CurrentState,D3D12_RESOURCE_STATE_COPY_SOURCE);
        Before[1]=D3D12_RESOURCE_BARRIER.InitTransition(History,ShaderResourceState,D3D12_RESOURCE_STATE_COPY_DEST);
        CommandList->ResourceBarrier(2,Before);
        CommandList->CopyResource(History,Current);
        D3D12_RESOURCE_BARRIER*After=stackalloc D3D12_RESOURCE_BARRIER[2];
        After[0]=D3D12_RESOURCE_BARRIER.InitTransition(Current,D3D12_RESOURCE_STATE_COPY_SOURCE,ShaderResourceState);
        After[1]=D3D12_RESOURCE_BARRIER.InitTransition(History,D3D12_RESOURCE_STATE_COPY_DEST,ShaderResourceState);
        CommandList->ResourceBarrier(2,After);
    }
    private void Transition(ID3D12GraphicsCommandList*CommandList,D3D12_RESOURCE_STATES From,D3D12_RESOURCE_STATES To,ID3D12Resource*First,ID3D12Resource*Second){
        D3D12_RESOURCE_BARRIER*Barriers=stackalloc D3D12_RESOURCE_BARRIER[2];
        Barriers[0]=D3D12_RESOURCE_BARRIER.InitTransition(First,From,To);
        var Count=1u;
        if(Second!=null){
            Barriers[1]=D3D12_RESOURCE_BARRIER.InitTransition(Second,From,To);
            Count=2;
        }
        CommandList->ResourceBarrier(Count,Barriers);
    }
    private void Dispatch(ID3D12GraphicsCommandList*CommandList,uint FrameIndex,uint Pass,uint Flags,uint GroupsX,uint GroupsY,uint GroupsZ){
        var Slot=FrameIndex*PassCount+Pass;
        this.WriteConstants((float*)(this.ConstantsMapped+Slot*ConstantsStride),Pass,Flags);
        CommandList->SetComputeRootConstantBufferView(0,this.ConstantsBuffer->GetGPUVirtualAddress()+Slot*ConstantsStride);
        CommandList->Dispatch(GroupsX,GroupsY,GroupsZ);
    }
    private void WriteConstants(float*Target,uint Pass,uint Flags){
        var Plan=this.Plan;
        var Input=Plan.Input;
        var Tier=Plan.Tier;
        var Integers=(uint*)Target;
        Integers[0]=Pass;
        Integers[1]=Pass==PassVisualize?this.VisualizeMode:Pass==PassCardLighting?(uint)Plan.CardTiles:(uint)Plan.CacheProbes;
        Integers[2]=Input.FrameNumber;
        Integers[3]=Flags;
        Integers[4]=(uint)Input.Width;
        Integers[5]=(uint)Input.Height;
        Integers[6]=(uint)this.TraceWidth;
        Integers[7]=(uint)this.TraceHeight;
        Integers[8]=(uint)this.ProbeCountX;
        Integers[9]=(uint)this.ProbeCountY;
        Integers[10]=(uint)Tier.ProbeTexels;
        Integers[11]=(uint)Tier.ReflectionDownsample;
        // The probes move through their tile and the rays through their octahedral texel over frames (Halton 2, 3), so time fills the gaps.
        var Frame=Input.FrameNumber;
        Target[12]=MathF.Floor(Halton(Frame%16+1,2)*Tier.ProbeTexels);
        Target[13]=MathF.Floor(Halton(Frame%16+1,3)*Tier.ProbeTexels);
        Target[14]=Halton(Frame%32+1,5);
        Target[15]=Halton(Frame%32+1,7);
        var ViewProjection=Input.ViewProjection;
        var Inverse=Input.InverseViewProjection;
        var Previous=Plan.PreviousViewProjection;
        Buffer.MemoryCopy(&ViewProjection,Target+16,64,64);
        Buffer.MemoryCopy(&Inverse,Target+32,64,64);
        Buffer.MemoryCopy(&Previous,Target+48,64,64);
        Store(Target,64,Input.CameraPosition,this.PreviousExposureForHistory);
        Store(Target,68,Plan.PreviousCamera,0f);
        Target[72]=Input.Settings.MaxTraceDistanceMeters;
        Target[73]=2f*Tier.CacheSpacingMeters;
        Target[74]=2f;
        Target[75]=Input.Settings.MaxRoughnessToTraceReflections;
        Target[76]=Tier.AtlasTexels;
        Target[77]=1f/Tier.AtlasTexels;
        Target[78]=Input.Settings.DiffuseColorBoost;
        Target[79]=Input.Settings.SkylightLeaking;
        Target[80]=Plan.GridOrigin.X;
        Target[81]=0f;
        Target[82]=Plan.GridOrigin.Y;
        Target[83]=GridCellMeters;
        Integers[84]=GridSide;
        Integers[85]=(uint)this.Records.Count;
        Integers[86]=0;
        Integers[87]=0;
        for(var Level=0;Level<4;Level++){
            *(Vector4*)(Target+88+Level*4)=Plan.CacheScales[Level];
            *(Vector4*)(Target+104+Level*4)=Plan.CacheOrigins[Level];
            Integers[120+Level*4]=Plan.CacheSlots[Level*4];
            Integers[121+Level*4]=Plan.CacheSlots[Level*4+1];
            Integers[122+Level*4]=Plan.CacheSlots[Level*4+2];
            Integers[123+Level*4]=Plan.CacheSlots[Level*4+3];
        }
        Integers[136]=(uint)Plan.CacheLevels;
        Integers[137]=(uint)Tier.CacheResolution;
        Integers[138]=(uint)this.CacheProbesPerRow;
        Integers[139]=0;
        for(var Level=0;Level<4;Level++){
            var (Scale,Origin,Texel)=Input.Clipmaps[Level];
            *(Vector4*)(Target+140+Level*4)=Scale;
            *(Vector4*)(Target+156+Level*4)=Origin;
            *(Vector4*)(Target+172+Level*4)=Texel;
        }
        Integers[188]=(uint)Input.ClipmapLevels;
        Integers[189]=0;
        Integers[190]=0;
        Integers[191]=0;
        // Unreal's screen probe temporal filter keeps about ten frames; a history younger than that averages what it has.
        Target[192]=MathF.Min(1f,MathF.Max(1f/(this.HistoryFrames+1),0.12f*Input.Settings.FinalGatherLightingUpdateSpeed));
        Target[193]=0.2f;
        Target[194]=0.75f;
        Target[195]=Input.ManualExposure;
        static void Store(float*Target,int Index,Vector3 Value,float W){
            Target[Index]=Value.X;
            Target[Index+1]=Value.Y;
            Target[Index+2]=Value.Z;
            Target[Index+3]=W;
        }
    }
    /// <summary>The manual exposure the scene colour history was drawn with (the previous frame's).</summary>
    private float PreviousExposureForHistory=>this.Plan.SceneColorValid?this.PlanPreviousExposure:0f;
    private float PlanPreviousExposure;
    private static float Halton(uint Index,uint Base){
        var Fraction=1f;
        var Result=0f;
        while(Index>0){
            Fraction/=Base;
            Result+=Fraction*(Index%Base);
            Index/=Base;
        }
        return Result;
    }
    /// <summary>Writes the descriptors of resources the renderer owns when they change (the GPU is idle then, or they were never read).</summary>
    private void DescribeExternal(in LumenComputeBindings Bindings){
        for(var Level=0;Level<GlobalDistanceFieldSettings.MaximumClipmapCount;Level++){
            var Resource=Bindings.Field.Level(Level<Bindings.Field.LevelCount?Level:0);
            if(this.DescribedLevels[Level]==(nint)Resource){
                continue;
            }
            this.DescribedLevels[Level]=(nint)Resource;
            D3D12_SHADER_RESOURCE_VIEW_DESC Srv=new(){ Format=DXGI_FORMAT_R16_FLOAT,ViewDimension=D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_TEXTURE3D,Shader4ComponentMapping=(uint)TerraFX.Interop.DirectX.D3D12.D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING };
            Srv.Texture3D.MipLevels=1;
            this.Device.Device->CreateShaderResourceView(Resource,&Srv,this.CpuHandle(SrvClipmap+Level));
        }
        if(this.DescribedShadowAtlas!=(nint)Bindings.ShadowAtlas){
            this.DescribedShadowAtlas=(nint)Bindings.ShadowAtlas;
            this.WriteTextureSrv(Bindings.ShadowAtlas,DXGI_FORMAT_R32_FLOAT,SrvShadowAtlas);
        }
        if(this.DescribedSkyMap!=(nint)Bindings.SkyMap){
            this.DescribedSkyMap=(nint)Bindings.SkyMap;
            this.WriteTextureSrv(Bindings.SkyMap,RadianceFormat,SrvSkyRadiance);
        }
    }
    /// <summary>Writes the Stage t16 (diffuse irradiance) and t17 (reflected radiance) views; null views until the outputs exist.</summary>
    internal void WriteShadingDescriptors(D3D12_CPU_DESCRIPTOR_HANDLE DiffuseHandle,D3D12_CPU_DESCRIPTOR_HANDLE ReflectionHandle){
        D3D12_SHADER_RESOURCE_VIEW_DESC Srv=new(){ Format=IrradianceFormat,ViewDimension=D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_TEXTURE2D,Shader4ComponentMapping=(uint)TerraFX.Interop.DirectX.D3D12.D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING };
        Srv.Texture2D.MipLevels=1;
        this.Device.Device->CreateShaderResourceView(this.Diffuse,&Srv,DiffuseHandle);
        Srv.Format=RadianceFormat;
        this.Device.Device->CreateShaderResourceView(this.Reflection,&Srv,ReflectionHandle);
    }
    // ---------------------------------------------------------------------------------------------------------------------------------------
    // Resources.
    private void EnsurePipeline(){
        if(this.PipelineState!=null){
            return;
        }
        if(!ShaderArtifactRuntime.TryLoad(this.ArtifactPath,ShaderArtifactTargetKind.Compute,out var Package,out var Error)||Package is null){
            throw new InvalidDataException($"The Lumen-like lighting requires '{this.ArtifactPath}': {Error}");
        }
        var Bytecode=Package.GetBytecode("CSMain");
        this.RootSignature=this.CreateRootSignature();
        fixed(byte*Code=Bytecode){
            D3D12_COMPUTE_PIPELINE_STATE_DESC Desc=new(){ pRootSignature=this.RootSignature,CS=new D3D12_SHADER_BYTECODE { pShaderBytecode=Code,BytecodeLength=(nuint)Bytecode.Length } };
            ID3D12PipelineState*Pipeline;
            D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateComputePipelineState(&Desc,__uuidof<ID3D12PipelineState>(),(void**)&Pipeline),
                "ID3D12Device::CreateComputePipelineState","creating the Lumen pipeline");
            this.PipelineState=Pipeline;
        }
        this.DescriptorHeap=this.CreateHeap(D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV,DescriptorCount,D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE);
        this.RtvHeap=this.CreateHeap(D3D12_DESCRIPTOR_HEAP_TYPE_RTV,5,D3D12_DESCRIPTOR_HEAP_FLAG_NONE);
        this.DsvHeap=this.CreateHeap(D3D12_DESCRIPTOR_HEAP_TYPE_DSV,2,D3D12_DESCRIPTOR_HEAP_FLAG_NONE);
        this.DescriptorSize=this.Device.Device->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV);
        this.RtvSize=this.Device.Device->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_RTV);
        this.DsvSize=this.Device.Device->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_DSV);
        this.ConstantsBuffer=this.CreateUploadBuffer((ulong)this.FrameCount*PassCount*ConstantsStride,out this.ConstantsMapped);
        this.CaptureConstants=this.CreateUploadBuffer((ulong)this.FrameCount*MaximumCaptures*(this.FrameConstantsStride+this.ObjectConstantsStride),out this.CaptureConstantsMapped);
        D3D12_HEAP_PROPERTIES DefaultHeap=new(D3D12_HEAP_TYPE_DEFAULT);
        var DummyDesc=D3D12_RESOURCE_DESC.Buffer(256);
        ID3D12Resource*Dummy;
        D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateCommittedResource(&DefaultHeap,D3D12_HEAP_FLAG_NONE,&DummyDesc,D3D12_RESOURCE_STATE_COMMON,null,__uuidof<ID3D12Resource>(),(void**)&Dummy),
            "ID3D12Device::CreateCommittedResource","creating the Lumen placeholder buffer");
        this.DummyBuffer=Dummy;
        // Until the renderer's resources are described, the external slots hold null views.
        for(var Slot=0;Slot<SrvCount;Slot++){
            this.WriteTextureSrv(null,RadianceFormat,Slot);
        }
        this.HistoryExposure=this.CreateBuffer(16);
        D3D12_SHADER_RESOURCE_VIEW_DESC ExposureSrv=new(){ Format=DXGI_FORMAT_UNKNOWN,ViewDimension=D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_BUFFER,Shader4ComponentMapping=(uint)TerraFX.Interop.DirectX.D3D12.D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING };
        ExposureSrv.Buffer=new D3D12_BUFFER_SRV { FirstElement=0,NumElements=1,StructureByteStride=16 };
        this.Device.Device->CreateShaderResourceView(this.HistoryExposure,&ExposureSrv,this.CpuHandle(SrvHistoryExposure));
        for(var Level=0;Level<GlobalDistanceFieldSettings.MaximumClipmapCount;Level++){
            D3D12_SHADER_RESOURCE_VIEW_DESC Srv=new(){ Format=DXGI_FORMAT_R16_FLOAT,ViewDimension=D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_TEXTURE3D,Shader4ComponentMapping=(uint)TerraFX.Interop.DirectX.D3D12.D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING };
            Srv.Texture3D.MipLevels=1;
            this.Device.Device->CreateShaderResourceView(null,&Srv,this.CpuHandle(SrvClipmap+Level));
        }
        this.WriteTextureUav(null,RadianceFormat,UavVisualize);
    }
    private void EnsureTier(in LumenTier Tier,Action WaitForGpuIdle){
        var Key=new TierKey(Tier.AtlasTexels,Tier.CacheLevels,Tier.CacheResolution);
        if(this.Tier==Key){
            return;
        }
        if(this.Tier is not null){
            WaitForGpuIdle();
        }
        this.ReleaseTierResources();
        this.Tier=Key;
        this.Atlas=new LumenCardAtlas(Tier.AtlasTexels);
        for(var Index=0;Index<this.Objects.Length;Index++){
            this.Objects[Index]=default;
        }
        var Size=Tier.AtlasTexels;
        D3D12_CLEAR_VALUE ColorClear=new(){ Format=CardAlbedoFormat };
        this.CardAlbedo=this.CreateTexture(CardAlbedoFormat,Size,Size,D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET,&ColorClear);
        this.CardNormal=this.CreateTexture(CardNormalFormat,Size,Size,D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET,&ColorClear);
        D3D12_CLEAR_VALUE EmissiveClear=new(){ Format=CardEmissiveFormat };
        this.CardEmissive=this.CreateTexture(CardEmissiveFormat,Size,Size,D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET,&EmissiveClear);
        D3D12_CLEAR_VALUE DepthClear=new(){ Format=DXGI_FORMAT_D32_FLOAT };
        DepthClear.DepthStencil.Depth=1f;
        this.CardDepth=this.CreateTexture(DXGI_FORMAT_R32_TYPELESS,Size,Size,D3D12_RESOURCE_FLAG_ALLOW_DEPTH_STENCIL,&DepthClear);
        this.CardRadiance=this.CreateTexture(RadianceFormat,Size,Size,D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS,null);
        this.WriteRtv(this.CardAlbedo,CardAlbedoFormat,RtvCardAlbedo);
        this.WriteRtv(this.CardNormal,CardNormalFormat,RtvCardAlbedo+1);
        this.WriteRtv(this.CardEmissive,CardEmissiveFormat,RtvCardAlbedo+2);
        this.WriteDsv(this.CardDepth,DsvCard);
        this.WriteTextureSrv(this.CardAlbedo,CardAlbedoFormat,SrvCardAlbedo);
        this.WriteTextureSrv(this.CardNormal,CardNormalFormat,SrvCardNormal);
        this.WriteTextureSrv(this.CardEmissive,CardEmissiveFormat,SrvCardEmissive);
        this.WriteTextureSrv(this.CardDepth,DXGI_FORMAT_R32_FLOAT,SrvCardDepth);
        this.WriteTextureSrv(this.CardRadiance,RadianceFormat,SrvCardRadiance);
        this.WriteTextureUav(this.CardRadiance,RadianceFormat,UavCardRadiance);
        // The radiance cache: one atlas tile and nine SH coefficients per probe.
        this.CacheProbeCount=Tier.CacheLevels*Tier.CacheResolution*Tier.CacheResolution*Tier.CacheResolution;
        this.CacheProbesPerRow=(int)MathF.Ceiling(MathF.Sqrt(this.CacheProbeCount));
        var Rows=(this.CacheProbeCount+this.CacheProbesPerRow-1)/this.CacheProbesPerRow;
        this.CacheRadiance=this.CreateTexture(RadianceFormat,this.CacheProbesPerRow*CacheTile,Rows*CacheTile,D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS,null);
        this.CacheIrradiance=this.CreateBuffer((ulong)this.CacheProbeCount*9*16);
        this.WriteTextureSrv(this.CacheRadiance,RadianceFormat,SrvCacheRadiance);
        this.WriteTextureUav(this.CacheRadiance,RadianceFormat,UavCacheRadiance);
        this.WriteBufferViews(this.CacheIrradiance,this.CacheProbeCount*9,SrvCacheIrradiance,UavCacheIrradiance);
        this.CacheSlotCoordinate=new LongVector3[this.CacheProbeCount];
        this.CacheSlotKnown=new bool[this.CacheProbeCount];
        this.CacheValid=new uint[this.CacheProbeCount];
        this.CacheStamp=new int[this.CacheProbeCount];
        Array.Clear(this.CacheCursor);
        this.InvalidateHistory();
    }
    private void EnsureScreen(int Width,int Height,in LumenTier Tier,Action WaitForGpuIdle){
        var Key=(Width,Height,Tier.ProbeTexels,Tier.ReflectionDownsample);
        if(this.ScreenKey==Key){
            return;
        }
        // A size change comes from Resize, which waits for the GPU and calls ReleaseScreen first; another change replaces targets a frame may read.
        if(this.ScreenKey is not null){
            WaitForGpuIdle();
        }
        this.ReleaseScreen();
        this.ScreenKey=Key;
        this.ProbeCountX=(Width+Tier.ProbeTexels-1)/Tier.ProbeTexels;
        this.ProbeCountY=(Height+Tier.ProbeTexels-1)/Tier.ProbeTexels;
        this.TraceWidth=(Width+Tier.ReflectionDownsample-1)/Tier.ReflectionDownsample;
        this.TraceHeight=(Height+Tier.ReflectionDownsample-1)/Tier.ReflectionDownsample;
        D3D12_CLEAR_VALUE ColorClear=new(){ Format=SurfaceNormalFormat };
        this.SurfaceNormal=this.CreateTexture(SurfaceNormalFormat,Width,Height,D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET,&ColorClear);
        D3D12_CLEAR_VALUE DistanceClear=new(){ Format=SurfaceDistanceFormat };
        this.SurfaceDistance=this.CreateTexture(SurfaceDistanceFormat,Width,Height,D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET,&DistanceClear);
        this.PreviousSurfaceNormal=this.CreateTexture(SurfaceNormalFormat,Width,Height,D3D12_RESOURCE_FLAG_NONE,null);
        this.PreviousSurfaceDistance=this.CreateTexture(SurfaceDistanceFormat,Width,Height,D3D12_RESOURCE_FLAG_NONE,null);
        D3D12_CLEAR_VALUE DepthClear=new(){ Format=DXGI_FORMAT_D32_FLOAT };
        DepthClear.DepthStencil.Depth=1f;
        this.SurfaceDepth=this.CreateTexture(DXGI_FORMAT_D32_FLOAT,Width,Height,D3D12_RESOURCE_FLAG_ALLOW_DEPTH_STENCIL,&DepthClear,D3D12_RESOURCE_STATE_DEPTH_WRITE);
        this.SceneColorHistory=this.CreateTexture(RadianceFormat,Width,Height,D3D12_RESOURCE_FLAG_NONE,null);
        this.ProbeRadiance=this.CreateTexture(RadianceFormat,this.ProbeCountX*OctahedronTexels,this.ProbeCountY*OctahedronTexels,D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS,null);
        var Probes=this.ProbeCountX*this.ProbeCountY;
        this.ProbeInfo=this.CreateBuffer((ulong)Probes*2*16);
        this.ProbeSh=this.CreateBuffer((ulong)Probes*9*16);
        this.Diffuse=this.CreateTexture(IrradianceFormat,Width,Height,D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS,null);
        this.DiffuseHistory=this.CreateTexture(IrradianceFormat,Width,Height,D3D12_RESOURCE_FLAG_NONE,null);
        this.ReflectionTrace=this.CreateTexture(RadianceFormat,this.TraceWidth,this.TraceHeight,D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS,null);
        this.Reflection=this.CreateTexture(RadianceFormat,Width,Height,D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS,null);
        this.ReflectionHistory=this.CreateTexture(RadianceFormat,Width,Height,D3D12_RESOURCE_FLAG_NONE,null);
        this.WriteRtv(this.SurfaceNormal,SurfaceNormalFormat,RtvSurfaceNormal);
        this.WriteRtv(this.SurfaceDistance,SurfaceDistanceFormat,RtvSurfaceNormal+1);
        D3D12_DEPTH_STENCIL_VIEW_DESC Dsv=new(){ Format=DXGI_FORMAT_D32_FLOAT,ViewDimension=D3D12_DSV_DIMENSION.D3D12_DSV_DIMENSION_TEXTURE2D };
        this.Device.Device->CreateDepthStencilView(this.SurfaceDepth,&Dsv,this.SurfaceDsv);
        this.WriteTextureSrv(this.SurfaceNormal,SurfaceNormalFormat,SrvSurfaceNormal);
        this.WriteTextureSrv(this.SurfaceDistance,SurfaceDistanceFormat,SrvSurfaceDistance);
        this.WriteTextureSrv(this.PreviousSurfaceNormal,SurfaceNormalFormat,SrvPreviousSurfaceNormal);
        this.WriteTextureSrv(this.PreviousSurfaceDistance,SurfaceDistanceFormat,SrvPreviousSurfaceDistance);
        this.WriteTextureSrv(this.SceneColorHistory,RadianceFormat,SrvSceneColorHistory);
        this.WriteTextureSrv(this.ProbeRadiance,RadianceFormat,SrvProbeRadiance);
        this.WriteTextureUav(this.ProbeRadiance,RadianceFormat,UavProbeRadiance);
        this.WriteBufferViews(this.ProbeInfo,Probes*2,SrvProbeInfo,UavProbeInfo);
        this.WriteBufferViews(this.ProbeSh,Probes*9,SrvProbeSh,UavProbeSh);
        this.WriteTextureSrv(this.DiffuseHistory,IrradianceFormat,SrvDiffuseHistory);
        this.WriteTextureUav(this.Diffuse,IrradianceFormat,UavDiffuse);
        this.WriteTextureSrv(this.ReflectionHistory,RadianceFormat,SrvReflectionHistory);
        this.WriteTextureSrv(this.ReflectionTrace,RadianceFormat,SrvReflectionTrace);
        this.WriteTextureUav(this.ReflectionTrace,RadianceFormat,UavReflectionTrace);
        this.WriteTextureUav(this.Reflection,RadianceFormat,UavReflection);
        this.InvalidateHistory();
        this.Generation++;
    }
    /// <summary>Releases the screen-sized targets; the caller guarantees the GPU no longer uses them (Resize waits first).</summary>
    internal void ReleaseScreen(){
        ReleaseResource(ref this.SurfaceNormal);
        ReleaseResource(ref this.SurfaceDistance);
        ReleaseResource(ref this.PreviousSurfaceNormal);
        ReleaseResource(ref this.PreviousSurfaceDistance);
        ReleaseResource(ref this.SurfaceDepth);
        ReleaseResource(ref this.SceneColorHistory);
        ReleaseResource(ref this.ProbeRadiance);
        ReleaseResource(ref this.ProbeInfo);
        ReleaseResource(ref this.ProbeSh);
        ReleaseResource(ref this.Diffuse);
        ReleaseResource(ref this.DiffuseHistory);
        ReleaseResource(ref this.ReflectionTrace);
        ReleaseResource(ref this.Reflection);
        ReleaseResource(ref this.ReflectionHistory);
        this.ScreenKey=null;
        this.InvalidateHistory();
    }
    private void ReleaseTierResources(){
        ReleaseResource(ref this.CardAlbedo);
        ReleaseResource(ref this.CardNormal);
        ReleaseResource(ref this.CardEmissive);
        ReleaseResource(ref this.CardDepth);
        ReleaseResource(ref this.CardRadiance);
        ReleaseResource(ref this.CacheRadiance);
        ReleaseResource(ref this.CacheIrradiance);
        this.Atlas=null;
        this.Tier=null;
    }
    private static void ReleaseResource(ref ID3D12Resource*Resource){
        if(Resource!=null){
            _=Resource->Release();
            Resource=null;
        }
    }
    private ID3D12RootSignature*CreateRootSignature(){
        D3D12_DESCRIPTOR_RANGE SrvRange=new(){ RangeType=D3D12_DESCRIPTOR_RANGE_TYPE_SRV,NumDescriptors=SrvCount,BaseShaderRegister=13 };
        D3D12_DESCRIPTOR_RANGE UavRange=new(){ RangeType=D3D12_DESCRIPTOR_RANGE_TYPE_UAV,NumDescriptors=DescriptorCount-SrvCount,BaseShaderRegister=0 };
        D3D12_ROOT_PARAMETER*Parameters=stackalloc D3D12_ROOT_PARAMETER[17];
        for(var Index=0;Index<2;Index++){
            Parameters[Index]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_CBV,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
            Parameters[Index].Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=(uint)Index };
        }
        for(var Index=0;Index<13;Index++){
            Parameters[2+Index]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_SRV,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
            Parameters[2+Index].Descriptor=new D3D12_ROOT_DESCRIPTOR { ShaderRegister=(uint)Index };
        }
        Parameters[15]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
        Parameters[15].DescriptorTable=new D3D12_ROOT_DESCRIPTOR_TABLE { NumDescriptorRanges=1,pDescriptorRanges=&SrvRange };
        Parameters[16]=new D3D12_ROOT_PARAMETER { ParameterType=D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE,ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL };
        Parameters[16].DescriptorTable=new D3D12_ROOT_DESCRIPTOR_TABLE { NumDescriptorRanges=1,pDescriptorRanges=&UavRange };
        D3D12_STATIC_SAMPLER_DESC*Samplers=stackalloc D3D12_STATIC_SAMPLER_DESC[4];
        Samplers[0]=new(){
            Filter=D3D12_FILTER.D3D12_FILTER_MIN_MAG_MIP_LINEAR,
            AddressU=D3D12_TEXTURE_ADDRESS_MODE.D3D12_TEXTURE_ADDRESS_MODE_CLAMP,
            AddressV=D3D12_TEXTURE_ADDRESS_MODE.D3D12_TEXTURE_ADDRESS_MODE_CLAMP,
            AddressW=D3D12_TEXTURE_ADDRESS_MODE.D3D12_TEXTURE_ADDRESS_MODE_CLAMP,
            ComparisonFunc=D3D12_COMPARISON_FUNC.D3D12_COMPARISON_FUNC_NEVER,
            MaxLOD=float.MaxValue,
            ShaderRegister=0,
            ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL,
        };
        Samplers[1]=Samplers[0];
        Samplers[1].AddressU=D3D12_TEXTURE_ADDRESS_MODE.D3D12_TEXTURE_ADDRESS_MODE_WRAP;
        Samplers[1].AddressV=D3D12_TEXTURE_ADDRESS_MODE.D3D12_TEXTURE_ADDRESS_MODE_WRAP;
        Samplers[1].AddressW=D3D12_TEXTURE_ADDRESS_MODE.D3D12_TEXTURE_ADDRESS_MODE_WRAP;
        Samplers[1].ShaderRegister=1;
        Samplers[2]=new(){
            Filter=D3D12_FILTER.D3D12_FILTER_COMPARISON_MIN_MAG_LINEAR_MIP_POINT,
            AddressU=D3D12_TEXTURE_ADDRESS_MODE.D3D12_TEXTURE_ADDRESS_MODE_BORDER,
            AddressV=D3D12_TEXTURE_ADDRESS_MODE.D3D12_TEXTURE_ADDRESS_MODE_BORDER,
            AddressW=D3D12_TEXTURE_ADDRESS_MODE.D3D12_TEXTURE_ADDRESS_MODE_BORDER,
            ComparisonFunc=D3D12_COMPARISON_FUNC.D3D12_COMPARISON_FUNC_LESS_EQUAL,
            BorderColor=D3D12_STATIC_BORDER_COLOR.D3D12_STATIC_BORDER_COLOR_OPAQUE_WHITE,
            MaxLOD=float.MaxValue,
            ShaderRegister=2,
            ShaderVisibility=D3D12_SHADER_VISIBILITY_ALL,
        };
        Samplers[3]=Samplers[0];
        Samplers[3].Filter=D3D12_FILTER.D3D12_FILTER_MIN_MAG_MIP_POINT;
        Samplers[3].ShaderRegister=3;
        D3D12_ROOT_SIGNATURE_DESC Desc=new(){ NumParameters=17,pParameters=Parameters,NumStaticSamplers=4,pStaticSamplers=Samplers };
        ID3DBlob*Signature=null;
        ID3DBlob*Error=null;
        try {
            D3D12Diagnostics.EnsureSuccess(D3D12SerializeRootSignature(&Desc,D3D_ROOT_SIGNATURE_VERSION.D3D_ROOT_SIGNATURE_VERSION_1,&Signature,&Error),
                "D3D12SerializeRootSignature","serializing the Lumen root signature");
            ID3D12RootSignature*Result;
            D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateRootSignature(0,Signature->GetBufferPointer(),Signature->GetBufferSize(),__uuidof<ID3D12RootSignature>(),(void**)&Result),
                "ID3D12Device::CreateRootSignature","creating the Lumen root signature");
            return Result;
        } finally {
            if(Signature!=null){ _=Signature->Release(); }
            if(Error!=null){ _=Error->Release(); }
        }
    }
    private ID3D12DescriptorHeap*CreateHeap(D3D12_DESCRIPTOR_HEAP_TYPE Type,uint Count,D3D12_DESCRIPTOR_HEAP_FLAGS Flags){
        D3D12_DESCRIPTOR_HEAP_DESC Desc=new(){ Type=Type,NumDescriptors=Count,Flags=Flags };
        ID3D12DescriptorHeap*Heap;
        D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateDescriptorHeap(&Desc,__uuidof<ID3D12DescriptorHeap>(),(void**)&Heap),
            "ID3D12Device::CreateDescriptorHeap","creating a Lumen descriptor heap");
        return Heap;
    }
    private ID3D12Resource*CreateTexture(DXGI_FORMAT Format,int Width,int Height,D3D12_RESOURCE_FLAGS Flags,D3D12_CLEAR_VALUE*Clear,D3D12_RESOURCE_STATES State=ShaderResourceState){
        var Desc=D3D12_RESOURCE_DESC.Tex2D(Format,(ulong)Math.Max(Width,1),(uint)Math.Max(Height,1),1,1);
        Desc.Flags|=Flags;
        D3D12_HEAP_PROPERTIES Heap=new(D3D12_HEAP_TYPE_DEFAULT);
        ID3D12Resource*Resource;
        D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateCommittedResource(&Heap,D3D12_HEAP_FLAG_NONE,&Desc,State,Clear,__uuidof<ID3D12Resource>(),(void**)&Resource),
            "ID3D12Device::CreateCommittedResource","creating a Lumen texture");
        return Resource;
    }
    private ID3D12Resource*CreateBuffer(ulong Size){
        var Desc=D3D12_RESOURCE_DESC.Buffer(Math.Max(Size,16UL),D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS);
        D3D12_HEAP_PROPERTIES Heap=new(D3D12_HEAP_TYPE_DEFAULT);
        ID3D12Resource*Resource;
        D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateCommittedResource(&Heap,D3D12_HEAP_FLAG_NONE,&Desc,ShaderResourceState,null,__uuidof<ID3D12Resource>(),(void**)&Resource),
            "ID3D12Device::CreateCommittedResource","creating a Lumen buffer");
        return Resource;
    }
    private ID3D12Resource*CreateUploadBuffer(ulong Size,out byte*Mapped){
        D3D12_HEAP_PROPERTIES Heap=new(D3D12_HEAP_TYPE_UPLOAD);
        var Desc=D3D12_RESOURCE_DESC.Buffer(Math.Max(Size,16UL));
        ID3D12Resource*Resource;
        D3D12Diagnostics.EnsureSuccess(this.Device.Device->CreateCommittedResource(&Heap,D3D12_HEAP_FLAG_NONE,&Desc,D3D12_RESOURCE_STATE_GENERIC_READ,null,__uuidof<ID3D12Resource>(),(void**)&Resource),
            "ID3D12Device::CreateCommittedResource","creating a Lumen upload buffer");
        D3D12_RANGE NoRead=new();
        byte*Pointer;
        D3D12Diagnostics.EnsureSuccess(Resource->Map(0,&NoRead,(void**)&Pointer),"ID3D12Resource::Map","mapping a Lumen upload buffer");
        Mapped=Pointer;
        return Resource;
    }
    private void WriteRtv(ID3D12Resource*Resource,DXGI_FORMAT Format,int Slot){
        D3D12_RENDER_TARGET_VIEW_DESC Rtv=new(){ Format=Format,ViewDimension=D3D12_RTV_DIMENSION.D3D12_RTV_DIMENSION_TEXTURE2D };
        this.Device.Device->CreateRenderTargetView(Resource,&Rtv,new D3D12_CPU_DESCRIPTOR_HANDLE(this.RtvHeap->GetCPUDescriptorHandleForHeapStart(),Slot,this.RtvSize));
    }
    private void WriteDsv(ID3D12Resource*Resource,int Slot){
        D3D12_DEPTH_STENCIL_VIEW_DESC Dsv=new(){ Format=DXGI_FORMAT_D32_FLOAT,ViewDimension=D3D12_DSV_DIMENSION.D3D12_DSV_DIMENSION_TEXTURE2D };
        this.Device.Device->CreateDepthStencilView(Resource,&Dsv,new D3D12_CPU_DESCRIPTOR_HANDLE(this.DsvHeap->GetCPUDescriptorHandleForHeapStart(),Slot,this.DsvSize));
    }
    private void WriteTextureSrv(ID3D12Resource*Resource,DXGI_FORMAT Format,int Slot){
        D3D12_SHADER_RESOURCE_VIEW_DESC Srv=new(){ Format=Format,ViewDimension=D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_TEXTURE2D,Shader4ComponentMapping=(uint)TerraFX.Interop.DirectX.D3D12.D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING };
        Srv.Texture2D.MipLevels=1;
        this.Device.Device->CreateShaderResourceView(Resource,&Srv,this.CpuHandle(Slot));
    }
    private void WriteTextureUav(ID3D12Resource*Resource,DXGI_FORMAT Format,int Slot){
        D3D12_UNORDERED_ACCESS_VIEW_DESC Uav=new(){ Format=Format,ViewDimension=D3D12_UAV_DIMENSION.D3D12_UAV_DIMENSION_TEXTURE2D };
        this.Device.Device->CreateUnorderedAccessView(Resource,null,&Uav,this.CpuHandle(Slot));
    }
    private void WriteBufferViews(ID3D12Resource*Resource,int Elements,int SrvSlot,int UavSlot){
        D3D12_SHADER_RESOURCE_VIEW_DESC Srv=new(){ Format=DXGI_FORMAT_UNKNOWN,ViewDimension=D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_BUFFER,Shader4ComponentMapping=(uint)TerraFX.Interop.DirectX.D3D12.D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING };
        Srv.Buffer=new D3D12_BUFFER_SRV { FirstElement=0,NumElements=(uint)Elements,StructureByteStride=16 };
        this.Device.Device->CreateShaderResourceView(Resource,&Srv,this.CpuHandle(SrvSlot));
        D3D12_UNORDERED_ACCESS_VIEW_DESC Uav=new(){ Format=DXGI_FORMAT_UNKNOWN,ViewDimension=D3D12_UAV_DIMENSION.D3D12_UAV_DIMENSION_BUFFER };
        Uav.Buffer=new D3D12_BUFFER_UAV { FirstElement=0,NumElements=(uint)Elements,StructureByteStride=16 };
        this.Device.Device->CreateUnorderedAccessView(Resource,null,&Uav,this.CpuHandle(UavSlot));
    }
    private D3D12_CPU_DESCRIPTOR_HANDLE CpuHandle(int Slot)=>new(this.DescriptorHeap->GetCPUDescriptorHandleForHeapStart(),Slot,this.DescriptorSize);
    // ---------------------------------------------------------------------------------------------------------------------------------------
    // Test-only reads (the caller guarantees the queue is idle).
    internal (Vector4[]Texels,int Width,int Height)ReadDiffuseForTesting(){
        if(this.Diffuse==null){
            throw new InvalidOperationException("The Lumen-like lighting has not run yet.");
        }
        var (Data,RowPitch,Width,Height)=GpuTextureReadback.Read(this.Device,this.Diffuse,ShaderResourceState);
        var Result=new Vector4[Width*Height];
        fixed(byte*Bytes=Data){
            for(var Y=0;Y<Height;Y++){
                new ReadOnlySpan<Vector4>(Bytes+Y*RowPitch,Width).CopyTo(Result.AsSpan(Y*Width,Width));
            }
        }
        return (Result,Width,Height);
    }
    internal (float[]Distances,int Width,int Height)ReadSurfaceDistancesForTesting(){
        if(this.SurfaceDistance==null){
            throw new InvalidOperationException("The Lumen-like surface prepass has not run yet.");
        }
        var (Data,RowPitch,Width,Height)=GpuTextureReadback.Read(this.Device,this.SurfaceDistance,ShaderResourceState);
        var Distances=new float[Width*Height];
        fixed(byte*Bytes=Data){
            for(var Y=0;Y<Height;Y++){
                new ReadOnlySpan<float>(Bytes+Y*RowPitch,Width).CopyTo(Distances.AsSpan(Y*Width,Width));
            }
        }
        return (Distances,Width,Height);
    }
    internal (Vector4[]Texels,int Width,int Height)ReadReflectionForTesting()=>this.ReadHalfTexture(this.Reflection);
    /// <summary>The screen probes' traced radiance: probe (x, y) holds an 8 x 8 octahedral tile at (8x, 8y).</summary>
    internal (Vector4[]Texels,int Width,int Height)ReadProbeRadianceForTesting()=>this.ReadHalfTexture(this.ProbeRadiance);
    internal (int X,int Y,int Texels)ProbeLayoutForTesting=>(this.ProbeCountX,this.ProbeCountY,this.Plan.Tier.ProbeTexels);
    /// <summary>The surface cache's radiance of one renderer object's card: the average over its covered texels and how many texels are covered.</summary>
    internal (Vector3 Average,int Covered)ReadCardForTesting(int ObjectIndex,int Face){
        if((uint)ObjectIndex>=(uint)this.Objects.Length||!this.Objects[ObjectIndex].Tracked||!this.Objects[ObjectIndex].Faces[Face].Allocated){
            return (Vector3.Zero,0);
        }
        var Card=this.Objects[ObjectIndex].Faces[Face];
        var (Radiance,Width,_)=this.ReadHalfTexture(this.CardRadiance);
        var Total=Vector3.Zero;
        var Covered=0;
        for(var Y=Card.Y;Y<Card.Y+Card.Height;Y++){
            for(var X=Card.X;X<Card.X+Card.Width;X++){
                var Texel=Radiance[Y*Width+X];
                if(Texel.W>0.5f){
                    Total+=new Vector3(Texel.X,Texel.Y,Texel.Z);
                    Covered++;
                }
            }
        }
        return (Covered>0?Total/Covered:Vector3.Zero,Covered);
    }
    private (Vector4[]Texels,int Width,int Height)ReadHalfTexture(ID3D12Resource*Texture){
        if(Texture==null){
            throw new InvalidOperationException("The Lumen-like lighting has not run yet.");
        }
        var (Data,RowPitch,Width,Height)=GpuTextureReadback.Read(this.Device,Texture,ShaderResourceState);
        var Result=new Vector4[Width*Height];
        fixed(byte*Bytes=Data){
            for(var Y=0;Y<Height;Y++){
                var Row=(Half*)(Bytes+Y*RowPitch);
                for(var X=0;X<Width;X++){
                    Result[Y*Width+X]=new Vector4((float)Row[X*4],(float)Row[X*4+1],(float)Row[X*4+2],(float)Row[X*4+3]);
                }
            }
        }
        return (Result,Width,Height);
    }
    public void Dispose(){
        if(this.Disposed){
            return;
        }
        this.Disposed=true;
        this.ReleaseScreen();
        this.ReleaseTierResources();
        this.ObjectRing.Release();
        this.CardRing.Release();
        this.CellRing.Release();
        this.CellObjectRing.Release();
        this.WorkRing.Release();
        this.ValidRing.Release();
        if(this.ConstantsBuffer!=null){
            this.ConstantsBuffer->Unmap(0,null);
            ReleaseResource(ref this.ConstantsBuffer);
        }
        if(this.CaptureConstants!=null){
            this.CaptureConstants->Unmap(0,null);
            ReleaseResource(ref this.CaptureConstants);
        }
        ReleaseResource(ref this.DummyBuffer);
        ReleaseResource(ref this.HistoryExposure);
        if(this.PipelineState!=null){ _=this.PipelineState->Release(); }
        if(this.RootSignature!=null){ _=this.RootSignature->Release(); }
        if(this.DescriptorHeap!=null){ _=this.DescriptorHeap->Release(); }
        if(this.RtvHeap!=null){ _=this.RtvHeap->Release(); }
        if(this.DsvHeap!=null){ _=this.DsvHeap->Release(); }
    }
    internal readonly record struct Capture(int ObjectIndex,int Face,Matrix4x4 Projection,int X,int Y,int Width,int Height);
    private struct CardAllocation {
        public bool Allocated;
        public bool Failed;
        public int X;
        public int Y;
        public int Width;
        public int Height;
    }
    private struct ObjectState {
        public bool Tracked;
        public int Mesh;
        public int Material;
        public int MeshRecord;
        public int RecordIndex;
        public CardAllocation[]Faces;
        public byte Captured;
        public bool RelightAll;
        public float EmissiveScale;
        public Vector3 BoxMinimum;
        public Vector3 BoxMaximum;
    }
    private readonly record struct TierKey(int AtlasTexels,int CacheLevels,int CacheResolution);
    private readonly record struct FramePlan(LumenTier Tier,LumenFrameInput Input,int CardTiles,int CacheProbes,Vector2 GridOrigin,Matrix4x4 PreviousViewProjection,
        Vector3 PreviousCamera,bool HistoryValid,bool SceneColorValid,Vector4[]CacheOrigins,uint[]CacheSlots,Vector4[]CacheScales,int CacheLevels);
    /// <summary>A mapped upload buffer with one region per frame in flight; it grows (after the GPU is idle) when a frame needs more.</summary>
    private struct UploadRing {
        private ID3D12Resource*Buffer;
        private byte*Mapped;
        private int BytesPerFrame;
        internal void Ensure(StageLumen Owner,int Bytes,Action WaitForGpuIdle){
            if(this.Buffer!=null&&Bytes<=this.BytesPerFrame){
                return;
            }
            if(this.Buffer!=null){
                WaitForGpuIdle();
                this.Release();
            }
            this.BytesPerFrame=Math.Max(4096,(int)BitOperations.RoundUpToPowerOf2((uint)Math.Max(Bytes,1)));
            this.Buffer=Owner.CreateUploadBuffer((ulong)this.BytesPerFrame*Owner.FrameCount,out this.Mapped);
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
/// <summary>What one frame of the Lumen-like lighting is given (ADR-0078/M0111).</summary>
internal readonly record struct LumenFrameInput(
    LumenSettings Settings,
    RenderScalability Scalability,
    GlobalDistanceFieldSettings Field,
    (Vector4 Scale,Vector4 Origin,Vector4 Texel)[]Clipmaps,
    int ClipmapLevels,
    Matrix4x4 ViewProjection,
    Matrix4x4 InverseViewProjection,
    Vector3 CameraPosition,
    int Width,
    int Height,
    uint FrameNumber,
    bool AutoExposure,
    float ManualExposure);
/// <summary>The buffers and textures of the renderer the compute passes read.</summary>
internal readonly unsafe struct LumenComputeBindings {
    internal ulong FrameConstants { get;init; }
    internal ulong LocalLights { get;init; }
    internal ulong SpotShadowProjections { get;init; }
    internal ulong PointShadowProjections { get;init; }
    internal ulong SkyIrradianceSh { get;init; }
    internal ulong ExposureState { get;init; }
    internal ID3D12Resource*ExposureStateResource { get;init; }
    internal ID3D12Resource*ShadowAtlas { get;init; }
    internal ID3D12Resource*SkyMap { get;init; }
    internal StageGlobalDistanceField Field { get;init; }
}
/// <summary>The sizes and per-frame budgets of a Lumen tier (see <see cref="StageLumen.ResolveTier"/>).</summary>
internal readonly record struct LumenTier(
    int AtlasTexels,
    int MaximumCardTexels,
    float TexelsPerMeter,
    int CardTexelsPerFrame,
    int CapturesPerFrame,
    int CacheLevels,
    int CacheResolution,
    float CacheSpacingMeters,
    int CacheProbesPerFrame,
    int ProbeTexels,
    bool Reflections,
    int ReflectionDownsample);
/// <summary>What the last frame of the Lumen-like lighting did (diagnostics and tests).</summary>
internal record struct LumenStatistics {
    public int Objects;
    public int Cards;
    public int Captures;
    public int PendingCaptures;
    public int LitCards;
    public int LitTexels;
    public int FreshProbes;
    public int TracedProbes;
    public int FreeAtlasPages;
}
