using System.Numerics;
using System.Text;
namespace CSharpGameEngine.VSExtension.Tests;
/// <summary>
/// 見えているMove矢印は掴めなければならない(M0098 B)。描く三角形(<see cref="TransformGizmo.BuildMoveOverlay"/>)と
/// 判定(<see cref="TransformGizmo.TryHitMove"/>・<see cref="TransformGizmo.TryHitTransform"/>)を、外から比べる。
/// </summary>
public sealed class GizmoVisibleHitConsistencyTests {
    private const float Fov=0.7853982f;
    private static readonly Vector3 Pivot=new(3f,0.5f,0f);
    // M0096の実機の面(1078x839)を含む。
    private static readonly (int Width,int Height)[]Surfaces=[(800,600),(1280,720),(640,640),(1078,839)];
    private static readonly Vector3[]InteriorWeights=[new(0.98f,0.01f,0.01f),new(0.01f,0.98f,0.01f),new(0.01f,0.01f,0.98f),new(1f/3f,1f/3f,1f/3f)];
    /// <summary>
    /// 画面の画素で: 描いた矢印が覆う画素はすべて、アプリと同じ経路(<see cref="SceneViewCamera.ScreenPointToRay"/>→
    /// <see cref="TransformGizmo.TryHitTransform"/>)で何かのハンドルに当たる。PrimitiveShowcaseBoxSceneの既定のフレーミングと、
    /// yaw/pitchの全周で測る。
    /// </summary>
    [Fact]
    public void EveryDrawnMoveArrowPixel_HitsAHandleThroughTheScenePath(){
        var Report=new StringBuilder();
        var Total=0;
        var Empty=0;
        foreach(var (Width,Height) in Surfaces){
            foreach(var (Name,State) in Views(Width/(float)Height)){
                var (Covered,Missed,First)=Measure(State,Width,Height);
                Total+=Covered;
                Empty+=Missed;
                if(Missed>0){
                    Report.AppendLine($"{Width}x{Height} {Name}: {Missed}/{Covered} drawn pixels hit nothing, first {First}");
                }
            }
        }
        Assert.True(Total>0);
        Assert.True(Empty==0,$"{Empty}/{Total} drawn Move-arrow pixels hit nothing.{Environment.NewLine}{Report}");
    }
    /// <summary>
    /// 解像度によらず: 描いた矢印の三角形の内側の点を通るレイは、どの方向(軸に厳密に平行な方向を含む)からでも
    /// <see cref="TransformGizmo.TryHitMove"/>に当たる。その点は軸の線分から描いた半径以内にあり、レイと線分の距離はそれ以下になるから。
    /// </summary>
    [Fact]
    public void EveryRayThroughTheDrawnMoveArrow_HitsTheMoveGizmo(){
        var Frame=MoveGizmoFrame.Create(Vector3.Zero,Quaternion.Identity,SceneTransformSpace.World,1f);
        var Report=new StringBuilder();
        var Total=0;
        var Missed=0;
        foreach(var (Handle,Vertices) in ArrowSolids(Frame)){
            for(var Index=0;Index+2<Vertices.Count;Index+=3){
                foreach(var Weights in InteriorWeights){
                    var Point=(Vertices[Index].Position*Weights.X)+(Vertices[Index+1].Position*Weights.Y)+(Vertices[Index+2].Position*Weights.Z);
                    foreach(var Direction in Directions()){
                        Total++;
                        if(TransformGizmo.TryHitMove(Frame,Point-(Direction*10f),Direction,out _) is null){
                            Missed++;
                            if(Missed<=5){
                                Report.AppendLine($"{Handle} point {Point} direction {Direction}");
                            }
                        }
                    }
                }
            }
        }
        Assert.True(Total>0);
        Assert.True(Missed==0,$"{Missed}/{Total} rays through the drawn Move arrows missed.{Environment.NewLine}{Report}");
    }
    private static IEnumerable<(string Name,SceneViewState State)>Views(float Aspect){
        var Framed=new SceneViewState();
        Framed.ResetToBounds(Pivot,0.866f,SceneCameraFraming.ViewDirection,Fov,Aspect);
        yield return("default framing",Framed);
        for(var YawDegrees=0;YawDegrees<360;YawDegrees+=30){
            foreach(var PitchDegrees in new[]{-60,-35,-10,10,30}){
                var State=new SceneViewState { FocusPosition=Pivot,Yaw=YawDegrees*FloatMath.Pi/180f,Pitch=PitchDegrees*FloatMath.Pi/180f,FocusDistance=4f,FovYRadians=Fov,LastKnownBoundsRadius=0.866f };
                yield return($"yaw {YawDegrees} pitch {PitchDegrees}",State);
            }
        }
    }
    /// <summary>描いた矢印の立体(軸に沿う三角形の集まり)。平面ハンドルの四角は軸から離れているので含まない。</summary>
    private static IEnumerable<(GizmoHandleId Handle,IReadOnlyList<GizmoOverlayVertex>Vertices)>ArrowSolids(MoveGizmoFrame Frame){
        var Found=0;
        foreach(var Primitive in TransformGizmo.BuildMoveOverlay(Frame,null,null)){
            if(Primitive.Kind!=GizmoPrimitiveKind.Triangle){
                continue;
            }
            foreach(var Handle in new[]{GizmoHandleId.AxisX,GizmoHandleId.AxisY,GizmoHandleId.AxisZ}){
                var Axis=Frame.GetAxis(Handle);
                if(Primitive.Vertices.All(Vertex=>Vector3.Cross(Vertex.Position-Frame.Pivot,Axis).Length()<=0.1f*Frame.WorldScale)){
                    Found++;
                    yield return(Handle,Primitive.Vertices);
                }
            }
        }
        Assert.Equal(3,Found);
    }
    /// <summary>球面上にほぼ均等な方向(フィボナッチ格子)と、軸に厳密に平行な6方向。</summary>
    private static IEnumerable<Vector3>Directions(){
        const int Count=512;
        for(var Index=0;Index<Count;Index++){
            var Y=1f-(2f*(Index+0.5f)/Count);
            var Radius=FloatMath.Sqrt(1f-(Y*Y));
            var Angle=Index*2.3999632f;
            yield return new Vector3(Radius*FloatMath.Cos(Angle),Y,Radius*FloatMath.Sin(Angle));
        }
        foreach(var Axis in new[]{Vector3.UnitX,Vector3.UnitY,Vector3.UnitZ}){
            yield return Axis;
            yield return -Axis;
        }
    }
    private static(int Covered,int Missed,string First)Measure(SceneViewState State,int Width,int Height){
        var ViewProjection=State.GetView()*State.GetProjection(Width/(float)Height);
        Matrix4x4.Invert(ViewProjection,out var Inverse);
        var Scale=TransformGizmo.ComputeWorldScale(SceneProjectionMode.Perspective,Vector3.Distance(Pivot,State.GetEyePosition()),State.FovYRadians,State.OrthographicSize);
        var Frame=MoveGizmoFrame.Create(Pivot,Quaternion.Identity,SceneTransformSpace.World,Scale);
        var Forward=SceneViewCamera.GetForward(State.Yaw,State.Pitch);
        var Right=SceneViewCamera.GetFlatRight(State.Yaw);
        var Up=SceneViewCamera.GetUp(State.Yaw,State.Pitch);
        var Covered=0;
        var Missed=0;
        var First=string.Empty;
        foreach(var (Handle,Vertices) in ArrowSolids(Frame)){
            var Pixels=new HashSet<(int X,int Y)>();
            for(var Index=0;Index+2<Vertices.Count;Index+=3){
                Rasterize(Vertices[Index].Position,Vertices[Index+1].Position,Vertices[Index+2].Position,ViewProjection,Width,Height,Pixels);
            }
            foreach(var (X,Y) in Pixels){
                Covered++;
                var (Origin,Direction)=SceneViewCamera.ScreenPointToRay(Inverse,X+0.5f,Y+0.5f,Width,Height);
                if(TransformGizmo.TryHitTransform(Frame,Origin,Direction,Forward,Right,Up) is null){
                    Missed++;
                    if(First.Length==0){
                        First=$"{Handle} at pixel ({X},{Y})";
                    }
                }
            }
        }
        return(Covered,Missed,First);
    }
    private static void Rasterize(Vector3 A,Vector3 B,Vector3 C,Matrix4x4 ViewProjection,int Width,int Height,HashSet<(int X,int Y)>Pixels){
        if(!ToScreen(A,ViewProjection,Width,Height,out var P0)||!ToScreen(B,ViewProjection,Width,Height,out var P1)||!ToScreen(C,ViewProjection,Width,Height,out var P2)){
            return;
        }
        var MinX=Math.Max(0,(int)Math.Floor(Math.Min(P0.X,Math.Min(P1.X,P2.X))));
        var MaxX=Math.Min(Width-1,(int)Math.Ceiling(Math.Max(P0.X,Math.Max(P1.X,P2.X))));
        var MinY=Math.Max(0,(int)Math.Floor(Math.Min(P0.Y,Math.Min(P1.Y,P2.Y))));
        var MaxY=Math.Min(Height-1,(int)Math.Ceiling(Math.Max(P0.Y,Math.Max(P1.Y,P2.Y))));
        var Area=Edge(P0,P1,P2);
        if(Math.Abs(Area)<1e-4f){
            return;
        }
        for(var Y=MinY;Y<=MaxY;Y++){
            for(var X=MinX;X<=MaxX;X++){
                var P=new Vector2(X+0.5f,Y+0.5f);
                var W0=Edge(P1,P2,P)/Area;
                var W1=Edge(P2,P0,P)/Area;
                var W2=Edge(P0,P1,P)/Area;
                if(W0>=0f&&W1>=0f&&W2>=0f){
                    Pixels.Add((X,Y));
                }
            }
        }
    }
    private static float Edge(Vector2 A,Vector2 B,Vector2 C)=>((C.X-A.X)*(B.Y-A.Y))-((C.Y-A.Y)*(B.X-A.X));
    private static bool ToScreen(Vector3 World,Matrix4x4 ViewProjection,int Width,int Height,out Vector2 Screen){
        var Clip=Vector4.Transform(new Vector4(World,1f),ViewProjection);
        if(Clip.W<=1e-4f){
            Screen=default;
            return false;
        }
        Screen=new Vector2((((Clip.X/Clip.W)*0.5f)+0.5f)*Width,(0.5f-((Clip.Y/Clip.W)*0.5f))*Height);
        return true;
    }
}
