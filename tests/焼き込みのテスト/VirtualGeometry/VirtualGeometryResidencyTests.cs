using Xunit;
using System.Numerics;
using CSharpGameEngine.Content.VirtualGeometry;
using CSharpGameEngine.Cooking.VirtualGeometry;
using CSharpGameEngine.Rendering.Pipeline;
using CSharpGameEngine.Rendering.Streaming;
namespace CSharpGameEngine.Cooking.Tests.VirtualGeometry;
/// <summary>
/// 仮想ジオメトリのページの出し入れ（M0131 単位 4）。ビルドした球で、GPU の選択と同じ規則（「十分」か「準備済みでない」なら細かくしない）で塊を選び、
/// 描いたページと要求を出し入れの制御へ返す、を CPU でフレームごとに回す。どのフレームでも、選んだ塊は割れ目が無く、すべて置き場にあり、
/// 追い出した枠と区画は決めたフレーム数より早く使い直されない。
/// </summary>
public sealed class VirtualGeometryResidencyTests {
    private const float ThresholdPixels=1f;
    private const int Delay=2;
    private static readonly ScreenSizeMetric Metric=ScreenSizeMetric.Perspective(1f,1080,0.01f);
    [Fact]
    public void AFullPoolPreloadsEveryPageAndDrawsTheUnstreamedCut(){
        var (Mesh,Layout,Clusters)=Build();
        var Residency=new VirtualGeometryResidency(Layout,Clusters,Mesh.Groups.Length,Layout.ClusterCount,Layout.TotalBlockCount,Delay);
        List<int>Loads=[];
        Residency.Preload(Loads);
        Assert.Equal(Mesh.Groups.Length,Loads.Count);
        Assert.Equal(0,Residency.FreeSlotCount);
        foreach(var Camera in new[]{ new Vector3(0f,0f,1.5f),new Vector3(4f,1f,0f),new Vector3(0f,-30f,0f) }){
            var Streamed=Select(Mesh,Residency,Camera,out _);
            var Unstreamed=Mesh.Clusters.Where(Cluster=>Enough(Mesh,Cluster.Group,Camera)&&(Cluster.ParentGroup<0||!Enough(Mesh,Cluster.ParentGroup,Camera))).ToArray();
            Assert.Equal(Unstreamed,Streamed);
        }
    }
    /// <summary>根のページだけから始め、1 フレームに 6 ページまで置いて、全部を置いたときと同じ塊に追いつく。途中のどのフレームも割れ目が無い。</summary>
    [Fact]
    public void StartingFromTheRootCatchesUpWithoutCracks(){
        var (Mesh,Layout,Clusters)=Build();
        var Residency=new VirtualGeometryResidency(Layout,Clusters,Mesh.Groups.Length,Layout.ClusterCount,Layout.TotalBlockCount,Delay);
        List<int>Loads=[];
        Residency.Preload(Loads,MaximumPages:1);
        Assert.Single(Loads);
        var Camera=new Vector3(0f,0.3f,1.6f);
        var Expected=Mesh.Clusters.Where(Cluster=>Enough(Mesh,Cluster.Group,Camera)&&(Cluster.ParentGroup<0||!Enough(Mesh,Cluster.ParentGroup,Camera))).ToArray();
        var Report=new StreamingReport();
        var CaughtUp=-1;
        for(var Frame=1;Frame<=60&&CaughtUp<0;Frame++){
            var Selected=Step(Mesh,Residency,Camera,(ulong)Frame,PageBudget:6,Report);
            if(Selected.SequenceEqual(Expected)){ CaughtUp=Frame; }
        }
        Assert.True(CaughtUp>0,"the streamed cut never caught up with the full cut in 60 frames");
        Assert.True(CaughtUp>1,"the cut was complete before anything was streamed");
        Assert.Equal(0,Report.DeniedFull);
    }
    /// <summary>置き場が全体の 4 割しかなくても、カメラが動くあいだ割れ目は無く、選んだ塊は置き場にある。足りない分は理由を報告し、追い出した枠と区画を早く使い直さない。</summary>
    [Fact]
    public void ASmallPoolStaysWatertightWhileTheCameraMoves(){
        var (Mesh,Layout,Clusters)=Build();
        var Residency=new VirtualGeometryResidency(Layout,Clusters,Mesh.Groups.Length,Layout.ClusterCount*2/5,Layout.TotalBlockCount*2/5,Delay);
        Residency.Preload([]);
        var Report=new StreamingReport();
        var Owner=new Dictionary<int,(int Cluster,ulong Since)>();
        var Released=new Dictionary<int,ulong>();
        for(var Frame=1;Frame<=120;Frame++){
            // 回りながら近づいたり離れたりする（選択は球の裏も描くので、使わなくなるページは、離れて細かさが要らなくなったときに出る）。
            var Angle=Frame*0.11f;
            var Distance=MathF.Exp(MathF.Log(1.5f)+(MathF.Log(80f)-MathF.Log(1.5f))*(0.5f+0.5f*MathF.Sin(Frame*0.13f)));
            var Camera=new Vector3(MathF.Cos(Angle),MathF.Sin(Angle*0.7f)*0.4f,MathF.Sin(Angle))*Distance;
            _=Step(Mesh,Residency,Camera,(ulong)Frame,PageBudget:8,Report);
            // 枠を持つ塊を記録し、持ち主が変わった枠は、前の持ち主を追い出してから決めたフレーム数より後でなければならない。
            var Current=new Dictionary<int,int>();
            for(var Cluster=0;Cluster<Layout.ClusterCount;Cluster++){
                if(Residency.TryGetPlacement(Cluster,out var Slot,out _)){ Current.Add(Slot,Cluster); }
            }
            foreach(var (Slot,(Cluster,_)) in Owner.ToArray()){
                if(!Current.TryGetValue(Slot,out var Now)||Now!=Cluster){
                    Released.TryAdd(Slot,(ulong)Frame);
                    Owner.Remove(Slot);
                }
            }
            foreach(var (Slot,Cluster) in Current){
                if(Owner.ContainsKey(Slot)){ continue; }
                if(Released.Remove(Slot,out var Freed)){ Assert.True((ulong)Frame>=Freed+Delay-1,$"slot {Slot} was reused at frame {Frame}, freed at {Freed}"); }
                Owner.Add(Slot,(Cluster,(ulong)Frame));
            }
        }
        Assert.True(Report.Evicted>0,$"a pool of two fifths never evicted a page (requested {Report.Requested}, resident {Report.AlreadyResident}, allocated {Report.Allocated}, denied full {Report.DeniedFull}, denied by budget {Report.DeniedByBudget}, resident pages {Residency.ResidentPageCount} of {Residency.GroupCount}, free slots {Residency.FreeSlotCount}, free blocks {Residency.FreeBlockCount})");
        Assert.True(Residency.IsResident(Mesh.Clusters.First(static Cluster=>Cluster.ParentGroup<0).Group),"the root page was evicted");
    }
    /// <summary>準備済みの印は、入力のページがすべて置かれていて、上のまとまりも準備済みのときだけ立つ。葉のまとまりには立たない。</summary>
    [Fact]
    public void ReadyNeedsTheInputsAndEveryConsumer(){
        var (Mesh,Layout,Clusters)=Build();
        var Residency=new VirtualGeometryResidency(Layout,Clusters,Mesh.Groups.Length,Layout.ClusterCount/2,Layout.TotalBlockCount/2,Delay);
        Residency.Preload([]);
        for(var Group=0;Group<Mesh.Groups.Length;Group++){
            if(Residency.Ready[Group]==0){ continue; }
            Assert.NotEmpty(Residency.InputPages(Group).ToArray());
            foreach(var Input in Residency.InputPages(Group)){ Assert.True(Residency.IsResident(Input),$"group {Group} is ready but its input page {Input} is not resident"); }
            foreach(var Cluster in Mesh.Clusters.Where(Cluster=>Cluster.Group==Group&&Cluster.ParentGroup>=0)){
                Assert.Equal(1u,Residency.Ready[Cluster.ParentGroup]);
            }
        }
        Assert.Contains(0u,Residency.Ready.ToArray());
    }
    [Fact]
    public void APoolTooSmallForTheRootIsRefused(){
        var (Mesh,Layout,Clusters)=Build();
        Assert.Throws<ArgumentException>(()=>new VirtualGeometryResidency(Layout,Clusters,Mesh.Groups.Length,1,1,Delay));
    }
    /// <summary>GPU の選択と同じ規則で 1 フレーム選び、割れ目が無く置き場にあることを確かめ、描いたページと要求を返して出し入れする。</summary>
    private static VirtualGeometryCluster[]Step(VirtualGeometryMesh Mesh,VirtualGeometryResidency Residency,Vector3 Camera,ulong Frame,int PageBudget,StreamingReport Report){
        var Selected=Select(Mesh,Residency,Camera,out var Requests);
        VirtualGeometryBuilderTests.AssertWatertight(Mesh,Selected,$"frame {Frame}");
        var Used=Selected.Select(static Cluster=>Cluster.Group).Distinct().ToArray();
        Residency.Update(Frame,Used,Requests,PageBudget,[],Report);
        return Selected;
    }
    private static VirtualGeometryCluster[]Select(VirtualGeometryMesh Mesh,VirtualGeometryResidency Residency,Vector3 Camera,out (int Group,float Priority)[]Requests){
        bool Stops(int Group)=>Enough(Mesh,Group,Camera)||Residency.Ready[Group]==0;
        List<VirtualGeometryCluster>Selected=[];
        var Wanted=new Dictionary<int,float>();
        for(var Index=0;Index<Mesh.Clusters.Length;Index++){
            var Cluster=Mesh.Clusters[Index];
            if(!Stops(Cluster.Group)||Cluster.ParentGroup>=0&&Stops(Cluster.ParentGroup)){ continue; }
            Assert.True(Residency.TryGetPlacement(Index,out _,out _),$"cluster {Index} is drawn but not resident");
            Selected.Add(Cluster);
            if(!Enough(Mesh,Cluster.Group,Camera)){ Wanted[Cluster.Group]=Pixels(Mesh,Cluster.Group,Camera); }
        }
        Requests=[..Wanted.Select(static Pair=>(Pair.Key,Pair.Value))];
        return [..Selected];
    }
    private static bool Enough(VirtualGeometryMesh Mesh,int Group,Vector3 Camera)=>Pixels(Mesh,Group,Camera)<=ThresholdPixels;
    private static float Pixels(VirtualGeometryMesh Mesh,int Group,Vector3 Camera){
        var G=Mesh.Groups[Group];
        return Metric.ProjectedPixels(G.Error,MathF.Max((G.BoundsCentre-Camera).Length()-G.BoundsRadius,Metric.MinimumDistanceMeters));
    }
    private static (VirtualGeometryMesh Mesh,VirtualGeometrySlotLayout Layout,StageVirtualGeometryCluster[]Clusters)Build(){
        var (Vertices,Indices)=VirtualGeometryBuilderTests.Sphere(40,80);
        var Mesh=VirtualGeometryBuilder.Build(Vertices,Indices);
        var Clusters=Mesh.Clusters.Select(static Cluster=>new StageVirtualGeometryCluster { FirstIndex=(uint)Cluster.FirstIndex,IndexCount=(uint)Cluster.IndexCount,Group=(uint)Cluster.Group,ParentGroup=Cluster.ParentGroup }).ToArray();
        var Info=new StageVirtualGeometryCreateInfo {
            Vertices=[..Mesh.Vertices.Select(static Vertex=>new StageVirtualGeometryVertex { Position=Vertex.Position,Normal=Vertex.Normal,Tangent=Vertex.Tangent,Uv=Vertex.Uv })],
            Indices=Mesh.Indices,
            Clusters=Clusters,
            Groups=[..Mesh.Groups.Select(static _=>new StageVirtualGeometryGroup())],
        };
        return (Mesh,VirtualGeometrySlotLayout.Create(Info),Clusters);
    }
}
