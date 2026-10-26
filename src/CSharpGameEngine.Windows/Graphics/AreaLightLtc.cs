using System.Numerics;

namespace CSharpGameEngine.Graphics.D3D12;

/// <summary>片面のランバート反射長方形発光体に対する、恒等 LTC 評価。戻り値は受光点から見た余弦重み付き立体角なので、nit 単位の RGB 輝度をそのまま乗算して、マテリアル BRDF の適用前に lux 単位の放射照度を得る。</summary>
public static class AreaLightLtc {
    private const float MinimumDirectionLengthSquared=1e-12f;
    private const float BasisOrthogonalityTolerance=1e-3f;
    private const float MinimumRightHandedness=0.999f;

    /// <summary>
    /// 片面の長方形が作る、非負の余弦重み付き立体角を評価する。光源は <paramref name="Forward"/>（Transform のローカル +Z）方向へ放射する。
    /// <paramref name="Right"/> と <paramref name="Up"/> は長方形のローカル +X / +Y 軸を定義し、光源とともに右手系の正規直交基底を構成しなければならな
    /// い。
    /// </summary>
    /// <exception cref="ArgumentException">ベクトル、範囲、基底が無効な場合。</exception>
    public static float EvaluateOneSidedProjectedSolidAngle(
        Vector3 Center,Vector3 Forward,Vector3 Right,Vector3 Up,
        float HalfWidth,float HalfHeight,
        Vector3 ReceiverPosition,Vector3 ReceiverNormal){
        if(!TryEvaluateOneSidedProjectedSolidAngle(
            Center,Forward,Right,Up,HalfWidth,HalfHeight,
            ReceiverPosition,ReceiverNormal,out float Factor)){
            throw new ArgumentException(
                "A rectangular Area Light requires finite positions, positive extents, and a right-handed orthonormal basis.");
        }
        return Factor;
    }

    /// <summary>
    /// 不正な光源データに対して、割り当てや例外を発生させずに <see cref="EvaluateOneSidedProjectedSolidAngle"/> の評価を試みる。光源の背面を向く場合や、
    /// 地平線でクリップされる場合も、係数0で成功する。
    /// </summary>
    public static bool TryEvaluateOneSidedProjectedSolidAngle(
        Vector3 Center,Vector3 Forward,Vector3 Right,Vector3 Up,
        float HalfWidth,float HalfHeight,
        Vector3 ReceiverPosition,Vector3 ReceiverNormal,out float Factor){
        Factor=0f;
        if(!IsFinite(Center)||!IsFinite(ReceiverPosition)
            ||!float.IsFinite(HalfWidth)||!float.IsFinite(HalfHeight)
            ||HalfWidth<=0f||HalfHeight<=0f
            ||!TryNormalize(Forward,out Vector3 UnitForward)
            ||!TryNormalize(Right,out Vector3 UnitRight)
            ||!TryNormalize(Up,out Vector3 UnitUp)
            ||!TryNormalize(ReceiverNormal,out Vector3 UnitReceiverNormal)){
            return false;
        }

        if(MathF.Abs(Vector3.Dot(UnitForward,UnitRight))>BasisOrthogonalityTolerance
            ||MathF.Abs(Vector3.Dot(UnitForward,UnitUp))>BasisOrthogonalityTolerance
            ||MathF.Abs(Vector3.Dot(UnitRight,UnitUp))>BasisOrthogonalityTolerance
            ||Vector3.Dot(Vector3.Cross(UnitRight,UnitUp),UnitForward)<MinimumRightHandedness){
            return false;
        }

        // ランバート発光体は背面から直接光を寄与しない。全体の
        // 長方形全体は同じ平面上にあるため、中心側かどうかの判定で十分かつ安定する。
        if(Vector3.Dot(UnitForward,ReceiverPosition-Center)<=0f){
            return true;
        }

        Span<Vector3> Directions=stackalloc Vector3[4];
        Vector3 Width=UnitRight*HalfWidth;
        Vector3 Height=UnitUp*HalfHeight;
        // 光源の位置で、長方形の Right / Up の巻き順は Forward を向く。受光点が
        // 発光側にある場合、各方向は受光点から光源へ向かうため、逆の
        // 巻き順を使って、球面面積ベクトルを入射側の半球に向ける。
        if(!TryDirection(Center-Width-Height,ReceiverPosition,out Directions[0])
            ||!TryDirection(Center-Width+Height,ReceiverPosition,out Directions[1])
            ||!TryDirection(Center+Width+Height,ReceiverPosition,out Directions[2])
            ||!TryDirection(Center+Width-Height,ReceiverPosition,out Directions[3])){
            return false;
        }

        Span<Vector3> ClippedDirections=stackalloc Vector3[8];
        int ClippedCount=ClipToReceiverHemisphere(
            Directions,UnitReceiverNormal,ClippedDirections);
        if(ClippedCount<3){
            return true;
        }

        Vector3 Integral=Vector3.Zero;
        for(int Index=0;Index<ClippedCount;Index++){
            Integral+=IntegrateSphericalEdge(
                ClippedDirections[Index],ClippedDirections[(Index+1)%ClippedCount]);
        }

        float ProjectedSolidAngle=Vector3.Dot(UnitReceiverNormal,Integral);
        if(!float.IsFinite(ProjectedSolidAngle)){
            return false;
        }

        // 受光側の半球について cos(theta) を積分すると π になる。物理的な範囲外の丸め誤差だけを制限する。
        // ここでは距離減衰を適用しない。
        // 長方形の立体角に逆二乗の減衰がすでに含まれるため。
        Factor=Math.Clamp(ProjectedSolidAngle,0f,MathF.PI);
        return true;
    }

    private static bool TryDirection(Vector3 Corner,Vector3 ReceiverPosition,out Vector3 Direction){
        Vector3 ToCorner=Corner-ReceiverPosition;
        return TryNormalize(ToCorner,out Direction);
    }

    private static int ClipToReceiverHemisphere(
        ReadOnlySpan<Vector3> Directions,Vector3 ReceiverNormal,Span<Vector3> Clipped){
        Vector3 Previous=Directions[^1];
        float PreviousHeight=Vector3.Dot(ReceiverNormal,Previous);
        bool PreviousInside=PreviousHeight>0f;
        int Count=0;

        for(int Index=0;Index<Directions.Length;Index++){
            Vector3 Current=Directions[Index];
            float CurrentHeight=Vector3.Dot(ReceiverNormal,Current);
            bool CurrentInside=CurrentHeight>0f;

            if(CurrentInside!=PreviousInside){
                float Denominator=PreviousHeight-CurrentHeight;
                if(MathF.Abs(Denominator)>float.Epsilon
                    &&TryNormalize(Previous+((Current-Previous)*(PreviousHeight/Denominator)),
                        out Vector3 Intersection)){
                    Clipped[Count++]=Intersection;
                }
            }

            if(CurrentInside){
                Clipped[Count++]=Current;
            }

            Previous=Current;
            PreviousHeight=CurrentHeight;
            PreviousInside=CurrentInside;
        }

        return Count;
    }

    /// <summary>大円で囲まれた辺1つに沿うベクトル積分を返す。係数1/2は球面面積ベクトルの恒等式による。integral(omega dOmega)=1/2 integral(r cross dr)。</summary>
    private static Vector3 IntegrateSphericalEdge(Vector3 From,Vector3 To){
        Vector3 Cross=Vector3.Cross(From,To);
        float SinAngle=Cross.Length();
        if(SinAngle<=float.Epsilon){
            return Vector3.Zero;
        }

        float CosAngle=Math.Clamp(Vector3.Dot(From,To),-1f,1f);
        float Angle=MathF.Atan2(SinAngle,CosAngle);
        return Cross*(0.5f*Angle/SinAngle);
    }

    private static bool TryNormalize(Vector3 Value,out Vector3 Normalized){
        if(!IsFinite(Value)){
            Normalized=default;
            return false;
        }

        float LengthSquared=Value.LengthSquared();
        if(!float.IsFinite(LengthSquared)||LengthSquared<=MinimumDirectionLengthSquared){
            Normalized=default;
            return false;
        }

        Normalized=Value/MathF.Sqrt(LengthSquared);
        return IsFinite(Normalized);
    }

    private static bool IsFinite(Vector3 Value)=>
        float.IsFinite(Value.X)&&float.IsFinite(Value.Y)&&float.IsFinite(Value.Z);
}
