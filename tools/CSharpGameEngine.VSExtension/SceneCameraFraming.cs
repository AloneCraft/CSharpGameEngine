using System;
using System.Collections.Generic;
using System.Numerics;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Scene デザイナーの視点ポリシー。Preview Hostは編集ビュー状態を一切持たず
/// (ADR-0041 決定事項1)、view/projection はここで組んでimmutable frame DTOから注入する。
/// Host process/sessionは再生成され得るため、カメラ計算と正本をVSIX側に置く。
///
/// <para>
/// 純粋な計算だけなので GPU も HWND も要らず、ユニットテストできる(バウンズ計算がテスト可能に
/// なったのはこの移設の副産物)。M0020 Phase 1 でナビゲーションが入ると、呼び出し側が
/// <c>SceneViewState</c> 由来の view/projection を渡すようになり、ここは初期フレーミング専用になる。
/// M0020 Phase 2 の FocusSelection(F)は選択ノード版の <see cref="ComputeSelectionBounds"/> を使う。
/// </para>
///
/// <para>
/// net472 には <c>MathF</c> が存在しないので <c>(float)Math.*</c> を使う。net10.0 側のカメラ数学を
/// 写すときの定番の落とし穴。
/// </para>
/// </summary>
internal static class SceneCameraFraming {
    /// <summary>既定の縦FOV(45°)。共通Render ServerのScene preview投影と同じ。</summary>
    internal const float DefaultFovYRadians=FloatMath.Pi/4f;

    /// <summary>固定の斜め俯瞰方向。<c>Camera.CreateBoundsFit</c> と同じ <c>normalize(1,0.75,1)</c>。</summary>
    internal static readonly Vector3 ViewDirection=Vector3.Normalize(new Vector3(1f,0.75f,1f));

    /// <summary>境界球が視野の縁に触れない余裕。</summary>
    private const float FramingSafetyMargin=1.1f;

    /// <summary>
    /// near 平面の下限。M0020 Phase 0 までは 0.05 の固定値だったが、実ステージ半径では far が
    /// 数百〜千まで伸びるため near:far が 20000:1 に達し、壁/床/Sky dome の境界が Z-fighting した
    /// (M0020 残課題 R1)。以後は <see cref="ComputeNearPlane"/> で far に対する比率として決める。
    /// </summary>
    internal const float BaseNearPlane=0.05f;

    /// <summary>
    /// 透視投影で許容する far/near 比の上限。Preview Hostの深度バッファは <c>D32_Float</c>
    /// なので 24bit UNORM より余裕があり、10000:1 で 1/z 分布は破綻しない。この値だと実ステージ規模
    /// (far 8000〜10000)で near が 0.8〜1.0 になり、M0020 残課題 R1 が挙げた推奨値 0.5〜1.0 に収まる。
    /// 小さいシーンでは <see cref="BaseNearPlane"/> が優先されるので、near が不要に遠のいて
    /// 手前が切れることはない。
    /// </summary>
    internal const float MaxDepthRatio=10000f;

    private const float FarPlaneRadiusFactor=20f;

    /// <summary>
    /// 透視投影専用。正射投影の深度は線形なので比率調整は不要(かつ手前を無駄に切るので有害)であり、
    /// <see cref="BaseNearPlane"/> をそのまま使う。
    /// </summary>
    internal static float ComputeNearPlane(float FarPlane)=>
        Math.Max(BaseNearPlane,FarPlane/MaxDepthRatio);

    private const float MinimumRadius=0.001f;

    /// <summary>
    /// ワールド空間の境界球。<c>Radius</c> は中心から最遠頂点までの距離(AABB 半対角ではない)。
    /// <c>IncludeInBounds=false</c> のオブジェクト(Sky dome 等)は描画されるが半径には寄与しない。
    /// </summary>
    internal static(Vector3 Center,float Radius)ComputeBounds(IReadOnlyList<SceneRenderObject>RenderObjects)=>
        ComputeBoundsCore(RenderObjects,RespectIncludeInBoundsFlag:true);

    /// <summary>
    /// M0020 Phase 2 FocusSelection(F)用。選択済みオブジェクトのみを渡す前提であり、
    /// <c>IncludeInBounds=false</c>(Sky dome等、自動フレーミング専用の除外)は無視して収める
    /// (ユーザーが明示的にそのオブジェクトを選択している以上、除外すると縮退したフォールバック
    /// <c>(Zero,1f)</c>へ落ちて原点へジャンプしてしまうため)。
    /// </summary>
    internal static(Vector3 Center,float Radius)ComputeSelectionBounds(IReadOnlyList<SceneRenderObject>SelectedRenderObjects)=>
        ComputeBoundsCore(SelectedRenderObjects,RespectIncludeInBoundsFlag:false);

    private static(Vector3 Center,float Radius)ComputeBoundsCore(
        IReadOnlyList<SceneRenderObject>RenderObjects,bool RespectIncludeInBoundsFlag){
        Vector3 Minimum=new(float.MaxValue);
        Vector3 Maximum=new(float.MinValue);
        var Any=false;

        foreach(var RenderObject in RenderObjects){
            if(RespectIncludeInBoundsFlag&&!RenderObject.IncludeInBounds){
                continue;
            }

            foreach(var LocalPosition in RenderObject.Mesh.Positions){
                var WorldPosition=Vector3.Transform(LocalPosition,RenderObject.World);
                Minimum=Vector3.Min(Minimum,WorldPosition);
                Maximum=Vector3.Max(Maximum,WorldPosition);
                Any=true;
            }
        }

        if(!Any){
            return(Vector3.Zero,1f);
        }

        var Center=(Minimum+Maximum)*0.5f;
        var Radius=0f;
        foreach(var RenderObject in RenderObjects){
            if(RespectIncludeInBoundsFlag&&!RenderObject.IncludeInBounds){
                continue;
            }

            foreach(var LocalPosition in RenderObject.Mesh.Positions){
                var WorldPosition=Vector3.Transform(LocalPosition,RenderObject.World);
                Radius=Math.Max(Radius,Vector3.Distance(Center,WorldPosition));
            }
        }

        return(Center,Radius>0f?Radius:1f);
    }

    /// <summary>
    /// 半径 <paramref name="BoundsRadius"/> の境界球が縦FOVにちょうど収まる接距離
    /// <c>Radius/sin(FovY/2)</c> に安全余裕 1.1 を掛けた距離。FovY=45° では約 2.874*Radius で、
    /// M0020 Phase 0 まで使っていた固定値 3.0*Radius と 4% しか違わない(見た目維持の根拠)。
    /// 魔法数 3.0 ではなく FOV から導出する形にしてあるので、Phase 1 で FOV を可変にしても壊れない。
    /// </summary>
    internal static float ComputeDistance(float BoundsRadius,float FovYRadians,float AspectRatio){
        var Radius=Math.Max(BoundsRadius,MinimumRadius);
        // 縦FOVではなく「実際に境界球を制約する側」のFOVで接距離を出す。縦長ウィンドウでは水平FOVの
        // 方が狭いため、縦FOVだけで決めると左右にはみ出す(aspect 0.75 という緩い縦長でも実測で
        // NDC x=1.012 と溢れた)。aspect≥1 では実効FOV=縦FOVなので既存の距離は変わらない。
        var HalfFov=ComputeEffectiveFovRadians(FovYRadians,AspectRatio)/2f;
        return Radius/FloatMath.Sin(HalfFov)*FramingSafetyMargin;
    }

    /// <summary>
    /// 境界球を実際に制約する側のFOV。水平FOVは <c>2*atan(tan(FovY/2)*aspect)</c> なので、
    /// 縦長ウィンドウ(<paramref name="AspectRatio"/>&lt;1)では縦より狭くなる。
    /// <c>min</c> を取ることで aspect≥1 では縦FOVがそのまま返り、横長・正方形の既存の距離計算が
    /// 1ビットも変わらない(<c>ComputeDistance_WideAspect_MatchesTheSquareCaseExactly</c>が固定)。
    /// </summary>
    internal static float ComputeEffectiveFovRadians(float FovYRadians,float AspectRatio){
        var SafeFovY=Math.Max(FovYRadians,MinimumRadius);
        var SafeAspect=Math.Max(AspectRatio,MinimumRadius);
        if(SafeAspect>=1f){
            return SafeFovY;
        }

        var HorizontalFov=2f*FloatMath.Atan(FloatMath.Tan(SafeFovY/2f)*SafeAspect);
        return Math.Min(SafeFovY,HorizontalFov);
    }

    internal static Matrix4x4 CreateView(Vector3 BoundsCenter,float BoundsRadius,float FovYRadians,float AspectRatio){
        var Eye=BoundsCenter+ViewDirection*ComputeDistance(BoundsRadius,FovYRadians,AspectRatio);
        return Matrix4x4.CreateLookAt(Eye,BoundsCenter,Vector3.UnitY);
    }

    /// <summary>
    /// far は M0020 Phase 0 までの値(Radius*20)を維持する。<c>Camera.CreateBoundsFit</c> の
    /// <c>far = Distance + 4*Radius</c> では、自動フレーミング半径から意図的に除外している Sky dome
    /// (<c>IncludeInBounds=false</c>、Scale=400)がクリップされてしまう。far を FocusDistance から
    /// 再計算するのは Phase 1 の仕事。near は M0030 で <see cref="ComputeNearPlane"/> による
    /// far 連動へ変えた(残課題 R1)。
    /// </summary>
    internal static Matrix4x4 CreateProjection(float BoundsRadius,float FovYRadians,float AspectRatio){
        var Radius=Math.Max(BoundsRadius,MinimumRadius);
        // ComputeBounds は半径を 1f で床打ちするので実経路では far=20 以上になるが、退化した半径を
        // 直接渡された場合に far が near を下回ると CreatePerspectiveFieldOfView が例外を投げる。
        var FarPlane=Math.Max(Radius*FarPlaneRadiusFactor,BaseNearPlane*2f);
        return Matrix4x4.CreatePerspectiveFieldOfView(
            Math.Max(FovYRadians,MinimumRadius),
            Math.Max(AspectRatio,MinimumRadius),
            ComputeNearPlane(FarPlane),
            FarPlane);
    }

    internal static(Matrix4x4 View,Matrix4x4 Projection)FrameBounds(
        Vector3 BoundsCenter,float BoundsRadius,float FovYRadians,float AspectRatio)=>(
        CreateView(BoundsCenter,BoundsRadius,FovYRadians,AspectRatio),
        CreateProjection(BoundsRadius,FovYRadians,AspectRatio));
}
