using System.Numerics;
namespace CSharpGameEngine.Rendering.Pipeline;
/// <summary>
/// Stage の影の計画（ADR-0087 §1-2 の StageShadowPlanner）。局所光の影のアトラスへ写す投影と、GI のプローブを撮るときの深度の符号化を作る。
/// GPU を使わない計算だけで、D3D12 の実行器（L0）は結果を書き込むだけにする。方向光のカスケードは <see cref="DirectionalShadowCascades"/>、
/// アトラスの割り当ては <see cref="PhysicalShadowAtlasPlan"/> が持つ。M0120 の移行で L0 の <c>StageRenderer</c> から式を変えずに移した。
/// </summary>
public static class StageShadowPlanner {
    /// <summary>点光源の影は、+X、-X、+Y、-Y、+Z、-Z の順の 90 度の 6 面で写す。HLSL の面の選び方もこの順を使う。</summary>
    public const int PointFaceCount=6;
    /// <summary>スポットライトの影の投影。外側の円錐の角度を視野にした透視。</summary>
    public static bool TryBuildSpotViewProjection(Vector3 Position,Vector3 Direction,float Range,float OuterConeCos,out Matrix4x4 ShadowViewProjection){
        ShadowViewProjection=Matrix4x4.Identity;
        if(!IsFinite(Position)||!IsFinite(Direction)
            ||!float.IsFinite(Range)||Range<=0f
            ||!float.IsFinite(OuterConeCos)){
            return false;
        }
        float DirectionLengthSquared=Direction.LengthSquared();
        if(DirectionLengthSquared<1e-8f||!float.IsFinite(DirectionLengthSquared)){
            return false;
        }
        Vector3 Forward=Vector3.Normalize(Direction);
        Vector3 Up=MathF.Abs(Vector3.Dot(Forward,Vector3.UnitY))>0.99f?Vector3.UnitX:Vector3.UnitY;
        float OuterHalfAngleRadians=MathF.Acos(Math.Clamp(OuterConeCos,-1f,1f));
        float FieldOfView=Math.Clamp(OuterHalfAngleRadians*2f,0.01f,MathF.PI-0.01f);
        float NearZ=Math.Clamp(Range*0.005f,0.01f,0.1f);
        float FarZ=MathF.Max(Range,NearZ+0.01f);
        Matrix4x4 View=Matrix4x4.CreateLookAt(Position,Position+Forward,Up);
        ShadowViewProjection=View*Matrix4x4.CreatePerspectiveFieldOfView(FieldOfView,1f,NearZ,FarZ);
        return true;
    }
    /// <summary>
    /// 面光源（矩形と発光面）のアトラスのタイルに使う、光源の法線を中心にした 90 度の透視。直接照明では光源を矩形のまま扱い、
    /// これは PCSS が参照する保守的な 1 タイルの投影。
    /// </summary>
    public static bool TryBuildAreaViewProjection(Vector3 Position,Vector3 Direction,Vector3 Up,float Range,float HalfWidth,float HalfHeight,out Matrix4x4 ShadowViewProjection){
        ShadowViewProjection=Matrix4x4.Identity;
        if(!IsFinite(Position)||!IsFinite(Direction)||!IsFinite(Up)
            ||!float.IsFinite(Range)||Range<=0f
            ||!float.IsFinite(HalfWidth)||HalfWidth<=0f
            ||!float.IsFinite(HalfHeight)||HalfHeight<=0f){
            return false;
        }
        if(!TryNormalize(Direction,out Vector3 Forward)
            ||!TryNormalize(Up,out Vector3 UnitUp)){
            return false;
        }
        float DirectionUpDot=MathF.Abs(Vector3.Dot(Forward,UnitUp));
        if(!float.IsFinite(DirectionUpDot)||DirectionUpDot>=0.999f){
            return false;
        }
        float NearZ=Math.Clamp(Range*0.005f,0.01f,0.1f);
        float FarZ=MathF.Max(Range,NearZ+0.01f);
        Matrix4x4 View=Matrix4x4.CreateLookAt(Position,Position+Forward,UnitUp);
        ShadowViewProjection=View*Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI/2f,1f,NearZ,FarZ);
        return true;
    }
    /// <summary>点光源が影を落とせるか（位置が有限で、範囲が正）。</summary>
    public static bool IsValidPointCaster(Vector3 Position,float Range)=>IsFinite(Position)&&float.IsFinite(Range)&&Range>0f;
    /// <summary>点光源の 6 面の投影を <paramref name="FaceViewProjections"/> の先頭から書く。長さは <see cref="PointFaceCount"/> 以上。</summary>
    public static bool TryBuildPointFaceViewProjections(Vector3 Position,float Range,Span<Matrix4x4>FaceViewProjections){
        if(FaceViewProjections.Length<PointFaceCount||!IsValidPointCaster(Position,Range)){
            return false;
        }
        float NearZ=Math.Clamp(Range*0.005f,0.01f,0.1f);
        float FarZ=MathF.Max(Range,NearZ+0.01f);
        Matrix4x4 Projection=Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI/2f,1f,NearZ,FarZ);
        for(int FaceIndex=0;FaceIndex<PointFaceCount;FaceIndex++){
            GetPointFaceBasis(FaceIndex,out Vector3 Direction,out Vector3 Up);
            Matrix4x4 View=Matrix4x4.CreateLookAt(Position,Position+Direction,Up);
            FaceViewProjections[FaceIndex]=View*Projection;
        }
        return true;
    }
    /// <summary>点光源の面の向きと上方向。</summary>
    public static void GetPointFaceBasis(int FaceIndex,out Vector3 Direction,out Vector3 Up){
        switch(FaceIndex){
            case 0:
                Direction=Vector3.UnitX;
                Up=Vector3.UnitY;
                return;
            case 1:
                Direction=-Vector3.UnitX;
                Up=Vector3.UnitY;
                return;
            case 2:
                Direction=Vector3.UnitY;
                Up=-Vector3.UnitZ;
                return;
            case 3:
                Direction=-Vector3.UnitY;
                Up=Vector3.UnitZ;
                return;
            case 4:
                Direction=Vector3.UnitZ;
                Up=Vector3.UnitY;
                return;
            case 5:
                Direction=-Vector3.UnitZ;
                Up=Vector3.UnitY;
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(FaceIndex));
        }
    }
    /// <summary>GI のプローブを撮る 90 度・縦横比 1 の面の、対数深度の符号化。近い面・遠い面が不正なら例外。</summary>
    public static RenderViewDepth ProbeCaptureDepth(float NearMeters,float FarMeters){
        if(!RadialDepthProjection.TryCreate(RadialDepthCurve.Default,NearMeters,FarMeters,MathF.PI/2f,1f,out var CaptureProjection)){
            throw new InvalidOperationException("Invalid probe capture projection.");
        }
        return RenderViewDepth.ForPerspective(CaptureProjection,ConservativeRasterDepth:false);
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
