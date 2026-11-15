using System.Numerics;
namespace CSharpGameEngine.Rendering.Pipeline;
/// <summary>局所光の種類。値は GPU の局所光のレコードへそのまま書くので、並びを変えない。</summary>
public enum StageLocalLightType {
    Point,
    Spot,
    Area,
}
/// <summary>方向光の提出。直接照明は影の優先度と別に順位付けする。<see cref="DirectionTowardLight"/> は面から光源へ向かう向き。</summary>
public readonly struct StageDirectionalLight(
    Vector3 DirectionTowardLight,Vector3 ColorTimesIntensity,bool CastsShadows,
    int ShadowPriority,float ShadowBiasMeters,int StableSubmissionOrder) {
    public Vector3 DirectionTowardLight { get; }=DirectionTowardLight;
    public Vector3 ColorTimesIntensity { get; }=ColorTimesIntensity;
    public bool CastsShadows { get; }=CastsShadows;
    public int ShadowPriority { get; }=ShadowPriority;
    public float ShadowBiasMeters { get; }=ShadowBiasMeters;
    public int StableSubmissionOrder { get; }=StableSubmissionOrder;
}
/// <summary>局所光（点・スポット・面）の提出。フレームのカメラが決まってから一度だけ順位を決める。</summary>
public readonly struct StageLocalLight(
    StageLocalLightType Type,Vector3 Position,float Range,Vector3 Direction,float InnerConeCos,
    Vector3 ColorTimesIntensity,float OuterConeCos,int SelectionPriority,int StableSubmissionOrder,
    bool CastsShadows,float ShadowBiasMeters,
    Vector3 Right,Vector3 Up,float HalfWidth,float HalfHeight,PhysicalLightKind ShadowKind) {
    public StageLocalLight(
        StageLocalLightType Type,Vector3 Position,float Range,Vector3 Direction,float InnerConeCos,
        Vector3 ColorTimesIntensity,float OuterConeCos,int SelectionPriority,int StableSubmissionOrder,
        bool CastsShadows,float ShadowBiasMeters)
        :this(Type,Position,Range,Direction,InnerConeCos,ColorTimesIntensity,OuterConeCos,
            SelectionPriority,StableSubmissionOrder,CastsShadows,ShadowBiasMeters,
            Vector3.Zero,Vector3.Zero,0f,0f,
            Type==StageLocalLightType.Point?PhysicalLightKind.Point:PhysicalLightKind.Spot) {
        if(Type==StageLocalLightType.Area)throw new ArgumentOutOfRangeException(nameof(Type));
    }
    public StageLocalLightType Type { get; }=Type;
    public Vector3 Position { get; }=Position;
    public float Range { get; }=Range;
    public Vector3 Direction { get; }=Direction;
    public float InnerConeCos { get; }=InnerConeCos;
    public Vector3 ColorTimesIntensity { get; }=ColorTimesIntensity;
    public float OuterConeCos { get; }=OuterConeCos;
    public int SelectionPriority { get; }=SelectionPriority;
    public int StableSubmissionOrder { get; }=StableSubmissionOrder;
    public bool CastsShadows { get; }=CastsShadows;
    public float ShadowBiasMeters { get; }=ShadowBiasMeters;
    public Vector3 Right { get; }=Right;
    public Vector3 Up { get; }=Up;
    public float HalfWidth { get; }=HalfWidth;
    public float HalfHeight { get; }=HalfHeight;
    public PhysicalLightKind ShadowKind { get; }=ShadowKind;
}
/// <summary>局所光の影のアトラスへの割り当て 1 つ。点光源は 6 面の投影を <see cref="StageLightSubmissions.PointFaceViewProjections"/> に持つ。</summary>
/// <param name="Kind">点・スポット・面のどれか。</param>
/// <param name="LocalIndex">選ばれた局所光の並びでの位置。</param>
/// <param name="Allocation">アトラスのタイル。</param>
/// <param name="ProjectionIndex">点光源は投影の先頭、スポットと面は投影の番号。</param>
/// <param name="ViewProjection">スポットと面の投影。点光源では使わない。</param>
public readonly record struct StageLocalShadowAllocation(StageLocalLightType Kind,int LocalIndex,ShadowAtlasAllocation Allocation,int ProjectionIndex,Matrix4x4 ViewProjection);
/// <summary>
/// Stage の光源の提出と、そのフレームごとの持続の規則と計画（ADR-0087 §1-2）。
/// <list type="bullet">
/// <item>方向光の単一設定（<see cref="SetDirectionalLight"/>）は置き換えられるまで毎フレーム有効。</item>
/// <item>方向光・局所光の追加はそのフレームだけ有効で、<see cref="EndFrame"/> で消える。</item>
/// <item>光源が提出されるまでは照らさない。</item>
/// </list>
/// M0120 単位 5b で L0 の <c>StageRenderer</c> から、提出の検証、持続の規則、方向光・局所光・影のアトラスの計画を、式と順を変えずに移した。
/// L0 は計画の結果を GPU へ書くだけ。
/// </summary>
public sealed class StageLightSubmissions {
    public const int MaxDirectionalLights=PhysicalLightBudget.MaxDirectionalLights;
    public const int MaxLocalLights=PhysicalLightBudget.MaxLocalLights;
    /// <summary>アトラスの計画が選んだ点光源 1 つに使う 90 度の 6 面。</summary>
    public const int MaxPointShadowProjections=PhysicalShadowAtlasPlan.PointLightBudget*PhysicalShadowAtlasPlan.PointTilesPerLight;
    /// <summary>アトラスの計画が選ぶスポットライトの最大数。</summary>
    public const int MaxSpotShadowProjections=PhysicalShadowAtlasPlan.SpotTileBudget;
    /// <summary>アトラスの計画が選ぶ面光源の最大数。</summary>
    public const int MaxAreaAndEmissiveShadowProjections=PhysicalShadowAtlasPlan.AreaAndEmissiveTileBudget;
    private bool PersistAcrossFrames;
    private bool ExplicitThisFrame;
    private StageDirectionalLight?Persistent;
    private readonly List<StageDirectionalLight>Directionals=[];
    private readonly List<StageLocalLight>Locals=[];
    private readonly List<PhysicalLightCandidate>DirectionalShadowCandidates=[];
    private readonly List<StageDirectionalLight>PlannedDirectionalsStorage=[];
    private readonly List<StageLocalLight>SelectedLocalsStorage=[];
    private readonly List<StageTileRange?>SelectedLocalTilesStorage=[];
    private readonly List<StageLocalShadowAllocation>ShadowAllocationsStorage=[];
    private readonly Matrix4x4[]PointFaceViewProjectionsStorage=new Matrix4x4[MaxPointShadowProjections];
    /// <summary>このフレームで使う方向光の数。単一設定のときはフレームをまたいで残る。</summary>
    public int DirectionalLightCount { get;private set; }
    /// <summary>カスケード影を落とす方向光の、使う方向光の並びでの位置。無ければ -1。</summary>
    public int DirectionalShadowIndex { get;private set; }=-1;
    /// <summary>影のアトラスで方向光の分の 1 タイルを決める候補。</summary>
    public PhysicalLightCandidate?DirectionalShadowAtlasCandidate { get;private set; }
    /// <summary>直前の <see cref="PlanDirectional"/> が方向光を決め直したか。false なら L0 は前の書き込みを残す（光源が提出されていない）。</summary>
    public bool DirectionalsPlanned { get;private set; }
    /// <summary>使う順の方向光。</summary>
    public IReadOnlyList<StageDirectionalLight>PlannedDirectionals=>this.PlannedDirectionalsStorage;
    /// <summary>大気の太陽にする方向光（使う方向光の先頭）。</summary>
    public StageDirectionalLight?Sun { get;private set; }
    /// <summary>カスケード影を作る方向光の向き（面から光源へ）。カスケード影が無ければ null。</summary>
    public Vector3?CascadeDirectionTowardLight { get;private set; }
    /// <summary>このフレームで使う局所光の数。</summary>
    public int LocalLightCount { get;private set; }
    /// <summary>使う順の局所光。</summary>
    public IReadOnlyList<StageLocalLight>SelectedLocals=>this.SelectedLocalsStorage;
    /// <summary>使う局所光ごとの Forward+ のタイル範囲。画面の外なら null。</summary>
    public IReadOnlyList<StageTileRange?>SelectedLocalTiles=>this.SelectedLocalTilesStorage;
    /// <summary>局所光の影のアトラスへの割り当て。</summary>
    public IReadOnlyList<StageLocalShadowAllocation>ShadowAllocations=>this.ShadowAllocationsStorage;
    /// <summary>点光源の 6 面の投影（<see cref="PointShadowProjectionCount"/> 個が有効）。</summary>
    public ReadOnlySpan<Matrix4x4>PointFaceViewProjections=>this.PointFaceViewProjectionsStorage;
    public int PointShadowProjectionCount { get;private set; }
    public int SpotShadowProjectionCount { get;private set; }
    public int AreaShadowProjectionCount { get;private set; }
    /// <summary>4 灯の上限を超えて落とした方向光の数（このフレーム）。</summary>
    public int DroppedDirectionalLightsThisFrame { get;private set; }
    public int DroppedDirectionalLightsLastFrame { get;private set; }
    /// <summary>1024 灯の上限を超えて落とした局所光の数（このフレーム）。</summary>
    public int DroppedLocalLightsThisFrame { get;private set; }
    public int DroppedLocalLightsLastFrame { get;private set; }
    /// <summary>
    /// 単一の方向光を設定する。そのフレームの方向光の追加を、この 1 灯に置き換え、置き換えられるまで毎フレーム有効。
    /// <paramref name="DirectionTowardLight"/> は面から光源へ向かう向き。
    /// </summary>
    public void SetDirectionalLight(Vector3 DirectionTowardLight,Vector3 Color,float Intensity,bool CastsShadows=true,float ShadowBiasMeters=0.0015f,int ShadowPriority=0){
        if(!float.IsFinite(ShadowBiasMeters)||ShadowBiasMeters<0f){
            throw new ArgumentOutOfRangeException(nameof(ShadowBiasMeters));
        }
        this.BeginDirectionalSubmission();
        var Submission=new StageDirectionalLight(DirectionTowardLight,Color*Intensity,CastsShadows,ShadowPriority,ShadowBiasMeters,StableSubmissionOrder:0);
        // 同じフレームで後から方向光を追加しても、この設定を黙って置き換えないよう、追加の並びにも入れる。
        this.Directionals.Add(Submission);
        this.Persistent=Submission;
        this.PersistAcrossFrames=true;
    }
    /// <summary>このフレームに方向光を 1 つ追加する。直接照明の上限を超えたら、画面への寄与の大きい 4 灯を残し、同じなら提出順。</summary>
    public void AddDirectionalLightTowardLight(Vector3 DirectionTowardLight,Vector3 Color,float Intensity,bool CastsShadows=true,int ShadowPriority=0,float ShadowBiasMeters=0.0015f){
        if(!float.IsFinite(ShadowBiasMeters)||ShadowBiasMeters<0f){
            throw new ArgumentOutOfRangeException(nameof(ShadowBiasMeters));
        }
        if(!this.ExplicitThisFrame){
            this.BeginDirectionalSubmission();
        }
        this.PersistAcrossFrames=false;
        this.Directionals.Add(new StageDirectionalLight(DirectionTowardLight,Color*Intensity,CastsShadows,ShadowPriority,ShadowBiasMeters,this.Directionals.Count));
    }
    private void BeginDirectionalSubmission(){
        this.ExplicitThisFrame=true;
        this.PersistAcrossFrames=false;
        this.DirectionalLightCount=0;
        this.DirectionalShadowIndex=-1;
        this.DirectionalShadowAtlasCandidate=null;
        this.DirectionalShadowCandidates.Clear();
        this.Persistent=null;
        this.Directionals.Clear();
    }
    /// <summary>このフレームに点光源を 1 つ追加する。</summary>
    public void AddPointLight(Vector3 Position,Vector3 Color,float Intensity,float Range,int SelectionPriority=0,bool CastsShadows=false,float ShadowBiasMeters=0f){
        if(!float.IsFinite(ShadowBiasMeters)||ShadowBiasMeters<0f){
            throw new ArgumentOutOfRangeException(nameof(ShadowBiasMeters));
        }
        this.Locals.Add(new StageLocalLight(StageLocalLightType.Point,Position,Range,Vector3.Zero,0f,Color*Intensity,0f,
            SelectionPriority,this.Locals.Count,CastsShadows,ShadowBiasMeters));
    }
    /// <summary>このフレームにスポットライトを 1 つ追加する。<paramref name="Direction"/> は正規化していなくてよい。角度は半角（ラジアン）。</summary>
    public void AddSpotLight(Vector3 Position,Vector3 Direction,Vector3 Color,float Intensity,float Range,float InnerConeAngleRadians,float OuterConeAngleRadians,int SelectionPriority=0,bool CastsShadows=false,float ShadowBiasMeters=0f){
        if(!float.IsFinite(ShadowBiasMeters)||ShadowBiasMeters<0f){
            throw new ArgumentOutOfRangeException(nameof(ShadowBiasMeters));
        }
        this.Locals.Add(new StageLocalLight(StageLocalLightType.Spot,Position,Range,Vector3.Normalize(Direction),MathF.Cos(InnerConeAngleRadians),
            Color*Intensity,MathF.Cos(OuterConeAngleRadians),SelectionPriority,this.Locals.Count,CastsShadows,ShadowBiasMeters));
    }
    /// <summary>このフレームに片面の矩形の光源（面光源か発光面）を 1 つ追加する。基底は右手系の正規直交でなければならない。</summary>
    public void AddAreaLight(
        Vector3 Position,Vector3 Direction,Vector3 Right,Vector3 Up,Vector3 LinearRgb,
        float LuminanceNits,float WidthMeters,float HeightMeters,float RangeMeters,
        int SelectionPriority,bool CastsShadows,float ShadowBiasMeters,PhysicalLightKind ShadowKind){
        if(ShadowKind!=PhysicalLightKind.Area&&ShadowKind!=PhysicalLightKind.Emissive){
            throw new ArgumentOutOfRangeException(nameof(ShadowKind));
        }
        if(!IsFinite(Position)||!IsFinite(LinearRgb)
            ||!float.IsFinite(LuminanceNits)||LuminanceNits<0f
            ||!float.IsFinite(WidthMeters)||WidthMeters<=0f
            ||!float.IsFinite(HeightMeters)||HeightMeters<=0f
            ||!float.IsFinite(RangeMeters)||RangeMeters<=0f
            ||!float.IsFinite(ShadowBiasMeters)||ShadowBiasMeters<0f
            ||!TryNormalize(Direction,out Vector3 UnitDirection)
            ||!TryNormalize(Right,out Vector3 UnitRight)
            ||!TryNormalize(Up,out Vector3 UnitUp)
            ||MathF.Abs(Vector3.Dot(UnitDirection,UnitRight))>1e-3f
            ||MathF.Abs(Vector3.Dot(UnitDirection,UnitUp))>1e-3f
            ||MathF.Abs(Vector3.Dot(UnitRight,UnitUp))>1e-3f
            ||Vector3.Dot(Vector3.Cross(UnitRight,UnitUp),UnitDirection)<0.999f){
            throw new ArgumentException("Area lights require finite positive dimensions/range and a right-handed orthonormal basis.");
        }
        this.Locals.Add(new StageLocalLight(StageLocalLightType.Area,Position,RangeMeters,UnitDirection,0f,LinearRgb*LuminanceNits,0f,
            SelectionPriority,this.Locals.Count,CastsShadows,ShadowBiasMeters,UnitRight,UnitUp,WidthMeters*0.5f,HeightMeters*0.5f,ShadowKind));
    }
    /// <summary>
    /// 方向光を決める。単一設定ならその 1 灯、追加があれば寄与の順に 4 灯まで。影の優先度は選んだ直接光の中でだけ使う。
    /// 光源が提出されていなければ何も決め直さない（<see cref="DirectionalsPlanned"/> が false）。
    /// </summary>
    public void PlanDirectional(){
        this.DirectionalsPlanned=false;
        this.Sun=null;
        this.CascadeDirectionTowardLight=null;
        this.PlannedDirectionalsStorage.Clear();
        if(this.PersistAcrossFrames){
            this.DirectionalsPlanned=true;
            this.DirectionalShadowIndex=-1;
            this.DirectionalShadowAtlasCandidate=null;
            this.DirectionalShadowCandidates.Clear();
            if(this.Persistent is not StageDirectionalLight Submission){
                this.DirectionalLightCount=0;
                return;
            }
            this.DirectionalLightCount=1;
            this.Sun=Submission;
            this.PlannedDirectionalsStorage.Add(Submission);
            if(Submission.CastsShadows){
                var Candidate=new PhysicalLightCandidate(Submission.StableSubmissionOrder,PhysicalLightKind.Directional,
                    Submission.ShadowPriority,StageLightPlanner.DirectionalScreenContribution(Submission.ColorTimesIntensity),CastsShadows:true);
                this.DirectionalShadowCandidates.Add(Candidate);
                this.DirectionalShadowIndex=0;
                this.DirectionalShadowAtlasCandidate=Candidate;
                this.CascadeDirectionTowardLight=Submission.DirectionTowardLight;
            }
            return;
        }
        if(!this.ExplicitThisFrame){
            return;
        }
        this.DirectionalsPlanned=true;
        var Ranked=new StageLightRank[this.Directionals.Count];
        for(var Position=0;Position<Ranked.Length;Position++){
            var Submitted=this.Directionals[Position];
            Ranked[Position]=new StageLightRank(Position,0,StageLightPlanner.DirectionalScreenContribution(Submitted.ColorTimesIntensity),Submitted.StableSubmissionOrder);
        }
        StageLightPlanner.SortDirectional(Ranked);
        this.DirectionalLightCount=Math.Min(Ranked.Length,MaxDirectionalLights);
        this.DroppedDirectionalLightsThisFrame=Ranked.Length-this.DirectionalLightCount;
        this.DirectionalShadowIndex=-1;
        this.DirectionalShadowAtlasCandidate=null;
        this.DirectionalShadowCandidates.Clear();
        if(this.DirectionalLightCount>0){
            this.Sun=this.Directionals[Ranked[0].Index];
        }
        for(int Index=0;Index<this.DirectionalLightCount;Index++){
            var Submission=this.Directionals[Ranked[Index].Index];
            this.PlannedDirectionalsStorage.Add(Submission);
            this.DirectionalShadowCandidates.Add(new PhysicalLightCandidate(Submission.StableSubmissionOrder,PhysicalLightKind.Directional,
                Submission.ShadowPriority,Ranked[Index].ScreenContribution,Submission.CastsShadows));
        }
        int SelectedStableOrder=PhysicalLightBudget.SelectDirectionalShadowIndex(this.DirectionalShadowCandidates);
        if(SelectedStableOrder>=0){
            for(int Index=0;Index<this.DirectionalShadowCandidates.Count;Index++){
                if(this.DirectionalShadowCandidates[Index].StableSubmissionOrder!=SelectedStableOrder){
                    continue;
                }
                this.DirectionalShadowIndex=Index;
                this.DirectionalShadowAtlasCandidate=this.DirectionalShadowCandidates[Index];
                this.CascadeDirectionTowardLight=this.Directionals[Ranked[Index].Index].DirectionTowardLight;
                break;
            }
        }
    }
    /// <summary>局所光を、優先度 → 画面上の寄与 → 提出順で 1024 灯まで選び、それぞれの Forward+ のタイル範囲を決める。影の投影の数は 0 に戻す。</summary>
    public void PlanLocal(in Matrix4x4 ViewProjection,int Width,int Height,int TileCountX,int TileCountY,int TileSizePixels){
        this.SelectedLocalsStorage.Clear();
        this.SelectedLocalTilesStorage.Clear();
        this.SpotShadowProjectionCount=0;
        this.PointShadowProjectionCount=0;
        this.AreaShadowProjectionCount=0;
        var Ranked=new StageLightRank[this.Locals.Count];
        for(var Position=0;Position<Ranked.Length;Position++){
            var Submitted=this.Locals[Position];
            Ranked[Position]=new StageLightRank(Position,Submitted.SelectionPriority,
                StageLightPlanner.LocalScreenContribution(Submitted.ColorTimesIntensity,Submitted.Position,Submitted.Range,ViewProjection),Submitted.StableSubmissionOrder);
        }
        StageLightPlanner.SortLocal(Ranked);
        this.LocalLightCount=Math.Min(Ranked.Length,MaxLocalLights);
        this.DroppedLocalLightsThisFrame=Ranked.Length-this.LocalLightCount;
        for(int LocalIndex=0;LocalIndex<this.LocalLightCount;LocalIndex++){
            var Submission=this.Locals[Ranked[LocalIndex].Index];
            this.SelectedLocalsStorage.Add(Submission);
            this.SelectedLocalTilesStorage.Add(StageLightPlanner.TryGetTileRange(Submission.Position,Submission.Range,ViewProjection,Width,Height,TileCountX,TileCountY,TileSizePixels,out var Tiles)?Tiles:null);
        }
    }
    /// <summary>
    /// 局所光の影のアトラスを割り当てる。唯一の割り当て元は <see cref="PhysicalShadowAtlasPlan"/>。点光源には計画の 6 面、スポットと面光源には中心の投影 1 タイル。
    /// カスケード影を落とす方向光があれば、提出順によらずアトラスに方向光の 1 タイルを置く。
    /// </summary>
    public void PlanLocalShadows(in Matrix4x4 ViewProjection){
        this.ShadowAllocationsStorage.Clear();
        this.PointShadowProjectionCount=0;
        this.SpotShadowProjectionCount=0;
        this.AreaShadowProjectionCount=0;
        List<PhysicalLightCandidate>Casters=[];
        Dictionary<int,int>LocalIndexByStableOrder=[];
        if(this.DirectionalShadowIndex>=0){
            // 方向光の元の提出順に関係なく、アトラスには方向光のタイルが 1 つある。最初の 4 灯を超えて選ばれた提出が、
            // 下の局所光の番号と重ならないよう、番号を 0 にそろえる。
            PhysicalLightCandidate Directional=this.DirectionalShadowAtlasCandidate
                ??new PhysicalLightCandidate(0,PhysicalLightKind.Directional,0,0f,CastsShadows:true);
            Casters.Add(new PhysicalLightCandidate(0,PhysicalLightKind.Directional,Directional.ShadowPriority,Directional.ScreenContribution,CastsShadows:true));
        }
        for(int LocalIndex=0;LocalIndex<this.SelectedLocalsStorage.Count;LocalIndex++){
            var Submission=this.SelectedLocalsStorage[LocalIndex];
            bool IsValidCaster=Submission.Type switch {
                StageLocalLightType.Point=>StageShadowPlanner.IsValidPointCaster(Submission.Position,Submission.Range),
                StageLocalLightType.Spot=>TryBuildSpotViewProjection(Submission,out _),
                StageLocalLightType.Area=>TryBuildAreaViewProjection(Submission,out _),
                _=>false,
            };
            if(!Submission.CastsShadows||!IsValidCaster){
                continue;
            }
            int StableOrder=MaxDirectionalLights+Submission.StableSubmissionOrder;
            Casters.Add(new PhysicalLightCandidate(StableOrder,Submission.ShadowKind,Submission.SelectionPriority,
                StageLightPlanner.LocalScreenContribution(Submission.ColorTimesIntensity,Submission.Position,Submission.Range,ViewProjection),CastsShadows:true));
            LocalIndexByStableOrder.Add(StableOrder,LocalIndex);
        }
        if(Casters.Count==0){
            return;
        }
        PhysicalShadowAtlasPlan Plan=PhysicalShadowAtlasPlan.Select(Casters);
        foreach(ShadowAtlasAllocation Allocation in Plan.Allocations){
            if(!LocalIndexByStableOrder.TryGetValue(Allocation.Candidate.StableSubmissionOrder,out int LocalIndex)){
                continue;
            }
            var Submission=this.SelectedLocalsStorage[LocalIndex];
            if(Allocation.Candidate.Kind==PhysicalLightKind.Point){
                if(this.PointShadowProjectionCount!=0||Allocation.TileCount!=MaxPointShadowProjections){
                    continue;
                }
                if(!StageShadowPlanner.TryBuildPointFaceViewProjections(Submission.Position,Submission.Range,this.PointFaceViewProjectionsStorage)){
                    continue;
                }
                this.ShadowAllocationsStorage.Add(new StageLocalShadowAllocation(StageLocalLightType.Point,LocalIndex,Allocation,this.PointShadowProjectionCount,Matrix4x4.Identity));
                this.PointShadowProjectionCount+=MaxPointShadowProjections;
                continue;
            }
            if(Allocation.Candidate.Kind==PhysicalLightKind.Spot){
                if(this.SpotShadowProjectionCount>=MaxSpotShadowProjections||!TryBuildSpotViewProjection(Submission,out Matrix4x4 SpotViewProjection)){
                    continue;
                }
                this.ShadowAllocationsStorage.Add(new StageLocalShadowAllocation(StageLocalLightType.Spot,LocalIndex,Allocation,this.SpotShadowProjectionCount,SpotViewProjection));
                this.SpotShadowProjectionCount++;
                continue;
            }
            if((Allocation.Candidate.Kind!=PhysicalLightKind.Area&&Allocation.Candidate.Kind!=PhysicalLightKind.Emissive)
                ||this.AreaShadowProjectionCount>=MaxAreaAndEmissiveShadowProjections
                ||!TryBuildAreaViewProjection(Submission,out Matrix4x4 AreaViewProjection)){
                continue;
            }
            if(Allocation.TileCount!=1){
                continue;
            }
            this.ShadowAllocationsStorage.Add(new StageLocalShadowAllocation(StageLocalLightType.Area,LocalIndex,Allocation,this.AreaShadowProjectionCount,AreaViewProjection));
            this.AreaShadowProjectionCount++;
        }
    }
    /// <summary>
    /// フレームを閉じる。追加した方向光と局所光、このフレームの計画を消し、落とした数を前フレームの値へ移す。
    /// 単一設定の方向光と、その計画（数・影の番号・候補）は残す。
    /// </summary>
    public void EndFrame(){
        if(!this.PersistAcrossFrames){
            this.DirectionalLightCount=0;
            this.DirectionalShadowIndex=-1;
            this.DirectionalShadowAtlasCandidate=null;
        }
        this.ExplicitThisFrame=false;
        this.DirectionalShadowCandidates.Clear();
        this.Directionals.Clear();
        this.Locals.Clear();
        this.SelectedLocalsStorage.Clear();
        this.SelectedLocalTilesStorage.Clear();
        this.ShadowAllocationsStorage.Clear();
        this.LocalLightCount=0;
        this.PointShadowProjectionCount=0;
        this.SpotShadowProjectionCount=0;
        this.AreaShadowProjectionCount=0;
        this.DroppedDirectionalLightsLastFrame=this.DroppedDirectionalLightsThisFrame;
        this.DroppedDirectionalLightsThisFrame=0;
        this.DroppedLocalLightsLastFrame=this.DroppedLocalLightsThisFrame;
        this.DroppedLocalLightsThisFrame=0;
    }
    private static bool TryBuildSpotViewProjection(in StageLocalLight Submission,out Matrix4x4 ShadowViewProjection)=>
        StageShadowPlanner.TryBuildSpotViewProjection(Submission.Position,Submission.Direction,Submission.Range,Submission.OuterConeCos,out ShadowViewProjection);
    private static bool TryBuildAreaViewProjection(in StageLocalLight Submission,out Matrix4x4 ShadowViewProjection){
        if(Submission.Type!=StageLocalLightType.Area){
            ShadowViewProjection=Matrix4x4.Identity;
            return false;
        }
        return StageShadowPlanner.TryBuildAreaViewProjection(Submission.Position,Submission.Direction,Submission.Up,Submission.Range,Submission.HalfWidth,Submission.HalfHeight,out ShadowViewProjection);
    }
    private static bool IsFinite(Vector3 Value)=>float.IsFinite(Value.X)&&float.IsFinite(Value.Y)&&float.IsFinite(Value.Z);
    private static bool TryNormalize(Vector3 Value,out Vector3 Normalized){
        Normalized=Vector3.Zero;
        if(!IsFinite(Value)){
            return false;
        }
        float LengthSquared=Value.LengthSquared();
        if(!float.IsFinite(LengthSquared)||LengthSquared<=1e-12f){
            return false;
        }
        Normalized=Value/MathF.Sqrt(LengthSquared);
        return IsFinite(Normalized);
    }
}
