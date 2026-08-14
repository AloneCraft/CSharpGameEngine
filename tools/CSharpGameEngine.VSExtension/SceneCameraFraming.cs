using System;
using System.Collections.Generic;
using System.Numerics;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Scene デザイナーの視点ポリシー。<see cref="SceneRenderer"/> は編集ビュー状態を一切持たず
/// (ADR-0041 決定事項1)、view/projection はここで組んで <see cref="SceneRenderer.SetCamera"/> から
/// 注入する。<see cref="SceneRenderer"/> は再解析のたびに破棄・再生成されるため、カメラ計算を
/// レンダラーの外へ出しておく必要がある。
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
    /// <summary>既定の縦FOV(45°)。現行の <c>SceneRenderer</c> の投影と同じ。</summary>
    internal const float DefaultFovYRadians=(float)(Math.PI/4.0);

    /// <summary>固定の斜め俯瞰方向。<c>Camera.CreateBoundsFit</c> と同じ <c>normalize(1,0.75,1)</c>。</summary>
    internal static readonly Vector3 ViewDirection=Vector3.Normalize(new Vector3(1f,0.75f,1f));

    /// <summary>境界球が視野の縁に触れない余裕。</summary>
    private const float FramingSafetyMargin=1.1f;

    private const float NearPlane=0.05f;

    private const float FarPlaneRadiusFactor=20f;

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

        foreach(SceneRenderObject RenderObject in RenderObjects){
            if(RespectIncludeInBoundsFlag&&!RenderObject.IncludeInBounds){
                continue;
            }

            foreach(Vector3 LocalPosition in RenderObject.Mesh.Positions){
                Vector3 WorldPosition=Vector3.Transform(LocalPosition,RenderObject.World);
                Minimum=Vector3.Min(Minimum,WorldPosition);
                Maximum=Vector3.Max(Maximum,WorldPosition);
                Any=true;
            }
        }

        if(!Any){
            return(Vector3.Zero,1f);
        }

        Vector3 Center=(Minimum+Maximum)*0.5f;
        var Radius=0f;
        foreach(SceneRenderObject RenderObject in RenderObjects){
            if(RespectIncludeInBoundsFlag&&!RenderObject.IncludeInBounds){
                continue;
            }

            foreach(Vector3 LocalPosition in RenderObject.Mesh.Positions){
                Vector3 WorldPosition=Vector3.Transform(LocalPosition,RenderObject.World);
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
    internal static float ComputeDistance(float BoundsRadius,float FovYRadians){
        var Radius=Math.Max(BoundsRadius,MinimumRadius);
        var HalfFov=Math.Max(FovYRadians,MinimumRadius)/2f;
        return Radius/(float)Math.Sin(HalfFov)*FramingSafetyMargin;
    }

    internal static Matrix4x4 CreateView(Vector3 BoundsCenter,float BoundsRadius,float FovYRadians){
        Vector3 Eye=BoundsCenter+ViewDirection*ComputeDistance(BoundsRadius,FovYRadians);
        return Matrix4x4.CreateLookAt(Eye,BoundsCenter,Vector3.UnitY);
    }

    /// <summary>
    /// near/far は M0020 Phase 0 までの値(0.05 / Radius*20)を維持する。<c>Camera.CreateBoundsFit</c> の
    /// <c>far = Distance + 4*Radius</c> では、自動フレーミング半径から意図的に除外している Sky dome
    /// (<c>IncludeInBounds=false</c>、Scale=400)がクリップされてしまう。far を FocusDistance から
    /// 再計算するのは Phase 1 の仕事。
    /// </summary>
    internal static Matrix4x4 CreateProjection(float BoundsRadius,float FovYRadians,float AspectRatio){
        var Radius=Math.Max(BoundsRadius,MinimumRadius);
        // ComputeBounds は半径を 1f で床打ちするので実経路では far=20 以上になるが、退化した半径を
        // 直接渡された場合に far が near を下回ると CreatePerspectiveFieldOfView が例外を投げる。
        var FarPlane=Math.Max(Radius*FarPlaneRadiusFactor,NearPlane*2f);
        return Matrix4x4.CreatePerspectiveFieldOfView(
            Math.Max(FovYRadians,MinimumRadius),
            Math.Max(AspectRatio,MinimumRadius),
            NearPlane,
            FarPlane);
    }

    internal static(Matrix4x4 View,Matrix4x4 Projection)FrameBounds(
        Vector3 BoundsCenter,float BoundsRadius,float FovYRadians,float AspectRatio)=>(
        CreateView(BoundsCenter,BoundsRadius,FovYRadians),
        CreateProjection(BoundsRadius,FovYRadians,AspectRatio));
}
