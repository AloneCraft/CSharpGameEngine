using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// M0037: UE5風のドラッグ中HUD(開始点マーカー・破線プレビュー・角度の扇・数値読み取り)。
/// overlayパイプラインはunlitな頂点色の線/三角形のみでテクスチャ・文字描画機能が無く、ネイティブ
/// HWNDのairspace制約でWPFの文字をビューポート上へ重ねることもできない
/// (<c>SceneDesignerPresentationContracts.cs</c>参照)ため、数値はベクターフォント(線分の集合)として
/// overlayへ焼き込む。<see cref="MarqueeOverlay.ToWorld"/>と同じ「スクリーン座標をnear面へ逆投影し
/// レイ方向へ僅かに押し出す」手法を使い、Perspective/Orthographicどちらでも指定ピクセルへ投影される。
/// </summary>
internal static class GizmoDragHud {
    private const float NearPush=0.01f;
    private const float GlyphAdvance=0.7f;
    private const float SpaceAdvance=0.45f;
    private const float DotPixelRadius=4f;
    // 奇数にすることで最後のダッシュがちょうどTo側の端点で終わる(偶数だと最後がgapで
    // 終わり、終点がわずかに手前で途切れて見える)。
    private const int DashCount=15;
    private const int WedgeMaxSegments=96;
    private const float WedgeSegmentsPerRadian=15f;

    private static readonly Vector4 Yellow=new(1f,0.92f,0.1f,1f);
    private static readonly Vector4 WedgeFill=new(1f,0.92f,0.1f,0.35f);

    // TL/TR/ML/MR/BL/BR corners of a 0..0.5(幅) x 0..1(高さ) box。7セグメント表示の要領。
    private static readonly (float X0,float Y0,float X1,float Y1)SegA=(0.05f,0.95f,0.45f,0.95f);
    private static readonly (float X0,float Y0,float X1,float Y1)SegB=(0.45f,0.95f,0.45f,0.5f);
    private static readonly (float X0,float Y0,float X1,float Y1)SegC=(0.45f,0.5f,0.45f,0.05f);
    private static readonly (float X0,float Y0,float X1,float Y1)SegD=(0.05f,0.05f,0.45f,0.05f);
    private static readonly (float X0,float Y0,float X1,float Y1)SegE=(0.05f,0.5f,0.05f,0.05f);
    private static readonly (float X0,float Y0,float X1,float Y1)SegF=(0.05f,0.95f,0.05f,0.5f);
    private static readonly (float X0,float Y0,float X1,float Y1)SegG=(0.05f,0.5f,0.45f,0.5f);

    private static readonly Dictionary<char,(float X0,float Y0,float X1,float Y1)[]>Glyphs=new(){
        ['0']=[SegA,SegB,SegC,SegD,SegE,SegF],
        ['1']=[SegB,SegC],
        ['2']=[SegA,SegB,SegG,SegE,SegD],
        ['3']=[SegA,SegB,SegG,SegC,SegD],
        ['4']=[SegF,SegG,SegB,SegC],
        ['5']=[SegA,SegF,SegG,SegC,SegD],
        ['6']=[SegA,SegF,SegG,SegE,SegC,SegD],
        ['7']=[SegA,SegB,SegC],
        ['8']=[SegA,SegB,SegC,SegD,SegE,SegF,SegG],
        ['9']=[SegA,SegB,SegC,SegD,SegF,SegG],
        ['.']=[(0.2f,0.05f,0.3f,0.15f),(0.3f,0.05f,0.2f,0.15f)],
        ['-']=[(0.05f,0.5f,0.45f,0.5f)],
        ['+']=[(0.05f,0.5f,0.45f,0.5f),(0.25f,0.15f,0.25f,0.85f)],
        ['°']=[(0.15f,0.75f,0.28f,0.9f),(0.28f,0.9f,0.4f,0.75f),(0.4f,0.75f,0.28f,0.6f),(0.28f,0.6f,0.15f,0.75f)],
        ['x']=[(0.05f,0.05f,0.45f,0.95f),(0.05f,0.95f,0.45f,0.05f)],
        ['c']=[(0.45f,0.8f,0.1f,0.8f),(0.1f,0.8f,0.1f,0.2f),(0.1f,0.2f,0.45f,0.2f)],
        ['m']=[(0.05f,0.05f,0.05f,0.8f),(0.05f,0.8f,0.25f,0.3f),(0.25f,0.3f,0.25f,0.8f),(0.25f,0.8f,0.45f,0.3f),(0.45f,0.3f,0.45f,0.05f)],
    };

    internal static IReadOnlyList<GizmoOverlayPrimitive>Build(
        in GizmoDragHudState State,Matrix4x4 View,Matrix4x4 Projection,float ViewportWidth,float ViewportHeight){
        if(ViewportWidth<=0f||ViewportHeight<=0f||!Matrix4x4.Invert(View*Projection,out var InverseVP)){
            return [];
        }

        return State.Tool switch {
            SceneActiveTool.Move=>BuildMoveHud(State,View,Projection,InverseVP,ViewportWidth,ViewportHeight),
            SceneActiveTool.Rotate=>BuildRotateHud(State,View,Projection,InverseVP,ViewportWidth,ViewportHeight),
            SceneActiveTool.Scale=>BuildScaleHud(State,InverseVP,ViewportWidth,ViewportHeight),
            _=>[],
        };
    }

    private static IReadOnlyList<GizmoOverlayPrimitive>BuildMoveHud(
        in GizmoDragHudState State,Matrix4x4 View,Matrix4x4 Projection,Matrix4x4 InverseVP,
        float ViewportWidth,float ViewportHeight){
        List<GizmoOverlayPrimitive>Primitives=[];
        List<GizmoOverlayVertex>Lines=[];
        AddDashedWorldLine(Lines,State.StartPivot,State.CurrentPivot,Yellow);
        if(Lines.Count>0){ Primitives.Add(new GizmoOverlayPrimitive(GizmoPrimitiveKind.Line,Lines)); }

        var Dot=BuildScreenDot(
                              State.StartPivot,Yellow,View,Projection,InverseVP,ViewportWidth,ViewportHeight);
        if(Dot.Count>0){ Primitives.Add(new GizmoOverlayPrimitive(GizmoPrimitiveKind.Triangle,Dot)); }

        float Distance=(State.CurrentPivot-State.StartPivot).Length();
        var Text=Distance.ToString("F1",CultureInfo.InvariantCulture);
        AddLabelNearCursor(Primitives,Text,Yellow,State,InverseVP,ViewportWidth,ViewportHeight,Row:0);
        return Primitives;
    }

    private static IReadOnlyList<GizmoOverlayPrimitive>BuildRotateHud(
        in GizmoDragHudState State,Matrix4x4 View,Matrix4x4 Projection,Matrix4x4 InverseVP,
        float ViewportWidth,float ViewportHeight){
        List<GizmoOverlayPrimitive>Primitives=[];
        (var First,var Second)=TransformGizmo.CreatePlaneBasis(State.RotateNormal);
        float Radius=State.RingRadius*State.WorldScale;
        var ClampedRadians=Math.Max(-(float)(Math.PI*2d),Math.Min((float)(Math.PI*2d),State.RotateAccumulatedRadians));

        var Segments=Math.Max(1,Math.Min(WedgeMaxSegments,(int)(Math.Abs(ClampedRadians)*WedgeSegmentsPerRadian)+1));
        List<GizmoOverlayVertex>Fill=[];
        List<GizmoOverlayVertex>Outline=[];
        Vector3 PreviousPoint=State.Pivot+(State.RotateStartDirection*Radius);
        Outline.Add(new GizmoOverlayVertex(State.Pivot,Yellow));
        Outline.Add(new GizmoOverlayVertex(PreviousPoint,Yellow));
        for(var Segment=1;Segment<=Segments;Segment++){
            var Angle=ClampedRadians*Segment/Segments;
            var Direction=RotateAroundNormal(State.RotateStartDirection,First,Second,Angle);
            var Point=State.Pivot+(Direction*Radius);
            Fill.Add(new GizmoOverlayVertex(State.Pivot,WedgeFill));
            Fill.Add(new GizmoOverlayVertex(PreviousPoint,WedgeFill));
            Fill.Add(new GizmoOverlayVertex(Point,WedgeFill));
            Outline.Add(new GizmoOverlayVertex(PreviousPoint,Yellow));
            Outline.Add(new GizmoOverlayVertex(Point,Yellow));
            PreviousPoint=Point;
        }
        Outline.Add(new GizmoOverlayVertex(PreviousPoint,Yellow));
        Outline.Add(new GizmoOverlayVertex(State.Pivot,Yellow));
        Primitives.Add(new GizmoOverlayPrimitive(GizmoPrimitiveKind.Triangle,Fill));
        Primitives.Add(new GizmoOverlayPrimitive(GizmoPrimitiveKind.Line,Outline));

        // マウス方向フィードバック: 現在の方向へ、リング半径より少し長く伸ばした破線。
        var CurrentDirection=RotateAroundNormal(State.RotateStartDirection,First,Second,ClampedRadians);
        List<GizmoOverlayVertex>DirectionLine=[];
        AddDashedWorldLine(DirectionLine,State.Pivot,State.Pivot+(CurrentDirection*Radius*1.5f),Yellow);
        if(DirectionLine.Count>0){ Primitives.Add(new GizmoOverlayPrimitive(GizmoPrimitiveKind.Line,DirectionLine)); }

        var Dot=BuildScreenDot(
                              State.Pivot+(State.RotateStartDirection*Radius),Yellow,View,Projection,InverseVP,ViewportWidth,ViewportHeight);
        if(Dot.Count>0){ Primitives.Add(new GizmoOverlayPrimitive(GizmoPrimitiveKind.Triangle,Dot)); }

        // 表示角度は折り返し無しの累積値(180°を超えても符号が反転しない)。
        var Degrees=State.RotateAccumulatedRadians*(180f/(float)Math.PI);
        var Text=$"{Degrees.ToString("F1",CultureInfo.InvariantCulture)}°";
        AddLabelNearCursor(Primitives,Text,Yellow,State,InverseVP,ViewportWidth,ViewportHeight,Row:0);
        return Primitives;
    }

    private static IReadOnlyList<GizmoOverlayPrimitive>BuildScaleHud(
        in GizmoDragHudState State,Matrix4x4 InverseVP,float ViewportWidth,float ViewportHeight){
        List<GizmoOverlayPrimitive>Primitives=[];
        if(State.Handle==GizmoHandleId.ScaleUniform){
            AddScaleLine(Primitives,State.ScaleFactors.X,AxisColor(GizmoHandleId.ScaleX),State,InverseVP,ViewportWidth,ViewportHeight,Row:0);
            AddScaleLine(Primitives,State.ScaleFactors.Y,AxisColor(GizmoHandleId.ScaleY),State,InverseVP,ViewportWidth,ViewportHeight,Row:1);
            AddScaleLine(Primitives,State.ScaleFactors.Z,AxisColor(GizmoHandleId.ScaleZ),State,InverseVP,ViewportWidth,ViewportHeight,Row:2);
        } else {
            float Factor=State.Handle switch {
                GizmoHandleId.ScaleX=>State.ScaleFactors.X,
                GizmoHandleId.ScaleY=>State.ScaleFactors.Y,
                GizmoHandleId.ScaleZ=>State.ScaleFactors.Z,
                _=>1f,
            };
            AddScaleLine(Primitives,Factor,AxisColor(State.Handle),State,InverseVP,ViewportWidth,ViewportHeight,Row:0);
        }

        return Primitives;
    }

    private static void AddScaleLine(
        List<GizmoOverlayPrimitive>Primitives,float Factor,Vector4 Color,in GizmoDragHudState State,
        Matrix4x4 InverseVP,float ViewportWidth,float ViewportHeight,int Row){
        var Sign=Factor>=0f?"+":"";
        var Text=$"{Sign}{Factor.ToString("F2",CultureInfo.InvariantCulture)}x";
        AddLabelNearCursor(Primitives,Text,Color,State,InverseVP,ViewportWidth,ViewportHeight,Row);
    }

    private static Vector4 AxisColor(GizmoHandleId Handle)=>Handle switch {
        GizmoHandleId.ScaleX=>new Vector4(1f,0.15f,0.1f,1f),
        GizmoHandleId.ScaleY=>new Vector4(0.2f,1f,0.2f,1f),
        GizmoHandleId.ScaleZ=>new Vector4(0.2f,0.45f,1f,1f),
        _=>Vector4.One,
    };

    private static void AddLabelNearCursor(
        List<GizmoOverlayPrimitive>Primitives,string Text,Vector4 Color,in GizmoDragHudState State,
        Matrix4x4 InverseVP,float ViewportWidth,float ViewportHeight,int Row){
        const float PixelHeight=14f;
        const float OffsetX=18f;
        var OffsetY=18f+(Row*(PixelHeight+6f));
        var Text3D=BuildScreenText(
                                  Text,State.CursorX+OffsetX,State.CursorY-OffsetY,PixelHeight,Color,InverseVP,ViewportWidth,ViewportHeight);
        if(Text3D.Count>0){ Primitives.Add(new GizmoOverlayPrimitive(GizmoPrimitiveKind.Line,Text3D)); }
    }

    /// <summary>M0037: フォントの網羅性・文字送りをユニットテストできるよう公開。</summary>
    internal static IReadOnlyList<GizmoOverlayVertex>BuildScreenText(
        string Text,float AnchorScreenX,float AnchorScreenY,float PixelHeight,Vector4 Color,
        Matrix4x4 InverseVP,float ViewportWidth,float ViewportHeight){
        List<GizmoOverlayVertex>Vertices=[];
        var Cursor=0f;
        foreach(var Ch in Text){
            if(!Glyphs.TryGetValue(Ch,out var Segments)){
                Cursor+=SpaceAdvance;
                continue;
            }
            foreach(var Segment in Segments){
                var ScreenX0=AnchorScreenX+((Cursor+Segment.X0)*PixelHeight);
                var ScreenY0=AnchorScreenY-(Segment.Y0*PixelHeight);
                var ScreenX1=AnchorScreenX+((Cursor+Segment.X1)*PixelHeight);
                var ScreenY1=AnchorScreenY-(Segment.Y1*PixelHeight);
                Vertices.Add(new GizmoOverlayVertex(ToWorld(ScreenX0,ScreenY0,InverseVP,ViewportWidth,ViewportHeight),Color));
                Vertices.Add(new GizmoOverlayVertex(ToWorld(ScreenX1,ScreenY1,InverseVP,ViewportWidth,ViewportHeight),Color));
            }
            Cursor+=GlyphAdvance;
        }
        return Vertices;
    }

    private static IReadOnlyList<GizmoOverlayVertex>BuildScreenDot(
        Vector3 WorldAnchor,Vector4 Color,Matrix4x4 View,Matrix4x4 Projection,Matrix4x4 InverseVP,
        float ViewportWidth,float ViewportHeight){
        if(!SceneViewCamera.TryProjectToScreen(
            WorldAnchor,View,Projection,ViewportWidth,ViewportHeight,out var ScreenX,out var ScreenY)){
            return [];
        }

        const int Segments=8;
        var Center=ToWorld(ScreenX,ScreenY,InverseVP,ViewportWidth,ViewportHeight);
        List<GizmoOverlayVertex>Vertices=[];
        for(var Segment=0;Segment<Segments;Segment++){
            var AngleA=(float)(Segment*Math.PI*2d/Segments);
            var AngleB=(float)((Segment+1)*Math.PI*2d/Segments);
            var PointA=ToWorld(
                               ScreenX+(DotPixelRadius*(float)Math.Cos(AngleA)),ScreenY+(DotPixelRadius*(float)Math.Sin(AngleA)),
                               InverseVP,ViewportWidth,ViewportHeight);
            var PointB=ToWorld(
                               ScreenX+(DotPixelRadius*(float)Math.Cos(AngleB)),ScreenY+(DotPixelRadius*(float)Math.Sin(AngleB)),
                               InverseVP,ViewportWidth,ViewportHeight);
            Vertices.Add(new GizmoOverlayVertex(Center,Color));
            Vertices.Add(new GizmoOverlayVertex(PointA,Color));
            Vertices.Add(new GizmoOverlayVertex(PointB,Color));
        }
        return Vertices;
    }

    private static void AddDashedWorldLine(
        List<GizmoOverlayVertex>Lines,Vector3 From,Vector3 To,Vector4 Color){
        var Delta=To-From;
        if(Delta.LengthSquared()<=1e-8f){ return; }
        for(var Index=0;Index<DashCount;Index+=2){
            var T0=(float)Index/DashCount;
            var T1=(float)Math.Min(Index+1,DashCount)/DashCount;
            Lines.Add(new GizmoOverlayVertex(From+(Delta*T0),Color));
            Lines.Add(new GizmoOverlayVertex(From+(Delta*T1),Color));
        }
    }

    private static Vector3 RotateAroundNormal(Vector3 Direction,Vector3 First,Vector3 Second,float Angle){
        var U=Vector3.Dot(Direction,First);
        var V=Vector3.Dot(Direction,Second);
        var Cos=(float)Math.Cos(Angle);
        var Sin=(float)Math.Sin(Angle);
        return(First*((U*Cos)-(V*Sin)))+(Second*((U*Sin)+(V*Cos)));
    }

    private static Vector3 ToWorld(
        float ScreenX,float ScreenY,Matrix4x4 InverseViewProjection,float ViewportWidth,float ViewportHeight){
        (var Origin,var Direction)=SceneViewCamera.ScreenPointToRay(
                                                                    InverseViewProjection,ScreenX,ScreenY,ViewportWidth,ViewportHeight);
        return Origin+(Direction*NearPush);
    }

    /// <summary>
    /// M0037: ドラッグ角度の折り返し(<c>Atan2</c>由来の(-π,π])を跨いでも符号反転せず連続的に
    /// 累積するための純粋関数。<c>PreviousRaw</c>/<c>CurrentRaw</c>は同じ(-π,π]の生の値。
    /// </summary>
    internal static float AccumulateAngle(float Accumulated,float PreviousRaw,float CurrentRaw){
        var Diff=CurrentRaw-PreviousRaw;
        while(Diff>(float)Math.PI){ Diff-=(float)(Math.PI*2d); }
        while(Diff<-(float)Math.PI){ Diff+=(float)(Math.PI*2d); }
        return Accumulated+Diff;
    }
}

/// <summary>M0037: <see cref="SceneHwndHost.UpdateTransformGizmoDrag"/>が毎ティック更新する、HUD描画に必要な状態。</summary>
internal readonly struct GizmoDragHudState(
    SceneActiveTool Tool,GizmoHandleId Handle,Vector3 Pivot,Vector3 StartPivot,Vector3 CurrentPivot,
    Vector3 RotateNormal,Vector3 RotateStartDirection,float RotateAccumulatedRadians,float RingRadius,
    Vector3 ScaleFactors,float WorldScale,int CursorX,int CursorY){
    internal SceneActiveTool Tool { get; }=Tool;
    internal GizmoHandleId Handle { get; }=Handle;
    internal Vector3 Pivot { get; }=Pivot;
    internal Vector3 StartPivot { get; }=StartPivot;
    internal Vector3 CurrentPivot { get; }=CurrentPivot;
    internal Vector3 RotateNormal { get; }=RotateNormal;
    internal Vector3 RotateStartDirection { get; }=RotateStartDirection;
    internal float RotateAccumulatedRadians { get; }=RotateAccumulatedRadians;
    internal float RingRadius { get; }=RingRadius;
    internal Vector3 ScaleFactors { get; }=ScaleFactors;
    internal float WorldScale { get; }=WorldScale;
    internal int CursorX { get; }=CursorX;
    internal int CursorY { get; }=CursorY;
}
