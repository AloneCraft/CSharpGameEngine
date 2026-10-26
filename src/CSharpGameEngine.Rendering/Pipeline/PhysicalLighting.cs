using System.Collections.Generic;
using System.Linq;
using System.Numerics;
namespace CSharpGameEngine.Rendering.Pipeline;
/// <summary>Conversions between the public photometric light units and shader-friendly values.</summary>
public static class PhysicalLightUnits {
    public const float Pi=MathF.PI;
    /// <summary>Converts directional illuminance in lux to RGB irradiance.</summary>
    public static Vector3 DirectionalIrradiance(Vector3 LinearRgb,float IlluminanceLux){
        ValidateFiniteNonNegative(IlluminanceLux,nameof(IlluminanceLux));
        return LinearRgb*IlluminanceLux;
    }
    /// <summary>Converts an omnidirectional luminous flux in lumens to candela.</summary>
    public static float PointLumensToCandela(float LuminousFluxLumens){
        ValidateFiniteNonNegative(LuminousFluxLumens,nameof(LuminousFluxLumens));
        return LuminousFluxLumens/(4f*Pi);
    }
    /// <summary>
    /// Converts a hard-edged cone's total luminous flux in lumens to its uniform in-cone intensity
    /// in candela. This overload is retained for callers without a separate inner angle.
    /// </summary>
    public static float SpotLumensToCandela(float LuminousFluxLumens,float OuterFullAngleDegrees){
        return SpotLumensToCandela(LuminousFluxLumens,OuterFullAngleDegrees,OuterFullAngleDegrees);
    }
    /// <summary>
    /// Converts luminous flux to candela for the renderer's squared smooth cone falloff. Its
    /// effective solid angle integrates the exact HLSL kernel: one inside the inner cone and
    /// <c>((μ-μouter)/(μinner-μouter))²</c> in the transition ring, where μ=cos(half angle).
    /// </summary>
    public static float SpotLumensToCandela(
        float LuminousFluxLumens,float InnerFullAngleDegrees,float OuterFullAngleDegrees){
        ValidateFiniteNonNegative(LuminousFluxLumens,nameof(LuminousFluxLumens));
        return LuminousFluxLumens/ComputeSpotEffectiveSolidAngle(
            InnerFullAngleDegrees,OuterFullAngleDegrees);
    }
    /// <summary>Converts a public full cone angle in degrees to the renderer's half angle in radians.</summary>
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
    /// Integrates the squared smooth falloff used by <c>Stage shader ABI</c>. This is smaller than the
    /// outer hard-cone solid angle when inner and outer angles differ, so using it preserves the
    /// public luminous flux after the shader's transition attenuation.
    /// </summary>
    public static float ComputeSpotEffectiveSolidAngle(
        float InnerFullAngleDegrees,float OuterFullAngleDegrees){
        ValidateSpotAngles(InnerFullAngleDegrees,OuterFullAngleDegrees);
        float InnerCos=MathF.Cos(FullConeAngleDegreesToHalfAngleRadians(InnerFullAngleDegrees));
        float OuterCos=MathF.Cos(FullConeAngleDegreesToHalfAngleRadians(OuterFullAngleDegrees));
        return 2f*Pi*((1f-InnerCos)+((InnerCos-OuterCos)/3f));
    }
    /// <summary>Luminous intensity along the normal of a uniform Lambertian rectangle.</summary>
    public static float AreaNitsToNormalCandela(float LuminanceNits,float WidthMeters,float HeightMeters){
        ValidateFiniteNonNegative(LuminanceNits,nameof(LuminanceNits));
        ValidateFinitePositive(WidthMeters,nameof(WidthMeters));
        ValidateFinitePositive(HeightMeters,nameof(HeightMeters));
        return LuminanceNits*WidthMeters*HeightMeters;
    }
    /// <summary>
    /// Splits a Lambertian rectangle's one-sided flux across uniform hemisphere samples.
    /// A rectangle emits π × L × area lumens; one uniform hemispherical sample emits
    /// 2π × candela, so each sample receives L × area / (2 × sample count) candela.
    /// </summary>
    public static float AreaNitsToUniformHemisphereSampleCandela(
        float LuminanceNits,float WidthMeters,float HeightMeters,int SampleCount){
        if(SampleCount<=0){
            throw new ArgumentOutOfRangeException(nameof(SampleCount),"The sample count must be positive.");
        }
        return AreaNitsToNormalCandela(LuminanceNits,WidthMeters,HeightMeters)/(2f*SampleCount);
    }
    /// <summary>Converts scene EV100 to a linear pre-ACES exposure multiplier.</summary>
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
/// <summary>Light source category used by deterministic renderer budgeting.</summary>
public enum PhysicalLightKind {
    Directional,
    Point,
    Spot,
    Area,
    Emissive,
}
/// <summary>
/// Renderer-neutral light submission metadata. <see cref="StableSubmissionOrder"/> is part of the
/// rendering contract: ties never depend on hash-table or ECS iteration order.
/// </summary>
public readonly record struct PhysicalLightCandidate(
    int StableSubmissionOrder,
    PhysicalLightKind Kind,
    int ShadowPriority,
    float ScreenContribution,
    bool CastsShadows);
/// <summary>Visible-light selection counters exposed to diagnostics and editor overlays.</summary>
public readonly record struct PhysicalLightBudgetDiagnostics(
    int SubmittedDirectional,
    int SubmittedLocal,
    int DroppedDirectional,
    int DroppedLocal);
/// <summary>Deterministic selection result for the global and local Forward+ light lists.</summary>
public sealed class PhysicalLightBudgetSelection(
        PhysicalLightCandidate[]Directional,
        PhysicalLightCandidate[]Local,
        PhysicalLightBudgetDiagnostics Diagnostics) {
    public IReadOnlyList<PhysicalLightCandidate>Directional { get; }=Directional;
    public IReadOnlyList<PhysicalLightCandidate>Local { get; }=Local;
    public PhysicalLightBudgetDiagnostics Diagnostics { get; }=Diagnostics;
}
/// <summary>Deterministic per-tile local-light list with the configured 64-light cap.</summary>
public sealed class PhysicalLightTileSelection(PhysicalLightCandidate[]Lights,int DroppedCount) {
    public IReadOnlyList<PhysicalLightCandidate>Lights { get; }=Lights;
    public int DroppedCount { get; }=DroppedCount;
}
/// <summary>
/// CPU-side selection contract for the StructuredBuffer/Forward+ uploader. This class performs no
/// GPU work; it makes overflow visible and deterministic before the renderer writes its buffers.
/// </summary>
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
    /// <summary>
    /// Chooses the only directional light that may use the legacy single shadow map. Priority is
    /// descending; screen contribution and then stable submission order break ties. A value of
    /// <c>-1</c> means no submitted directional light casts shadows.
    /// </summary>
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
    /// Direct-light overflow selection is intentionally independent from shadow priority. Direct
    /// lighting keeps the largest screen contribution, then the stable submission order; shadow
    /// priority is reserved for <see cref="PhysicalShadowAtlasPlan"/> allocation.
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
/// One physical 1024x1024 tile in the fixed 4096x4096 atlas. Tile indices are row-major:
/// left-to-right, then top-to-bottom. The explicit pixel and UV rectangle lets a future GPU
/// uploader use this CPU plan without reinterpreting its allocation order.
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
/// <summary>
/// One contiguous allocation in the 4096x4096 shadow atlas. A point light owns six consecutive
/// tiles in cubemap-face order; all other current light kinds own one tile.
/// </summary>
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
/// <summary>
/// Shadow-caster overflow counters. A dropped caster remains in the direct-light submission;
/// only its shadow lookup is disabled for the current frame.
/// </summary>
public readonly record struct ShadowAtlasDiagnostics(
    int SubmittedCasters,
    int SelectedCasters,
    int DroppedCasters,
    int AllocatedTiles,
    int ReservedTiles);
/// <summary>
/// Deterministic physical-light shadow budget for the fixed 4K atlas. Selection affects only
/// shadow-map allocation. <see cref="SubmittedLights"/> always retains every input candidate,
/// and <see cref="ShadowCastersWithoutAtlasTiles"/> identifies casters that continue direct
/// illumination but cannot sample a shadow this frame.
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
    /// <summary>All direct-light submissions, including non-casters and atlas-overflow casters.</summary>
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
