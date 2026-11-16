using System.Numerics;
namespace CSharpGameEngine.Rendering.Pipeline;
/// <summary>光源の順位付けで比べる値。<see cref="Index"/> は呼び出し側の提出の並びでの位置。</summary>
/// <param name="Index">呼び出し側の提出の並びでの位置。</param>
/// <param name="SelectionPriority">選択の優先度。局所光だけが使う。</param>
/// <param name="ScreenContribution">画面上の寄与の見積もり。</param>
/// <param name="StableSubmissionOrder">提出の順。同じ寄与のときの決め手で、提出ごとに異なる。</param>
public readonly record struct StageLightRank(int Index,int SelectionPriority,float ScreenContribution,int StableSubmissionOrder);
/// <summary>Forward+ のタイルの範囲（両端を含む）。</summary>
public readonly record struct StageTileRange(int MinimumX,int MaximumX,int MinimumY,int MaximumY);
/// <summary>
/// Stage の光源の計画（ADR-0087 §1-2 の StageLightPlanner）。どの光源を使い、Forward+ のどのタイルへ割り当てるかを決める。
/// GPU を使わない計算だけで、D3D12 の実行器（L0）は結果を書き込むだけにする。
/// M0120 の移行で L0 の <c>StageRenderer</c> から式と比較の順を変えずに移した。
/// </summary>
public static class StageLightPlanner {
    /// <summary>
    /// 局所光の画面上の寄与の見積もり。最も明るい色の成分 × 範囲² ÷ 深度²。値が有限でなければ最大値にして、必ず選ばれる側へ寄せる。
    /// </summary>
    public static float LocalScreenContribution(Vector3 ColorTimesIntensity,Vector3 Position,float Range,in Matrix4x4 ViewProjection){
        float Luminance=MathF.Max(ColorTimesIntensity.X,MathF.Max(ColorTimesIntensity.Y,ColorTimesIntensity.Z));
        if(!float.IsFinite(Luminance)||!float.IsFinite(Range)){
            return float.MaxValue;
        }
        Luminance=MathF.Max(0f,Luminance);
        Vector4 Clip=Vector4.Transform(new Vector4(Position,1f),ViewProjection);
        float Depth=MathF.Abs(Clip.W);
        if(!float.IsFinite(Depth)||Depth<1e-3f){
            return MathF.Min(float.MaxValue,Luminance*Range*Range);
        }
        float Contribution=Luminance*Range*Range/MathF.Max(Depth*Depth,1e-4f);
        return float.IsFinite(Contribution)?Contribution:float.MaxValue;
    }
    /// <summary>方向光の画面上の寄与の見積もり。最も明るい色の成分。</summary>
    public static float DirectionalScreenContribution(Vector3 ColorTimesIntensity){
        float Contribution=MathF.Max(ColorTimesIntensity.X,MathF.Max(ColorTimesIntensity.Y,ColorTimesIntensity.Z));
        if(!float.IsFinite(Contribution)){
            return float.MaxValue;
        }
        return MathF.Max(0f,Contribution);
    }
    /// <summary>局所光の順。優先度の高い順、寄与の大きい順、提出の早い順。提出の順が異なれば全順序になるので、並べ方の手順によらず結果は一つに決まる。</summary>
    public static int CompareLocal(StageLightRank Left,StageLightRank Right){
        int Priority=Right.SelectionPriority.CompareTo(Left.SelectionPriority);
        if(Priority!=0){
            return Priority;
        }
        int Contribution=Right.ScreenContribution.CompareTo(Left.ScreenContribution);
        return Contribution!=0?Contribution:Left.StableSubmissionOrder.CompareTo(Right.StableSubmissionOrder);
    }
    /// <summary>方向光の順。直接光の選択では影の優先度を見ない。寄与の大きい順、提出の早い順。</summary>
    public static int CompareDirectional(StageLightRank Left,StageLightRank Right){
        int Contribution=Right.ScreenContribution.CompareTo(Left.ScreenContribution);
        return Contribution!=0?Contribution:Left.StableSubmissionOrder.CompareTo(Right.StableSubmissionOrder);
    }
    /// <summary>局所光を使う順に並べる。並べた後の先頭から上限までを使う。</summary>
    public static void SortLocal(Span<StageLightRank>Ranks)=>Ranks.Sort(CompareLocal);
    /// <summary>方向光を使う順に並べる。</summary>
    public static void SortDirectional(Span<StageLightRank>Ranks)=>Ranks.Sort(CompareDirectional);
    /// <summary>
    /// 局所光が届く範囲（中心から各軸に ±範囲の箱）を画面へ写し、Forward+ のタイルの範囲にする。
    /// 画面の外なら false。位置や範囲が有限でないとき、または箱の角がカメラの後ろへ回るときは、取りこぼさないよう全タイルにする。
    /// </summary>
    public static bool TryGetTileRange(Vector3 Position,float Range,in Matrix4x4 ViewProjection,int Width,int Height,int TileCountX,int TileCountY,int TileSizePixels,out StageTileRange Tiles){
        if(TileCountX<=0||TileCountY<=0){
            Tiles=default;
            return false;
        }
        var Everything=new StageTileRange(0,TileCountX-1,0,TileCountY-1);
        if(!float.IsFinite(Position.X)||!float.IsFinite(Position.Y)||!float.IsFinite(Position.Z)||!float.IsFinite(Range)||Range<=0f){
            Tiles=Everything;
            return true;
        }
        float MinimumNdcX=float.MaxValue;
        float MaximumNdcX=float.MinValue;
        float MinimumNdcY=float.MaxValue;
        float MaximumNdcY=float.MinValue;
        for(int X=-1;X<=1;X+=2){
            for(int Y=-1;Y<=1;Y+=2){
                for(int Z=-1;Z<=1;Z+=2){
                    Vector3 Corner=Position+(new Vector3(X,Y,Z)*Range);
                    Vector4 Clip=Vector4.Transform(new Vector4(Corner,1f),ViewProjection);
                    if(!float.IsFinite(Clip.X)||!float.IsFinite(Clip.Y)||!float.IsFinite(Clip.W)||Clip.W<=1e-4f){
                        Tiles=Everything;
                        return true;
                    }
                    float NdcX=Clip.X/Clip.W;
                    float NdcY=Clip.Y/Clip.W;
                    MinimumNdcX=MathF.Min(MinimumNdcX,NdcX);
                    MaximumNdcX=MathF.Max(MaximumNdcX,NdcX);
                    MinimumNdcY=MathF.Min(MinimumNdcY,NdcY);
                    MaximumNdcY=MathF.Max(MaximumNdcY,NdcY);
                }
            }
        }
        if(MaximumNdcX< -1f||MinimumNdcX>1f||MaximumNdcY< -1f||MinimumNdcY>1f){
            Tiles=default;
            return false;
        }
        float MinimumPixelX=(MinimumNdcX*0.5f+0.5f)*Width;
        float MaximumPixelX=(MaximumNdcX*0.5f+0.5f)*Width;
        float MinimumPixelY=(-MaximumNdcY*0.5f+0.5f)*Height;
        float MaximumPixelY=(-MinimumNdcY*0.5f+0.5f)*Height;
        Tiles=new StageTileRange(
            Math.Clamp((int)MathF.Floor(MinimumPixelX/TileSizePixels),0,TileCountX-1),
            Math.Clamp((int)MathF.Floor(MaximumPixelX/TileSizePixels),0,TileCountX-1),
            Math.Clamp((int)MathF.Floor(MinimumPixelY/TileSizePixels),0,TileCountY-1),
            Math.Clamp((int)MathF.Floor(MaximumPixelY/TileSizePixels),0,TileCountY-1));
        return true;
    }
}
