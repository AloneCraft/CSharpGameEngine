using System.Numerics;
using CSharpGameEngine.Runtime.Animation;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>
/// ADR-0078/M0107：テンプレートに同梱される、実際のマネキンアセット。BS_Idle_Walk_Runのすべてのクリップがこのスケルトンにバインドされる。
/// スケルトンは足IKと一人称腕が参照するジョイントを持つ。モデル空間でのレスト姿勢はUEのControl Rigの作成時と同じ
///（頭の高さ162.6 cm、上腕の間隔±19 cm）なので、数値をそのまま引き継げる。
/// </summary>
public sealed class MannequinAssetTests {
    private static string RepositoryRoot(){
        var Directory=new DirectoryInfo(AppContext.BaseDirectory);
        while(Directory is not null&&!File.Exists(Path.Combine(Directory.FullName,"CSharpGameEngine.slnx"))){ Directory=Directory.Parent; }
        return Directory?.FullName??throw new InvalidOperationException("The repository root was not found.");
    }
    private static string Asset(string Template,string Name)=>Path.Combine(RepositoryRoot(),"Templates","CSharpGameEngine.Templates."+Template,"Assets","Characters","Mannequins",Name);
    private static readonly string[]Clips=[
        "MM_Idle","MM_Jump","MM_Fall_Loop","MM_Land",
        "MF_Unarmed_Walk_Fwd","MF_Unarmed_Walk_Fwd_Left","MF_Unarmed_Walk_Fwd_Right","MF_Unarmed_Walk_Left","MF_Unarmed_Walk_Right","MF_Unarmed_Walk_Bwd","MF_Unarmed_Walk_Bwd_Left","MF_Unarmed_Walk_Bwd_Right",
        "MF_Unarmed_Jog_Fwd","MF_Unarmed_Jog_Fwd_Left","MF_Unarmed_Jog_Fwd_Right","MF_Unarmed_Jog_Left","MF_Unarmed_Jog_Right","MF_Unarmed_Jog_Bwd","MF_Unarmed_Jog_Bwd_Left","MF_Unarmed_Jog_Bwd_Right",
    ];
    private static Skeleton SkeletonOf(string Template,string Mesh)=>Skeleton.FromImported(SkeletalAssetCache.Scene(Asset(Template,Mesh)).Skins[0]);
    [Theory]
    [InlineData("ThirdPerson","SKM_Quinn_Simple.glb")]
    [InlineData("TopDown","SKM_Manny_Simple.glb")]
    [InlineData("FirstPerson","SKM_Manny_Simple.glb")]
    public void EveryClipOfTheLocomotionGraphBindsToTheSkeleton(string Template,string Mesh){
        var Skeleton=SkeletonOf(Template,Mesh);
        foreach(var Name in Clips){
            var Clip=SkeletalAssetCache.Clip(Asset(Template,Path.Combine("Anims",Name+".glb")),Skeleton);
            Assert.True(Clip.BoundTrackCount>=Skeleton.JointCount,$"{Name} animates {Clip.BoundTrackCount} of {Skeleton.JointCount} joints.");
            Assert.InRange(Clip.DurationSeconds,0.3f,10f);
        }
    }
    [Fact]
    public void TheMannequinsRestPoseIsInUnrealsControlRigFrame(){
        // インポーターはglTFのZを反射する（エンジンの座標軸は左手系）。UEのセンチメートル単位のメッシュコンポーネント座標(x, y, z)は、モデル空間のメートル(x, z, -y) / 100になる。
        var Skeleton=SkeletonOf("TopDown","SKM_Manny_Simple.glb");
        var Model=new Matrix4x4[Skeleton.JointCount];
        Skeleton.ComputeModel(Skeleton.Rest,Model);
        Vector3 Position(string Name)=>Model[Skeleton.IndexOf(Name)].Translation;
        Assert.True(Vector3.Distance(new Vector3(0f,1.6258f,-0.0066f),Position("head"))<0.002f,Position("head").ToString());
        Assert.True(Vector3.Distance(new Vector3(0.1901f,1.4358f,0.0256f),Position("upperarm_l"))<0.002f,Position("upperarm_l").ToString());
        Assert.True(Vector3.Distance(new Vector3(-0.1901f,1.4358f,0.0256f),Position("upperarm_r"))<0.002f,Position("upperarm_r").ToString());
        Assert.True(Vector3.Distance(new Vector3(0.1409f,0.0824f,0.0099f),Position("foot_l"))<0.002f,Position("foot_l").ToString());
        foreach(var Name in new[] { "pelvis","thigh_l","calf_l","foot_l","ball_l","thigh_r","calf_r","foot_r","ball_r","spine_01","spine_04","head","upperarm_l","upperarm_r" }){
            Assert.True(Skeleton.IndexOf(Name)>=0,Name);
        }
    }
    /// <summary>
    /// クリップをそのまま使うため、各クリップの移動方向は足の動きから読み取る。接地した足は骨盤に対して後ろへ動く。
    /// シーン内ではメッシュノードを上軸まわりに半回転させるため、角度で名前を付けたクリップ（前方が0、Rightクリップ側が正）はその方向へ移動する。
    /// <see cref="CSharpGameEngine.Runtime.Character.LocomotionObserver.CalculateDirection"/>の符号は、この事実に基づく。
    /// </summary>
    [Theory]
    [InlineData("MF_Unarmed_Walk_Fwd",0f)]
    [InlineData("MF_Unarmed_Walk_Fwd_Right",45f)]
    [InlineData("MF_Unarmed_Walk_Right",90f)]
    [InlineData("MF_Unarmed_Walk_Bwd_Right",135f)]
    [InlineData("MF_Unarmed_Walk_Bwd_Left",-135f)]
    [InlineData("MF_Unarmed_Walk_Left",-90f)]
    [InlineData("MF_Unarmed_Walk_Fwd_Left",-45f)]
    [InlineData("MF_Unarmed_Jog_Fwd",0f)]
    [InlineData("MF_Unarmed_Jog_Fwd_Right",45f)]
    [InlineData("MF_Unarmed_Jog_Right",90f)]
    [InlineData("MF_Unarmed_Jog_Left",-90f)]
    [InlineData("MF_Unarmed_Jog_Fwd_Left",-45f)]
    public void TheDirectionalClipsTravelTheWayTheyAreNamed(string Name,float ExpectedDegrees){
        var Skeleton=SkeletonOf("TopDown","SKM_Manny_Simple.glb");
        var Clip=SkeletalAssetCache.Clip(Asset("TopDown",Path.Combine("Anims",Name+".glb")),Skeleton);
        var Pose=new JointPose[Skeleton.JointCount];
        var Model=new Matrix4x4[Skeleton.JointCount];
        var Left=Skeleton.IndexOf("foot_l");
        var Right=Skeleton.IndexOf("foot_r");
        var Pelvis=Skeleton.IndexOf("pelvis");
        const int Steps=60;
        Vector3[]Feet(int Step){
            Clip.Sample(Skeleton,Clip.DurationSeconds*Step/Steps,true,Pose);
            Skeleton.ComputeModel(Pose,Model);
            return [Model[Left].Translation-Model[Pelvis].Translation,Model[Right].Translation-Model[Pelvis].Translation];
        }
        var Sum=Vector3.Zero;
        var Previous=Feet(0);
        for(var Step=1;Step<=Steps;Step++){
            var Next=Feet(Step%Steps);
            var Stance=Previous[0].Y<Previous[1].Y?0:1;
            // ボディは、骨盤に対する立脚側の足の動きと反対方向へ進む。
            Sum-=Next[Stance]-Previous[Stance];
            Previous=Next;
        }
        // モデル空間からシーン空間への変換：メッシュノードを上軸まわりに半回転させる。
        var R=-Sum.X;
        var F=-Sum.Z;
        Assert.True(MathF.Sqrt((R*R)+(F*F))>1e-3f,Name);
        var Degrees=CSharpGameEngine.Runtime.Character.LocomotionObserver.CalculateDirection(R,F,0f,1f);
        var Difference=MathF.Abs(((Degrees-ExpectedDegrees+540f)%360f)-180f);
        Assert.True(Difference<15f,$"{Name} travels {Degrees:0}° but is named for {ExpectedDegrees:0}°.");
    }
    /// <summary><paramref name="Name"/>クリップ（TopDownテンプレートのマネキン）の<paramref name="Seconds"/>時点におけるボディ姿勢。</summary>
    private static (Skeleton Skeleton,JointPose[]Pose)Posed(string Name,float Seconds){
        var Skeleton=SkeletonOf("TopDown","SKM_Manny_Simple.glb");
        var Clip=SkeletalAssetCache.Clip(Asset("TopDown",Path.Combine("Anims",Name+".glb")),Skeleton);
        var Pose=new JointPose[Skeleton.JointCount];
        Clip.Sample(Skeleton,Seconds,true,Pose);
        return (Skeleton,Pose);
    }
    private static Matrix4x4[]ModelOf(Skeleton Skeleton,ReadOnlySpan<JointPose>Pose){
        var Model=new Matrix4x4[Skeleton.JointCount];
        Skeleton.ComputeModel(Pose,Model);
        return Model;
    }
    [Fact]
    public void TheFirstPersonWarpPutsTheHeadAndUpperArmsOnTheirControlsAndTheHandsFollow(){
        // 歩行中は頭が揺れ、肩が振れる。これらはWarpで除去する対象。
        var (Skeleton,Pose)=Posed("MF_Unarmed_Walk_Fwd",0.4f);
        var Before=ModelOf(Skeleton,Pose);
        var Rest=ModelOf(Skeleton,Skeleton.Rest);
        var Rig=new FirstPersonRig(Skeleton,new FirstPersonPose { SpineWeight=0f });
        var Warped=Pose.ToArray();
        Rig.Apply(Warped,new Matrix4x4[Skeleton.JointCount]);
        var After=ModelOf(Skeleton,Warped);
        int Joint(string Name)=>Skeleton.IndexOf(Name);
        Assert.True(Vector3.Distance(Before[Joint("head")].Translation,Rest[Joint("head")].Translation)>0.002f,"the walk should move the head, or this test proves nothing");
        Assert.True(Vector3.Distance(After[Joint("head")].Translation,Rest[Joint("head")].Translation)<1e-4f);
        Assert.True(Vector3.Distance(Vector3.TransformNormal(Vector3.UnitY,After[Joint("head")]),Vector3.TransformNormal(Vector3.UnitY,Rest[Joint("head")]))<1e-3f);
        foreach(var Name in new[] { "upperarm_l","upperarm_r" }){
            Assert.True(Vector3.Distance(After[Joint(Name)].Translation,Rest[Joint(Name)].Translation)<1e-4f,Name);
            // 上腕はアニメーションの向きを維持し、前腕と手は上腕に固定して追従する。
            Assert.True(Vector3.Distance(Vector3.TransformNormal(Vector3.UnitX,After[Joint(Name)]),Vector3.TransformNormal(Vector3.UnitX,Before[Joint(Name)]))<1e-3f,Name);
        }
        foreach(var Side in new[] { "l","r" }){
            var HandBefore=Before[Joint("hand_"+Side)].Translation-Before[Joint("upperarm_"+Side)].Translation;
            var HandAfter=After[Joint("hand_"+Side)].Translation-After[Joint("upperarm_"+Side)].Translation;
            Assert.True(Vector3.Distance(HandBefore,HandAfter)<1e-3f,Side);
        }
        // Warpの指定対象以外は完全に元のまま。脚と、重み0の脊椎を維持する。
        foreach(var Name in new[] { "pelvis","thigh_l","calf_r","foot_l","spine_02","spine_04" }){
            Assert.Equal(Pose[Joint(Name)].Rotation,Warped[Joint(Name)].Rotation);
            Assert.Equal(Pose[Joint(Name)].Translation,Warped[Joint(Name)].Translation);
        }
    }
    [Fact]
    public void TheFirstPersonWarpPullsTheSpineTowardTheBodyOffsetAtHalfWeightKeepingItsLengths(){
        var (Skeleton,Pose)=Posed("MM_Idle",0f);
        var Before=ModelOf(Skeleton,Pose);
        int Joint(string Name)=>Skeleton.IndexOf(Name);
        var Settings=new FirstPersonPose();
        Assert.True(Settings.BodyOffset.TryToSingle(out var Offset));
        var Rig=new FirstPersonRig(Skeleton,Settings);
        var Warped=Pose.ToArray();
        Rig.Apply(Warped,new Matrix4x4[Skeleton.JointCount]);
        var After=ModelOf(Skeleton,Warped);
        var Chain=new[] { "spine_01","spine_02","spine_03","spine_04" };
        // 最初の脊椎ジョイントはチェーンのルートとして維持する。先端はオフセット位置へ近づくが、ボーンの長さは変わらない。
        Assert.True(Vector3.Distance(Before[Joint("spine_01")].Translation,After[Joint("spine_01")].Translation)<1e-5f);
        Assert.True(Vector3.Distance(After[Joint("spine_04")].Translation,Offset)<Vector3.Distance(Before[Joint("spine_04")].Translation,Offset)-0.01f);
        for(var Index=0;Index<Chain.Length-1;Index++){
            Assert.Equal(Vector3.Distance(Before[Joint(Chain[Index])].Translation,Before[Joint(Chain[Index+1])].Translation),Vector3.Distance(After[Joint(Chain[Index])].Translation,After[Joint(Chain[Index+1])].Translation),3);
        }
        // チェーン先端までの半分。解決後のチェーンは同じ長さを保つため、ブレンドは厳密には半分でないが近い値となる。
        var Moved=Vector3.Distance(Before[Joint("spine_04")].Translation,After[Joint("spine_04")].Translation);
        var Full=Vector3.Distance(Before[Joint("spine_04")].Translation,Offset);
        Assert.InRange(Moved,Full*0.3f,Full*0.7f);
        // Warpは脚ではなく、胸より下にあるジョイントを変更する。
        Assert.Equal(Pose[Joint("thigh_l")].Rotation,Warped[Joint("thigh_l")].Rotation);
    }
    [Fact]
    public void TheFirstPersonWarpAtFullWeightReachesAReachableOffsetAndStretchesTowardAFarOne(){
        var (Skeleton,Pose)=Posed("MM_Idle",0f);
        int Joint(string Name)=>Skeleton.IndexOf(Name);
        var Before=ModelOf(Skeleton,Pose);
        var Tip=Before[Joint("spine_04")].Translation;
        var Near=Tip+new Vector3(0f,-0.04f,0.03f);
        var Rig=new FirstPersonRig(Skeleton,new FirstPersonPose { SpineWeight=1f,BodyOffset=new LogicalVector3(Near.X,Near.Y,Near.Z),Precision=0.0005f,Iterations=40 });
        var Warped=Pose.ToArray();
        Rig.Apply(Warped,new Matrix4x4[Skeleton.JointCount]);
        Assert.True(Vector3.Distance(ModelOf(Skeleton,Warped)[Joint("spine_04")].Translation,Near)<0.002f);
        var Root=Before[Joint("spine_01")].Translation;
        var Reach=new[] { "spine_01","spine_02","spine_03" }.Zip(new[] { "spine_02","spine_03","spine_04" },(Parent,Child)=>Vector3.Distance(Before[Joint(Parent)].Translation,Before[Joint(Child)].Translation)).Sum();
        var Far=new Vector3(0f,Root.Y+5f,0f);
        var Stretched=new FirstPersonRig(Skeleton,new FirstPersonPose { SpineWeight=1f,BodyOffset=new LogicalVector3(Far.X,Far.Y,Far.Z) });
        var Straight=Pose.ToArray();
        Stretched.Apply(Straight,new Matrix4x4[Skeleton.JointCount]);
        var Tipped=ModelOf(Skeleton,Straight)[Joint("spine_04")].Translation;
        Assert.Equal(Reach,Vector3.Distance(Root,Tipped),2);
        Assert.True(Vector3.Distance(Vector3.Normalize(Tipped-Root),Vector3.UnitY)<0.01f);
    }
    [Fact]
    public void TheFirstPersonWarpRefusesASkeletonItCannotWarp(){
        var Skeleton=SkeletonOf("TopDown","SKM_Manny_Simple.glb");
        Assert.Throws<ArgumentException>(()=>new FirstPersonRig(Skeleton,new FirstPersonPose { Head="nose" }));
        Assert.Throws<ArgumentException>(()=>new FirstPersonRig(Skeleton,new FirstPersonPose { SpineStart="spine_04",SpineEnd="spine_01" }));
        Assert.Throws<ArgumentException>(()=>new FirstPersonRig(Skeleton,new FirstPersonPose()).Apply(new JointPose[3],new Matrix4x4[3]));
    }
    [Fact]
    public void TheFootIkOnTheRealMannequinLowersThePelvisAndKeepsTheFeetOnTheirGround(){
        var (Skeleton,Pose)=Posed("MM_Idle",0f);
        var Before=ModelOf(Skeleton,Pose);
        int Joint(string Name)=>Skeleton.IndexOf(Name);
        var Ik=new FootIkPose(Skeleton,new FootIk());
        // 左足の地面はメッシュ原点より8 cm低く、右足は2 cm高い。
        Ik.Update(0.05f,true,-0.08f,0.02f);
        var Solved=Pose.ToArray();
        Ik.Apply(Solved,new Matrix4x4[Skeleton.JointCount]);
        var After=ModelOf(Skeleton,Solved);
        Assert.Equal(-0.08f,Ik.PelvisOffset,5);
        Assert.Equal(Before[Joint("pelvis")].Translation.Y-0.08f,After[Joint("pelvis")].Translation.Y,3);
        Assert.Equal(Before[Joint("foot_l")].Translation.Y-0.08f,After[Joint("foot_l")].Translation.Y,3);
        Assert.Equal(Before[Joint("foot_r")].Translation.Y+0.02f,After[Joint("foot_r")].Translation.Y,3);
        // 足は地面上の位置を保ち、アニメーションの向きも維持する。
        Assert.Equal(Before[Joint("foot_l")].Translation.X,After[Joint("foot_l")].Translation.X,3);
        Assert.Equal(Before[Joint("foot_r")].Translation.Z,After[Joint("foot_r")].Translation.Z,3);
        Assert.True(Vector3.Distance(Vector3.TransformNormal(Vector3.UnitX,Before[Joint("foot_l")]),Vector3.TransformNormal(Vector3.UnitX,After[Joint("foot_l")]))<2e-3f);
        // 四肢の長さを保ち、膝は引き続きつま先側へ曲がる。
        foreach(var Side in new[] { "l","r" }){
            Assert.Equal(Vector3.Distance(Before[Joint("thigh_"+Side)].Translation,Before[Joint("calf_"+Side)].Translation),Vector3.Distance(After[Joint("thigh_"+Side)].Translation,After[Joint("calf_"+Side)].Translation),3);
            var Forward=Before[Joint("ball_"+Side)].Translation-Before[Joint("foot_"+Side)].Translation;
            Forward.Y=0f;
            var KneeBefore=Vector3.Dot(Before[Joint("calf_"+Side)].Translation-Before[Joint("thigh_"+Side)].Translation,Vector3.Normalize(Forward));
            var KneeAfter=Vector3.Dot(After[Joint("calf_"+Side)].Translation-After[Joint("thigh_"+Side)].Translation,Vector3.Normalize(Forward));
            Assert.True(KneeAfter*KneeBefore>=0f,Side);
        }
    }
}
