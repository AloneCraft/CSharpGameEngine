using Xunit;
using System.Numerics;
namespace CSharpGameEngine.Rendering.Tests;
/// <summary>
/// 仮想シャドウの計画（M0132 単位 1）。実行時と同じく描画空間の原点をカメラに置き、格子の区画の中の位置をカメラの世界の位置にして、
/// カメラだけが動くと作ったページを作り直さない、動いた物の範囲のページだけが古くなる、1 フレームの上限と置き場の数を守る、
/// 同じ入力で同じ計画、ページ表と段の行列がシェーダーと同じ引き方で一致する、を確かめる。
/// </summary>
public sealed class VirtualShadowMapPlannerTests {
    private static readonly Vector3 TowardLight=Vector3.Normalize(new Vector3(0.35f,0.8f,-0.45f));
    private static readonly VirtualShadowBounds[]WorldObjects=[
        new(0,new Vector3(-1f,0f,-1f),new Vector3(1f,2f,1f),true,true),
        new(1,new Vector3(6f,0f,3f),new Vector3(7f,4f,5f),true,true),
        new(2,new Vector3(-30f,-0.1f,-30f),new Vector3(30f,0f,30f),true,true),
    ];
    [Fact]
    public void MovingOnlyTheCameraRendersNoPageAgain(){
        var Planner=new VirtualShadowMapPlanner();
        var Rendered=new HashSet<VirtualShadowPageKey>();
        var First=Frame(Planner,new Vector3(2f,1.7f,-3f),WorldObjects);
        Assert.True(First.Count>VirtualShadowMapPlanner.DefaultRenderBudget,$"the first frame rendered {First.Count} pages; everything was missing, so the budget should be lifted");
        Rendered.UnionWith(First);
        // 同じ所に止まっていれば、何も作らない。
        Assert.Empty(Frame(Planner,new Vector3(2f,1.7f,-3f),WorldObjects));
        // 区画の中で動く（描画空間の位置はカメラからの相対なので、全部の物の行列が変わる）。前に作ったページは作り直さない。
        foreach(var Camera in new[]{ new Vector3(2.013f,1.7f,-3.007f),new Vector3(2.4f,1.75f,-2.6f),new Vector3(9.3f,2f,4.1f) }){
            var Pages=Frame(Planner,Camera,WorldObjects);
            Assert.Equal(0,Planner.Statistics.Invalidated);
            Assert.DoesNotContain(Pages,Rendered.Contains);
            Rendered.UnionWith(Pages);
        }
    }
    [Fact]
    public void AMovingObjectDirtiesOnlyThePagesItOverlaps(){
        var Planner=new VirtualShadowMapPlanner();
        var Camera=new Vector3(0f,1.7f,-4f);
        _=Frame(Planner,Camera,WorldObjects);
        _=Frame(Planner,Camera,WorldObjects);
        var Moved=WorldObjects.Select(static Object=>Object.Id==1?Object with{ Minimum=Object.Minimum+new Vector3(0.5f,0f,0f),Maximum=Object.Maximum+new Vector3(0.5f,0f,0f) }:Object).ToArray();
        var Pages=Frame(Planner,Camera,Moved);
        Assert.True(Planner.Statistics.Invalidated>0,"moving a box did not dirty any page");
        Assert.NotEmpty(Pages);
        // 作り直したページは、どれも前か今の箱の光の範囲に重なる。
        var Light=LightBasis();
        foreach(var Key in Pages){
            var Width=VirtualShadowMapPlanner.PageWidthMeters(Key.Level);
            var Overlaps=new[]{ WorldObjects[1],Moved[1] }.Any(Object=>{
                var (Low,High)=Box(Object,Light);
                return Key.X*Width<=High.X&&(Key.X+1)*Width>=Low.X&&Key.Y*Width<=High.Y&&(Key.Y+1)*Width>=Low.Y;
            });
            Assert.True(Overlaps,$"page {Key} was rendered again but does not overlap the moved box");
        }
        // 1 mm 以下の揺れでは古くならない。
        var Jitter=Moved.Select(static Object=>Object.Id==1?Object with{ Minimum=Object.Minimum+new Vector3(0.0004f,0f,0f),Maximum=Object.Maximum+new Vector3(0.0004f,0f,0f) }:Object).ToArray();
        _=Frame(Planner,Camera,Jitter);
        Assert.Equal(0,Planner.Statistics.Invalidated);
    }
    [Fact]
    public void TheBudgetLimitsANormalFrameAndThePoolIsNeverExceeded(){
        var Planner=new VirtualShadowMapPlanner();
        _=Frame(Planner,new Vector3(0f,1.7f,0f),WorldObjects);
        // 少しだけ動いて要求の大半が揃っているフレームは、上限で止まる。
        var Pages=Frame(Planner,new Vector3(0f,1.7f,0f),WorldObjects,RadiusMeters:90f);
        Assert.InRange(Pages.Count,0,VirtualShadowMapPlanner.DefaultRenderBudget);
        // 置き場が小さいと、置き場の数を超えず、入らない要求を断ったと報告する。
        var Small=new VirtualShadowMapPlanner();
        _=Frame(Small,new Vector3(0f,1.7f,0f),WorldObjects,PhysicalPages:40);
        Assert.InRange(Small.Statistics.Resident,1,40);
        Assert.True(Small.Report.DeniedFull>0,"a pool of 40 pages never reported being full");
        Assert.True(Small.PageTable.ToArray().All(static Entry=>Entry<=40));
    }
    [Fact]
    public void ChangingTheLightOrTheCellRendersEverythingAgain(){
        var Planner=new VirtualShadowMapPlanner();
        var Camera=new Vector3(0f,1.7f,0f);
        var First=Frame(Planner,Camera,WorldObjects);
        Assert.Empty(Frame(Planner,Camera,WorldObjects));
        Assert.NotEmpty(First);
        // 光が 0.01° を超えて変わると、要求したページを全部作り直す（使い回しは 0）。
        var Turned=Vector3.Normalize(TowardLight+new Vector3(0.01f,0f,0f));
        _=Frame(Planner,Camera,WorldObjects,Light:Turned);
        Assert.Equal(0,Planner.Statistics.Reused);
        Assert.Equal(Planner.Statistics.Resident,Planner.Statistics.Rendered);
        Assert.Empty(Frame(Planner,Camera,WorldObjects,Light:Turned));
        // 区画が変わっても全部作り直す。
        _=Frame(Planner,Camera,WorldObjects,Light:Turned,CellX:1);
        Assert.Equal(0,Planner.Statistics.Reused);
        Assert.Equal(Planner.Statistics.Resident,Planner.Statistics.Rendered);
    }
    [Fact]
    public void TheSameInputGivesTheSamePlan(){
        var A=new VirtualShadowMapPlanner();
        var B=new VirtualShadowMapPlanner();
        foreach(var Camera in new[]{ new Vector3(0f,1.7f,0f),new Vector3(3f,1.7f,1f) }){
            Assert.Equal(Frame(A,Camera,WorldObjects),Frame(B,Camera,WorldObjects));
            Assert.Equal(A.PagesToRender.Select(static Page=>(Page.Key,Page.PhysicalIndex)),B.PagesToRender.Select(static Page=>(Page.Key,Page.PhysicalIndex)));
            Assert.True(A.PageTable.SequenceEqual(B.PageTable));
        }
    }
    /// <summary>段の行列で窓の中の位置を求め、ページ表を引く（シェーダーと同じ引き方）と、計画のページの場所と一致する。ページの行列はその点を範囲の中へ写す。</summary>
    [Fact]
    public void TheLevelMatricesAndThePageTableAgreeWithThePageKeys(){
        var Planner=new VirtualShadowMapPlanner();
        var Anchor=new VirtualShadowAnchor(0,0,0,new Vector3(317.25f,12f,-80.5f));
        Planner.Plan(Anchor,Vector3.Zero,TowardLight,[],[],VirtualShadowMapPlanner.MaximumPhysicalPages);
        var Random=new Random(132);
        for(var Case=0;Case<2000;Case++){
            var Level=Random.Next(VirtualShadowMapPlanner.LevelCount);
            var Reach=VirtualShadowMapPlanner.LevelWidthMeters(Level)*0.45f;
            var Point=new Vector3(((float)Random.NextDouble()*2f-1f)*Reach,((float)Random.NextDouble()*2f-1f)*Reach*0.2f,((float)Random.NextDouble()*2f-1f)*Reach);
            var Key=Planner.PageOf(Level,Point,Anchor);
            var Index=Planner.TableIndex(Key);
            if(Index<0){ continue; }
            var Clip=Vector4.Transform(new Vector4(Point,1f),Planner.Projections[Level]);
            var U=(Clip.X*0.5f+0.5f)*VirtualShadowMapPlanner.VirtualTexels;
            var V=(0.5f-Clip.Y*0.5f)*VirtualShadowMapPlanner.VirtualTexels;
            var PageX=(int)MathF.Floor(U/VirtualShadowMapPlanner.PageTexels);
            var PageY=(int)MathF.Floor(V/VirtualShadowMapPlanner.PageTexels);
            // 境目のちょうど上の点は、丸めで隣になってよい。
            if(MathF.Abs(U/VirtualShadowMapPlanner.PageTexels-MathF.Round(U/VirtualShadowMapPlanner.PageTexels))<1e-3f||MathF.Abs(V/VirtualShadowMapPlanner.PageTexels-MathF.Round(V/VirtualShadowMapPlanner.PageTexels))<1e-3f){ continue; }
            Assert.Equal(Index,(Level*VirtualShadowMapPlanner.PagesPerAxis+PageY)*VirtualShadowMapPlanner.PagesPerAxis+PageX);
            Assert.InRange(Clip.Z,0f,1f);
        }
    }
    /// <summary>
    /// 世界の物と、地面に置いた点から作った要求で 1 フレームを計画する。描画空間はカメラからの相対で、格子の区画の中の位置はカメラの世界の位置。
    /// 要求の段は、点までの距離で決める（段 L の 1 テクセルが、距離 × 1 画素の角度（1/1000）に近くなる段）。
    /// </summary>
    private static List<VirtualShadowPageKey>Frame(VirtualShadowMapPlanner Planner,Vector3 Camera,VirtualShadowBounds[]Objects,float RadiusMeters=60f,Vector3?Light=null,int PhysicalPages=VirtualShadowMapPlanner.MaximumPhysicalPages,long CellX=0){
        var Anchor=new VirtualShadowAnchor(CellX,0,0,Camera);
        var Drawing=Objects.Select(Object=>Object with{ Minimum=Object.Minimum-Camera,Maximum=Object.Maximum-Camera }).ToArray();
        var Toward=Light??TowardLight;
        var Requests=new HashSet<VirtualShadowPageKey>();
        for(var Z=-RadiusMeters;Z<=RadiusMeters;Z+=1.5f){
            for(var X=-RadiusMeters;X<=RadiusMeters;X+=1.5f){
                var World=new Vector3(MathF.Round(Camera.X)+X,0f,MathF.Round(Camera.Z)+Z);
                var Distance=MathF.Max(Vector3.Distance(World,Camera),1f);
                var Level=Math.Clamp((int)MathF.Ceiling(MathF.Log2(Distance*0.001f/(VirtualShadowMapPlanner.LevelWidthMeters(0)/VirtualShadowMapPlanner.VirtualTexels))),0,VirtualShadowMapPlanner.LevelCount-1);
                Requests.Add(VirtualShadowMapPlanner.PageOf(Level,World-Camera,Anchor,Toward));
            }
        }
        Planner.Plan(Anchor,Vector3.Zero,Toward,Drawing,[..Requests],PhysicalPages);
        return [..Planner.PagesToRender.Select(static Page=>Page.Key)];
    }
    private static Matrix4x4 LightBasis()=>Matrix4x4.CreateLookAt(Vector3.Zero,-TowardLight,Vector3.UnitY);
    private static (Vector3 Low,Vector3 High)Box(VirtualShadowBounds Object,Matrix4x4 Light){
        var Low=new Vector3(float.PositiveInfinity);
        var High=new Vector3(float.NegativeInfinity);
        for(var Corner=0;Corner<8;Corner++){
            var Point=Vector3.TransformNormal(new Vector3((Corner&1)==0?Object.Minimum.X:Object.Maximum.X,(Corner&2)==0?Object.Minimum.Y:Object.Maximum.Y,(Corner&4)==0?Object.Minimum.Z:Object.Maximum.Z),Light);
            Low=Vector3.Min(Low,Point);
            High=Vector3.Max(High,Point);
        }
        return (Low,High);
    }
}
