using System.Collections.Generic;
using System.Linq;
using System.Numerics;
namespace CSharpGameEngine.Rendering.Pipeline;
/// <summary>公開される測光単位と、シェーダーで扱いやすい値との変換。</summary>
public static class PhysicalLightUnits {
    public const float Pi=MathF.PI;
    /// <summary>指向性光の照度（lux）を RGB 放射照度に変換する。</summary>
    public static Vector3 DirectionalIrradiance(Vector3 LinearRgb,float IlluminanceLux){
        ValidateFiniteNonNegative(IlluminanceLux,nameof(IlluminanceLux));
        return LinearRgb*IlluminanceLux;
    }
    /// <summary>全方向の光束（ルーメン）を光度（カンデラ）に変換する。</summary>
    public static float PointLumensToCandela(float LuminousFluxLumens){
        ValidateFiniteNonNegative(LuminousFluxLumens,nameof(LuminousFluxLumens));
        return LuminousFluxLumens/(4f*Pi);
    }
    /// <summary>境界が明確な円錐の全光束（ルーメン）を、円錐内部で一様な光度（カンデラ）に変換する。内側の角度を別に指定しない呼び出し側のため、このオーバーロードを残す。</summary>
    public static float SpotLumensToCandela(float LuminousFluxLumens,float OuterFullAngleDegrees){
        return SpotLumensToCandela(LuminousFluxLumens,OuterFullAngleDegrees,OuterFullAngleDegrees);
    }
    /// <summary>
    /// レンダラーの二乗スムーズ円錐減衰に対する光束から光度への変換。有効立体角は、HLSL のカーネルを正確に積分して求める。内側の円錐では1、遷移帯では
    /// <c>((μ-μouter)/(μinner-μouter))²</c> とする。ここで μ = cos(半角)。
    /// </summary>
    public static float SpotLumensToCandela(
        float LuminousFluxLumens,float InnerFullAngleDegrees,float OuterFullAngleDegrees){
        ValidateFiniteNonNegative(LuminousFluxLumens,nameof(LuminousFluxLumens));
        return LuminousFluxLumens/ComputeSpotEffectiveSolidAngle(
            InnerFullAngleDegrees,OuterFullAngleDegrees);
    }
    /// <summary>公開 API の円錐全角度（度）を、レンダラーが使う半角（ラジアン）に変換する。</summary>
    public static float FullConeAngleDegreesToHalfAngleRadians(float FullAngleDegrees){
        if(!float.IsFinite(FullAngleDegrees)||FullAngleDegrees<=0f||FullAngleDegrees>180f){
            throw new ArgumentOutOfRangeException(nameof(FullAngleDegrees),"A spot full angle must be finite and in (0, 180] degrees.");
        }
        return FullAngleDegrees*Pi/360f;
    }
    public static float ComputeSpotSolidAngle(float OuterFullAngleDegrees){
        var HalfAngleRadians=FullConeAngleDegreesToHalfAngleRadians(OuterFullAngleDegrees);
        return 2f*Pi*(1f-MathF.Cos(HalfAngleRadians));
    }
    /// <summary>
    /// <c>Stage shader ABI</c> が使う二乗スムーズ減衰を積分する。内側と外側の角度が異なる場合、有効立体角は外側の境界が明確な円錐の立体角より小さい。その積分値を使うと、シェーダーで
    /// 遷移減衰を適用した後も公開された光束が保たれる。
    /// </summary>
    public static float ComputeSpotEffectiveSolidAngle(
        float InnerFullAngleDegrees,float OuterFullAngleDegrees){
        ValidateSpotAngles(InnerFullAngleDegrees,OuterFullAngleDegrees);
        float InnerCos=MathF.Cos(FullConeAngleDegreesToHalfAngleRadians(InnerFullAngleDegrees));
        float OuterCos=MathF.Cos(FullConeAngleDegreesToHalfAngleRadians(OuterFullAngleDegrees));
        return 2f*Pi*((1f-InnerCos)+((InnerCos-OuterCos)/3f));
    }
    /// <summary>一様なランバート反射長方形の法線方向の光度。</summary>
    public static float AreaNitsToNormalCandela(float LuminanceNits,float WidthMeters,float HeightMeters){
        ValidateFiniteNonNegative(LuminanceNits,nameof(LuminanceNits));
        ValidateFinitePositive(WidthMeters,nameof(WidthMeters));
        ValidateFinitePositive(HeightMeters,nameof(HeightMeters));
        return LuminanceNits*WidthMeters*HeightMeters;
    }
    /// <summary>
    /// ランバート反射長方形の片面光束を、一様な半球サンプルへ分配する。長方形が放射する光束は π × L × 面積ルーメン、半球からの一様な1サンプルが放射する光束は 2π × カンデラである。そのため、各
    /// サンプルには L × 面積 / (2 × サンプル数) カンデラを割り当てる。
    /// </summary>
    public static float AreaNitsToUniformHemisphereSampleCandela(
        float LuminanceNits,float WidthMeters,float HeightMeters,int SampleCount){
        if(SampleCount<=0){
            throw new ArgumentOutOfRangeException(nameof(SampleCount),"The sample count must be positive.");
        }
        return AreaNitsToNormalCandela(LuminanceNits,WidthMeters,HeightMeters)/(2f*SampleCount);
    }
    /// <summary>シーンの EV100 を、ACES 適用前の線形露出乗数に変換する。</summary>
    public static float Ev100ToExposureScale(float Ev100){
        if(!float.IsFinite(Ev100)){
            throw new ArgumentOutOfRangeException(nameof(Ev100),"EV100 must be finite.");
        }
        return MathF.Pow(2f,-Ev100);
    }
    public static void ValidateLightRange(float RangeMeters){
        ValidateFinitePositive(RangeMeters,nameof(RangeMeters));
    }
    public static void ValidateSpotAngles(float InnerFullAngleDegrees,float OuterFullAngleDegrees){
        _=ComputeSpotSolidAngle(OuterFullAngleDegrees);
        if(!float.IsFinite(InnerFullAngleDegrees)||InnerFullAngleDegrees<=0f||InnerFullAngleDegrees>OuterFullAngleDegrees){
            throw new ArgumentOutOfRangeException(nameof(InnerFullAngleDegrees),"The inner spot full angle must be finite, positive, and no greater than the outer full angle.");
        }
    }
    private static void ValidateFiniteNonNegative(float Value,string Name){
        if(!float.IsFinite(Value)||Value<0f){
            throw new ArgumentOutOfRangeException(Name,$"{Name} must be finite and non-negative.");
        }
    }
    private static void ValidateFinitePositive(float Value,string Name){
        if(!float.IsFinite(Value)||Value<=0f){
            throw new ArgumentOutOfRangeException(Name,$"{Name} must be finite and greater than zero.");
        }
    }
}
/// <summary>決定論的なレンダラー予算管理で使用する光源の分類。</summary>
public enum PhysicalLightKind {
    Directional,
    Point,
    Spot,
    Area,
    Emissive,
}
/// <summary>レンダラーに依存しないライト送信メタデータ。<see cref="StableSubmissionOrder"/> は描画契約の一部であり、同順位の順序がハッシュテーブルや ECS の反復順に左右されないようにする。</summary>
public readonly record struct PhysicalLightCandidate(
    int StableSubmissionOrder,
    PhysicalLightKind Kind,
    int ShadowPriority,
    float ScreenContribution,
    bool CastsShadows);
/// <summary>診断とエディター表示に公開する可視ライト選択カウンター。</summary>
public readonly record struct PhysicalLightBudgetDiagnostics(
    int SubmittedDirectional,
    int SubmittedLocal,
    int DroppedDirectional,
    int DroppedLocal);
/// <summary>グローバルおよびローカル Forward+ ライト一覧の決定論的な選択結果。</summary>
public sealed class PhysicalLightBudgetSelection(
        PhysicalLightCandidate[]Directional,
        PhysicalLightCandidate[]Local,
        PhysicalLightBudgetDiagnostics Diagnostics) {
    public IReadOnlyList<PhysicalLightCandidate>Directional { get; }=Directional;
    public IReadOnlyList<PhysicalLightCandidate>Local { get; }=Local;
    public PhysicalLightBudgetDiagnostics Diagnostics { get; }=Diagnostics;
}
/// <summary>設定された64ライト上限を持つ、タイルごとの決定論的なローカルライト一覧。</summary>
public sealed class PhysicalLightTileSelection(PhysicalLightCandidate[]Lights,int DroppedCount) {
    public IReadOnlyList<PhysicalLightCandidate>Lights { get; }=Lights;
    public int DroppedCount { get; }=DroppedCount;
}
/// <summary>StructuredBuffer / Forward+ へのアップロードで使う CPU 側の選択契約。このクラスは GPU 処理を行わず、レンダラーがバッファへ書き込む前に超過を検出可能かつ決定論的にする。</summary>
public static class PhysicalLightBudget {
    public const int MaxDirectionalLights=4;
    public const int MaxLocalLights=1024;
    public const int MaxLightsPerTile=64;
    public static PhysicalLightBudgetSelection Select(
        IEnumerable<PhysicalLightCandidate> Candidates){
        PhysicalLightCandidate[]All=ValidateAndMaterialize(Candidates);
        PhysicalLightCandidate[]Directional=RankDirectLighting(All.Where(static Candidate=>
            Candidate.Kind==PhysicalLightKind.Directional),MaxDirectionalLights);
        PhysicalLightCandidate[]Local=RankDirectLighting(All.Where(static Candidate=>
            Candidate.Kind!=PhysicalLightKind.Directional),MaxLocalLights);
        int SubmittedDirectional=All.Count(static Candidate=>Candidate.Kind==PhysicalLightKind.Directional);
        int SubmittedLocal=All.Length-SubmittedDirectional;
        return new PhysicalLightBudgetSelection(Directional,Local,new PhysicalLightBudgetDiagnostics(
            SubmittedDirectional,SubmittedLocal,
            SubmittedDirectional-Directional.Length,
            SubmittedLocal-Local.Length));
    }
    /// <summary>従来の単一シャドウマップを使える指向性ライトを1つ選ぶ。優先度の降順とし、同順位なら画面上の寄与、次に安定した送信順で決める。<c>-1</c> は影を落とす指向性ライトが送信されていないことを表す。</summary>
    public static int SelectDirectionalShadowIndex(
        IEnumerable<PhysicalLightCandidate> Candidates){
        PhysicalLightCandidate[]All=ValidateAndMaterialize(Candidates);
        return All
            .Where(static Candidate=>Candidate.Kind==PhysicalLightKind.Directional
                &&Candidate.CastsShadows)
            .OrderByDescending(static Candidate=>Candidate.ShadowPriority)
            .ThenByDescending(static Candidate=>Candidate.ScreenContribution)
            .ThenBy(static Candidate=>Candidate.StableSubmissionOrder)
            .Select(static Candidate=>Candidate.StableSubmissionOrder)
            .DefaultIfEmpty(-1)
            .First();
    }
    public static PhysicalLightTileSelection SelectTile(
        IEnumerable<PhysicalLightCandidate> LocalCandidates){
        PhysicalLightCandidate[]All=ValidateAndMaterialize(LocalCandidates);
        if(All.Any(static Candidate=>Candidate.Kind==PhysicalLightKind.Directional)){
            throw new ArgumentException("Directional lights are global and cannot be submitted to a Forward+ tile.",nameof(LocalCandidates));
        }
        PhysicalLightCandidate[]Selected=RankDirectLighting(All,MaxLightsPerTile);
        return new PhysicalLightTileSelection(Selected,All.Length-Selected.Length);
    }
    /// <summary>
    /// 直接光の超過分の選択は、シャドウ優先度とは意図的に独立させる。直接光では画面上の寄与が最も大きいものを残し、同順位なら安定した送信順にする。シャドウ優先度は
    /// <see cref="PhysicalShadowAtlasPlan"/> の割り当てにのみ使う。
    /// </summary>
    private static PhysicalLightCandidate[]RankDirectLighting(
        IEnumerable<PhysicalLightCandidate> Candidates,int Limit)=>
        Candidates
            .OrderByDescending(static Candidate=>Candidate.ScreenContribution)
            .ThenBy(static Candidate=>Candidate.StableSubmissionOrder)
            .Take(Limit)
            .ToArray();
    private static PhysicalLightCandidate[]ValidateAndMaterialize(
        IEnumerable<PhysicalLightCandidate> Candidates){
        ArgumentNullException.ThrowIfNull(Candidates);
        PhysicalLightCandidate[]Result=Candidates.ToArray();
        HashSet<int>Orders=[];
        foreach(PhysicalLightCandidate Candidate in Result){
            if(!float.IsFinite(Candidate.ScreenContribution)||Candidate.ScreenContribution<0f){
                throw new ArgumentOutOfRangeException(nameof(Candidates),
                    "Screen contribution must be finite and non-negative.");
            }
            if(!Orders.Add(Candidate.StableSubmissionOrder)){
                throw new ArgumentException(
                    "Stable submission orders must be unique so light selection is deterministic.",
                    nameof(Candidates));
            }
        }
        return Result;
    }
}
/// <summary>
/// 固定サイズ4096×4096アトラス内にある、実サイズ1024×1024のタイル1つ。タイル番号は行優先で、左から右、上から下の順にする。ピクセル矩形と UV 矩形を明示するため、将来の GPU
/// アップローダーは割り当て順を再解釈せず、この CPU 計画を利用できる。
/// </summary>
public readonly record struct ShadowAtlasTile(
    int Index,
    int X,
    int Y,
    int Width,
    int Height){
    public Vector4 UvScaleOffset=>new(
        (float)Width/PhysicalShadowAtlasPlan.AtlasSizePixels,
        (float)Height/PhysicalShadowAtlasPlan.AtlasSizePixels,
        (float)X/PhysicalShadowAtlasPlan.AtlasSizePixels,
        (float)Y/PhysicalShadowAtlasPlan.AtlasSizePixels);
    public static ShadowAtlasTile FromIndex(int TileIndex){
        if((uint)TileIndex>=(uint)PhysicalShadowAtlasPlan.TileCount){
            throw new ArgumentOutOfRangeException(nameof(TileIndex),
                $"A shadow-atlas tile index must be in [0, {PhysicalShadowAtlasPlan.TileCount-1}].");
        }
        int X=(TileIndex%PhysicalShadowAtlasPlan.TilesPerAxis)*PhysicalShadowAtlasPlan.TileSizePixels;
        int Y=(TileIndex/PhysicalShadowAtlasPlan.TilesPerAxis)*PhysicalShadowAtlasPlan.TileSizePixels;
        return new ShadowAtlasTile(
            TileIndex,X,Y,
            PhysicalShadowAtlasPlan.TileSizePixels,PhysicalShadowAtlasPlan.TileSizePixels);
    }
}
/// <summary>4096×4096シャドウアトラス内の連続領域1つ。ポイントライトはキューブマップの面の順で連続する6タイルを使い、他の現行ライト種別は1タイルを使う。</summary>
public readonly record struct ShadowAtlasAllocation(
    PhysicalLightCandidate Candidate,
    int FirstTile,
    int TileCount){
    public int LastTileExclusive {
        get {
            ValidateBounds();
            return FirstTile+TileCount;
        }
    }
    public ShadowAtlasTile GetTile(int ProjectionIndex){
        ValidateBounds();
        if((uint)ProjectionIndex>=(uint)TileCount){
            throw new ArgumentOutOfRangeException(nameof(ProjectionIndex),
                "The projection index must refer to a tile owned by this allocation.");
        }
        return ShadowAtlasTile.FromIndex(FirstTile+ProjectionIndex);
    }
    public IReadOnlyList<ShadowAtlasTile> Tiles {
        get {
            ValidateBounds();
            ShadowAtlasTile[]Result=new ShadowAtlasTile[TileCount];
            for(int Index=0;Index<Result.Length;Index++){
                Result[Index]=GetTile(Index);
            }
            return Result;
        }
    }
    private void ValidateBounds(){
        if(FirstTile<0||TileCount<=0||FirstTile>PhysicalShadowAtlasPlan.TileCount-TileCount){
            throw new InvalidOperationException(
                "A shadow-atlas allocation must own one or more tiles within the fixed atlas.");
        }
    }
}
/// <summary>シャドウ遮光物の上限超過カウンター。除外された遮光物も直接光の送信対象には残すが、当該フレームではシャドウ参照だけを無効にする。</summary>
public readonly record struct ShadowAtlasDiagnostics(
    int SubmittedCasters,
    int SelectedCasters,
    int DroppedCasters,
    int AllocatedTiles,
    int ReservedTiles);
/// <summary>
/// 固定サイズ4Kアトラスに対する、決定論的な物理ライトのシャドウ予算。選択結果が影響するのはシャドウマップの割り当てだけ。<see cref="SubmittedLights"/> は入力候補をすべて
/// 保持する。<see cref="ShadowCastersWithoutAtlasTiles"/> には、直接照明は続けるが当該フレームの影をサンプリングできない遮光物を列挙する。
/// </summary>
public sealed class PhysicalShadowAtlasPlan(
        PhysicalLightCandidate[]SubmittedLights,
        ShadowAtlasAllocation[]Allocations,
        PhysicalLightCandidate[]ShadowCastersWithoutAtlasTiles,
        ShadowAtlasDiagnostics Diagnostics) {
    public const int AtlasSizePixels=4096;
    public const int TileSizePixels=1024;
    public const int TilesPerAxis=AtlasSizePixels/TileSizePixels;
    public const int TileCount=TilesPerAxis*TilesPerAxis;
    public const int DirectionalTileIndex=0;
    public const int DirectionalTileBudget=1;
    public const int PointLightBudget=1;
    public const int PointTilesPerLight=6;
    public const int SpotTileBudget=4;
    public const int AreaAndEmissiveTileBudget=4;
    public const int TotalDefaultAllocatedTiles=
        DirectionalTileBudget+(PointLightBudget*PointTilesPerLight)+SpotTileBudget+AreaAndEmissiveTileBudget;
    public const int ReservedTileCount=TileCount-TotalDefaultAllocatedTiles;
    /// <summary>遮光しないライトやアトラス上限超過の遮光物も含む、すべての直接光の送信。</summary>
    public IReadOnlyList<PhysicalLightCandidate>SubmittedLights { get; }=SubmittedLights;
    public IReadOnlyList<ShadowAtlasAllocation>Allocations { get; }=Allocations;
    public IReadOnlyList<PhysicalLightCandidate>ShadowCastersWithoutAtlasTiles { get; }=ShadowCastersWithoutAtlasTiles;
    public ShadowAtlasDiagnostics Diagnostics { get; }=Diagnostics;
    public bool TryGetAllocation(int StableSubmissionOrder,out ShadowAtlasAllocation Allocation){
        foreach(ShadowAtlasAllocation Current in Allocations){
            if(Current.Candidate.StableSubmissionOrder==StableSubmissionOrder){
                Allocation=Current;
                return true;
            }
        }
        Allocation=default;
        return false;
    }
    public static PhysicalShadowAtlasPlan Select(
        IEnumerable<PhysicalLightCandidate> Candidates){
        PhysicalLightCandidate[]All=Candidates?.ToArray()
            ??throw new ArgumentNullException(nameof(Candidates));
        ValidateShadowCandidates(All);
        PhysicalLightCandidate[]Casters=All.Where(static Candidate=>Candidate.CastsShadows).ToArray();
        List<ShadowAtlasAllocation>Allocations=[];
        int NextTile=0;
        NextTile=Allocate(
            Casters.Where(static Candidate=>Candidate.Kind==PhysicalLightKind.Directional),
            DirectionalTileBudget,1,NextTile,Allocations);
        NextTile=Allocate(
            Casters.Where(static Candidate=>Candidate.Kind==PhysicalLightKind.Point),
            PointLightBudget,PointTilesPerLight,NextTile,Allocations);
        NextTile=Allocate(
            Casters.Where(static Candidate=>Candidate.Kind==PhysicalLightKind.Spot),
            SpotTileBudget,1,NextTile,Allocations);
        NextTile=Allocate(
            Casters.Where(static Candidate=>Candidate.Kind==PhysicalLightKind.Area
                ||Candidate.Kind==PhysicalLightKind.Emissive),
            AreaAndEmissiveTileBudget,1,NextTile,Allocations);
        if(NextTile>TileCount){
            throw new InvalidOperationException("The configured shadow-atlas budgets exceed its tile capacity.");
        }
        ShadowAtlasAllocation[]Selected=[.. Allocations];
        HashSet<int>SelectedOrders=Selected
            .Select(static Allocation=>Allocation.Candidate.StableSubmissionOrder)
            .ToHashSet();
        PhysicalLightCandidate[]Unallocated=Rank(Casters
            .Where(Candidate=>!SelectedOrders.Contains(Candidate.StableSubmissionOrder)))
            .ToArray();
        int AllocatedTiles=Selected.Sum(static Allocation=>Allocation.TileCount);
        return new PhysicalShadowAtlasPlan(All,Selected,Unallocated,new ShadowAtlasDiagnostics(
            Casters.Length,Selected.Length,Unallocated.Length,AllocatedTiles,TileCount-AllocatedTiles));
    }
    private static int Allocate(
        IEnumerable<PhysicalLightCandidate> Candidates,
        int LightBudget,int TilesPerLight,int NextTile,
        List<ShadowAtlasAllocation> Allocations){
        foreach(PhysicalLightCandidate Candidate in Rank(Candidates).Take(LightBudget)){
            if(NextTile+TilesPerLight>TileCount){
                throw new InvalidOperationException("A shadow allocation would exceed the atlas capacity.");
            }
            Allocations.Add(new ShadowAtlasAllocation(Candidate,NextTile,TilesPerLight));
            NextTile+=TilesPerLight;
        }
        return NextTile;
    }
    private static IOrderedEnumerable<PhysicalLightCandidate> Rank(
        IEnumerable<PhysicalLightCandidate> Candidates)=>Candidates
            .OrderByDescending(static Candidate=>Candidate.ShadowPriority)
            .ThenByDescending(static Candidate=>Candidate.ScreenContribution)
            .ThenBy(static Candidate=>Candidate.StableSubmissionOrder);
    private static void ValidateShadowCandidates(
        IReadOnlyList<PhysicalLightCandidate> Candidates){
        HashSet<int>Orders=[];
        foreach(PhysicalLightCandidate Candidate in Candidates){
            if(!float.IsFinite(Candidate.ScreenContribution)||Candidate.ScreenContribution<0f){
                throw new ArgumentOutOfRangeException(nameof(Candidates),
                    "Screen contribution must be finite and non-negative.");
            }
            if(!Orders.Add(Candidate.StableSubmissionOrder)){
                throw new ArgumentException(
                    "Stable submission orders must be unique so shadow selection is deterministic.",
                    nameof(Candidates));
            }
        }
    }
}
