using System.Numerics;
namespace CSharpGameEngine.Unreal.SceneImporter;
/// <summary>
/// インポートしたレベルに ADR-0069 の GlobalIlluminationVolume を合わせる。Unreal は作成されたボリュームを持たない Lumen でテンプレートを照らすため、インポーターがボリュームを導出する。水平方向の範囲はレベル内のすべてのメッシュ、垂直方向は主要な歩行可能面（上向きで三角形面積の合計が最も大きい高さ）のすぐ上からレベル上端までとし、<see cref="MaxHeightMeters"/> で上限を設ける。プローブ間隔は <see cref="PreferredSpacingMeters"/> から始め、グリッド数が <see cref="ProbeBudget"/>（アトラスの半分）以下になるまで広げることで、ラウンドロビン全体の更新時間を短く保つ。
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
    /// <summary>レンダラーの GlobalIlluminationSettings と同じグリッド規則：軸ごとに floor(2h/s)+1、最小 2。</summary>
    internal static long ProbeCount(Vector3 HalfExtent,float Spacing){
        static long Axis(float Half,float Spacing)=>Math.Max(2L,(long)MathF.Floor(2f*Half/Spacing)+1L);
        return Axis(HalfExtent.X,Spacing)*Axis(HalfExtent.Y,Spacing)*Axis(HalfExtent.Z,Spacing);
    }
}
