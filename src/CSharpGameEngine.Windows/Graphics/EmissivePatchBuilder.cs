using System.Numerics;

namespace CSharpGameEngine.Graphics.D3D12;

/// <summary>
/// A deterministic rectangular approximation of emissive triangles. <see cref="Forward"/> is the
/// one emitting side; its back face has zero direct-light contribution.
/// </summary>
public readonly record struct EmissivePatch(
    Vector3 Center,
    Vector3 Forward,
    Vector3 Right,
    Vector3 Up,
    Vector3 LinearRgb,
    float LuminanceNits,
    float WidthMeters,
    float HeightMeters,
    int SourceTriangleCount);

/// <summary>Geometry and material state used to rebuild emissive patches after an object moves.</summary>
public sealed class EmissivePatchBuildInput {
    public required IReadOnlyList<Vector3> Positions { get; init; }
    public required IReadOnlyList<uint> Indices { get; init; }
    public required Matrix4x4 ObjectWorld { get; init; }
    public required Vector3 EmissiveNits { get; init; }

    /// <summary>
    /// Mean linear luminance sampled from the emissive texture. A texture decoder may refine this
    /// value later; one preserves the factor/strength-only glTF material result.
    /// </summary>
    public float EmissiveTextureLuminanceScale { get; init; }=1f;

    public int MaxPatches { get; init; }=16;
}

/// <summary>
/// Converts emissive triangle geometry to no more than 16 deterministic rectangle patches per
/// object. Triangles are ordered by normal, position, area, and original index before clustering,
/// so the same imported mesh always produces the same result.
/// </summary>
public static class EmissivePatchBuilder {
    private const float MinimumAreaSquareMeters=1e-8f;

    public static IReadOnlyList<EmissivePatch> Build(EmissivePatchBuildInput Input){
        ArgumentNullException.ThrowIfNull(Input);
        Validate(Input);
        Vector3 EmissiveNits=Input.EmissiveNits*Input.EmissiveTextureLuminanceScale;
        if(EmissiveNits.X<=0f&&EmissiveNits.Y<=0f&&EmissiveNits.Z<=0f){
            return [];
        }

        List<EmissiveTriangle>Triangles=[];
        for(var Index=0;Index<Input.Indices.Count;Index+=3){
            Vector3 A=Vector3.Transform(Input.Positions[(int)Input.Indices[Index]],Input.ObjectWorld);
            Vector3 B=Vector3.Transform(Input.Positions[(int)Input.Indices[Index+1]],Input.ObjectWorld);
            Vector3 C=Vector3.Transform(Input.Positions[(int)Input.Indices[Index+2]],Input.ObjectWorld);
            Vector3 Cross=Vector3.Cross(B-A,C-A);
            float DoubleArea=Cross.Length();
            if(DoubleArea<=MinimumAreaSquareMeters*2f){
                continue;
            }

            Triangles.Add(new EmissiveTriangle(
                Index/3,A,B,C,Vector3.Normalize(Cross),DoubleArea*0.5f));
        }
        if(Triangles.Count==0){
            return [];
        }

        Triangles.Sort(EmissiveTriangleComparer.Instance);
        int PatchCount=Math.Min(Input.MaxPatches,Triangles.Count);
        float PeakNits=MathF.Max(EmissiveNits.X,MathF.Max(EmissiveNits.Y,EmissiveNits.Z));
        Vector3 LinearRgb=EmissiveNits/PeakNits;
        List<EmissivePatch>Patches=new(PatchCount);
        for(var PatchIndex=0;PatchIndex<PatchCount;PatchIndex++){
            int Start=(PatchIndex*Triangles.Count)/PatchCount;
            int End=((PatchIndex+1)*Triangles.Count)/PatchCount;
            Patches.Add(BuildPatch(Triangles,Start,End,LinearRgb,PeakNits));
        }
        return Patches;
    }

    /// <summary>Submits generated patches through the true one-sided rectangle Area-light path.</summary>
    public static void SubmitDirectLighting(
        this StageRenderer Renderer,
        IReadOnlyList<EmissivePatch> Patches,
        float RangeMeters,
        bool CastsShadows,
        int ShadowPriority,
        float ShadowBiasMeters){
        ArgumentNullException.ThrowIfNull(Renderer);
        ArgumentNullException.ThrowIfNull(Patches);
        foreach(EmissivePatch Patch in Patches){
            Renderer.AddAreaLight(
                Patch.Center,Patch.Forward,Patch.Right,Patch.Up,Patch.LinearRgb,Patch.LuminanceNits,
                Patch.WidthMeters,Patch.HeightMeters,RangeMeters,
                ShadowPriority,CastsShadows,ShadowBiasMeters,PhysicalLightKind.Emissive);
        }
    }

    private static EmissivePatch BuildPatch(
        IReadOnlyList<EmissiveTriangle> Triangles,
        int Start,
        int End,
        Vector3 LinearRgb,
        float LuminanceNits){
        float TotalArea=0f;
        Vector3 NormalSum=Vector3.Zero;
        Vector3 CenterSum=Vector3.Zero;
        for(var Index=Start;Index<End;Index++){
            EmissiveTriangle Triangle=Triangles[Index];
            TotalArea+=Triangle.Area;
            NormalSum+=Triangle.Forward*Triangle.Area;
            CenterSum+=((Triangle.A+Triangle.B+Triangle.C)/3f)*Triangle.Area;
        }
        Vector3 Forward=NormalSum.LengthSquared()>MinimumAreaSquareMeters
            ?Vector3.Normalize(NormalSum)
            :Triangles[Start].Forward;
        Vector3 Center=CenterSum/TotalArea;
        Vector3 Reference=MathF.Abs(Vector3.Dot(Forward,Vector3.UnitY))>0.99f
            ?Vector3.UnitX
            :Vector3.UnitY;
        Vector3 Right=Vector3.Normalize(Vector3.Cross(Reference,Forward));
        Vector3 Up=Vector3.Normalize(Vector3.Cross(Forward,Right));
        float MinimumRight=float.PositiveInfinity;
        float MaximumRight=float.NegativeInfinity;
        float MinimumUp=float.PositiveInfinity;
        float MaximumUp=float.NegativeInfinity;
        for(var Index=Start;Index<End;Index++){
            EmissiveTriangle Triangle=Triangles[Index];
            ExpandBounds(Triangle.A); ExpandBounds(Triangle.B); ExpandBounds(Triangle.C);
        }
        float CenterRight=(MinimumRight+MaximumRight)*0.5f;
        float CenterUp=(MinimumUp+MaximumUp)*0.5f;
        Center+=(Right*CenterRight)+(Up*CenterUp);
        return new EmissivePatch(
            Center,Forward,Right,Up,LinearRgb,LuminanceNits,
            MathF.Max(MaximumRight-MinimumRight,0.001f),
            MathF.Max(MaximumUp-MinimumUp,0.001f),
            End-Start);

        void ExpandBounds(Vector3 Position){
            Vector3 Relative=Position-Center;
            float RightCoordinate=Vector3.Dot(Relative,Right);
            float UpCoordinate=Vector3.Dot(Relative,Up);
            MinimumRight=MathF.Min(MinimumRight,RightCoordinate);
            MaximumRight=MathF.Max(MaximumRight,RightCoordinate);
            MinimumUp=MathF.Min(MinimumUp,UpCoordinate);
            MaximumUp=MathF.Max(MaximumUp,UpCoordinate);
        }
    }

    private static void Validate(EmissivePatchBuildInput Input){
        if(Input.MaxPatches<1||Input.MaxPatches>16){
            throw new ArgumentOutOfRangeException(nameof(Input.MaxPatches),
                "An object may produce between one and sixteen emissive patches.");
        }
        if(Input.Indices.Count%3!=0){
            throw new ArgumentException("Emissive patch geometry must be a triangle list.",nameof(Input));
        }
        if(!float.IsFinite(Input.EmissiveTextureLuminanceScale)
            ||Input.EmissiveTextureLuminanceScale<0f){
            throw new ArgumentOutOfRangeException(nameof(Input.EmissiveTextureLuminanceScale));
        }
        if(!IsFiniteNonNegative(Input.EmissiveNits)){
            throw new ArgumentOutOfRangeException(nameof(Input.EmissiveNits));
        }
        for(var Index=0;Index<Input.Indices.Count;Index++){
            if(Input.Indices[Index]>=Input.Positions.Count){
                throw new ArgumentOutOfRangeException(nameof(Input.Indices));
            }
        }
    }

    private static bool IsFiniteNonNegative(Vector3 Value)=>
        float.IsFinite(Value.X)&&float.IsFinite(Value.Y)&&float.IsFinite(Value.Z)
        &&Value.X>=0f&&Value.Y>=0f&&Value.Z>=0f;

    private readonly record struct EmissiveTriangle(
        int OriginalIndex,
        Vector3 A,
        Vector3 B,
        Vector3 C,
        Vector3 Forward,
        float Area) {
        public Vector3 Centroid=>(this.A+this.B+this.C)/3f;
    }

    private sealed class EmissiveTriangleComparer:IComparer<EmissiveTriangle> {
        public static readonly EmissiveTriangleComparer Instance=new();

        public int Compare(EmissiveTriangle Left,EmissiveTriangle Right){
            int Result=CompareVector(Left.Forward,Right.Forward);
            if(Result!=0)return Result;
            Result=CompareVector(Left.Centroid,Right.Centroid);
            if(Result!=0)return Result;
            Result=Left.Area.CompareTo(Right.Area);
            return Result!=0?Result:Left.OriginalIndex.CompareTo(Right.OriginalIndex);
        }

        private static int CompareVector(Vector3 Left,Vector3 Right){
            int Result=Left.X.CompareTo(Right.X);
            if(Result!=0)return Result;
            Result=Left.Y.CompareTo(Right.Y);
            return Result!=0?Result:Left.Z.CompareTo(Right.Z);
        }
    }
}
