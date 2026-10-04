using System.Numerics;
namespace CSharpGameEngine.Unreal.SceneImporter;
/// <summary>
/// Fits the ADR-0069 GlobalIlluminationVolume to an imported level. Unreal lights the template with
/// Lumen, which has no authored volume, so the importer derives one: the horizontal extent of every
/// level mesh, and vertically from just above the dominant walkable surface (the up-facing height
/// with the most triangle area) to the level top, capped at <see cref="MaxHeightMeters"/>. The probe
/// spacing starts at <see cref="PreferredSpacingMeters"/> and grows until the grid has at most
/// <see cref="ProbeBudget"/> probes (half the atlas), which keeps a full round-robin refresh short.
/// </summary>
internal static class GlobalIlluminationVolumeFitter {
    internal const float PreferredSpacingMeters=2.5f;
    internal const float FloorClearanceMeters=0.3f;
    internal const float MaxHeightMeters=8f;
    internal const int ProbeBudget=2048;
    internal readonly record struct Volume(Vector3 Center,Vector3 HalfExtent,float SpacingMeters);
    internal static Volume Fit(IEnumerable<(Matrix4x4 World,IReadOnlyList<Vector3>Positions,IReadOnlyList<uint>Indices)>Meshes){
        var Min=new Vector3(float.MaxValue);
        var Max=new Vector3(float.MinValue);
        var UpAreaByHeight=new Dictionary<int,float>();
        foreach(var (World,Positions,Indices) in Meshes){
            var Transformed=Positions.Select(Position=>Vector3.Transform(Position,World)).ToArray();
            foreach(var Position in Transformed){
                Min=Vector3.Min(Min,Position);
                Max=Vector3.Max(Max,Position);
            }
            for(var Index=0;Index+2<Indices.Count;Index+=3){
                var A=Transformed[Indices[Index]];
                var B=Transformed[Indices[Index+1]];
                var C=Transformed[Indices[Index+2]];
                var Cross=Vector3.Cross(B-A,C-A);
                var Area=Cross.Length()*0.5f;
                if(Area<=0f||MathF.Abs(Cross.Y)/(2f*Area)<0.99f){
                    continue;
                }
                var Bucket=(int)MathF.Round((A.Y+B.Y+C.Y)/3f*10f);
                UpAreaByHeight[Bucket]=UpAreaByHeight.GetValueOrDefault(Bucket)+Area;
            }
        }
        if(UpAreaByHeight.Count==0){
            throw new InvalidOperationException("The level has no horizontal surface to fit a global illumination volume to.");
        }
        var FloorHeight=UpAreaByHeight.MaxBy(Pair=>Pair.Value).Key/10f;
        var Bottom=FloorHeight+FloorClearanceMeters;
        var Top=MathF.Min(Max.Y,FloorHeight+MaxHeightMeters);
        if(Top<=Bottom+1f){
            Top=Bottom+1f;
        }
        var Low=new Vector3(Min.X,Bottom,Min.Z);
        var High=new Vector3(Max.X,Top,Max.Z);
        var HalfExtent=(High-Low)*0.5f;
        var Spacing=PreferredSpacingMeters;
        while(ProbeCount(HalfExtent,Spacing)>ProbeBudget){
            Spacing*=1.1f;
        }
        return new Volume((Low+High)*0.5f,HalfExtent,Spacing);
    }
    /// <summary>Same grid rule as the renderer's GlobalIlluminationSettings: floor(2h/s)+1 per axis, at least two.</summary>
    internal static long ProbeCount(Vector3 HalfExtent,float Spacing){
        static long Axis(float Half,float Spacing)=>Math.Max(2L,(long)MathF.Floor(2f*Half/Spacing)+1L);
        return Axis(HalfExtent.X,Spacing)*Axis(HalfExtent.Y,Spacing)*Axis(HalfExtent.Z,Spacing);
    }
}
