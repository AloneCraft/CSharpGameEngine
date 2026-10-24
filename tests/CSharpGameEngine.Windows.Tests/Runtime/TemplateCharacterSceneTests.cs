using System.Text.Json;
using CSharpGameEngine.Numerics;
using CSharpGameEngine.Runtime.Character;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>
/// ADR-0078/M0107: the character scenes of the templates carry the values read from the UE Blueprints (UE-Template-Behavior.md): the FirstPerson camera manager's pitch
/// limits and eye position, TopDown's camera lag and plane constraint, the Mannequin's eight-direction clips, and a world KillZ on every pawn.
/// </summary>
public sealed class TemplateCharacterSceneTests {
    private static string RepositoryRoot(){
        var Directory=new DirectoryInfo(AppContext.BaseDirectory);
        while(Directory is not null&&!File.Exists(Path.Combine(Directory.FullName,"CSharpGameEngine.slnx"))){ Directory=Directory.Parent; }
        return Directory?.FullName??throw new InvalidOperationException("The repository root was not found.");
    }
    private static string TemplateDirectory(string Template)=>Path.Combine(RepositoryRoot(),"Templates","CSharpGameEngine.Templates."+Template);
    private static JsonElement Scene(string Template,string Name)=>JsonDocument.Parse(File.ReadAllText(Path.Combine(TemplateDirectory(Template),"Scenes",Name+".json"))).RootElement.GetProperty("root");
    private static IEnumerable<JsonElement>Nodes(JsonElement Node){
        yield return Node;
        foreach(var Child in Node.GetProperty("children").EnumerateArray()){
            foreach(var Descendant in Nodes(Child)){ yield return Descendant; }
        }
    }
    private static JsonElement Members(JsonElement Root,string Component,string?Node=null){
        foreach(var Candidate in Nodes(Root)){
            if(Node is not null&&Candidate.GetProperty("name").GetString()!=Node){ continue; }
            foreach(var Item in Candidate.GetProperty("components").EnumerateArray()){
                if(Item.GetProperty("type").GetString()!.EndsWith("."+Component,StringComparison.Ordinal)){ return Item.GetProperty("members"); }
            }
        }
        throw new InvalidOperationException($"No {Component} on {Node??"any node"}.");
    }
    private static float Turns(JsonElement Members,string Name,float Default=0f)=>Members.TryGetProperty(Name,out var Value)?Value.GetSingle():Default;
    [Fact]
    public void TheTopDownCharacterBrakesOverUesFixedPathBrakingDistanceOfTwoMetres(){
        // BP_TopDownCharacter's CharacterMovement: FixedPathBrakingDistance 200 cm with bUseFixedBrakingDistanceForPaths (M0112).
        var Motion=Members(Scene("TopDown","TopDownCharacter"),"CharacterMotion");
        Assert.Equal(2f,Motion.GetProperty("FixedPathBrakingDistance").GetSingle());
    }
    [Fact]
    public void TheFirstPersonCameraManagerLimitsThePitchToMinus70AndPlus80Degrees(){
        // BP_FirstPersonCameraManager: ViewPitchMin -70, ViewPitchMax 80 (the controller's default is -89 to 89).
        var View=Members(Scene("FirstPerson","FirstPersonCharacter"),"ViewAngles");
        Assert.Equal(-70f,Turns(View,"MinPitch")*360f,2);
        Assert.Equal(80f,Turns(View,"MaxPitch")*360f,2);
        var Controller=new ViewAngles { MinPitchDegrees=Turns(View,"MinPitch")*360f,MaxPitchDegrees=Turns(View,"MaxPitch")*360f };
        PlayerControl.AddLook(ref Controller,0f,500f);
        Assert.Equal(80f,Controller.PitchDegrees,2);
        PlayerControl.AddLook(ref Controller,0f,-900f);
        Assert.Equal(-70f,Controller.PitchDegrees,2);
        Assert.Equal(-89f,new ViewAngles().MinPitchDegrees);
    }
    [Fact]
    public void TheFirstPersonEyeIsWhereTheRigsHeadAndTheCameraOffsetPutIt(){
        // UE (cm): CharacterMesh0 at (-20, 0, -96) relative to the capsule, turned -90 degrees so the mesh's +Y is the actor's forward; the rig's head control at
        // (0, 0.6631, 162.575) in mesh space; the camera at (2.794, 5.881, 0) in the head bone's frame (the bone's X is up, its Y forward). The eye in the actor's frame:
        var Forward=-20f+0.6631227f+5.880724f;
        var Up=-96f+162.5751f+2.79388f;
        var Pivot=Members(Scene("FirstPerson","FirstPersonCharacter"),"OrbitCamera","Eyes").GetProperty("Pivot");
        Assert.Equal(Up/100f,Pivot.GetProperty("U").GetSingle(),3);
        Assert.Equal(Forward/100f,Pivot.GetProperty("F").GetSingle(),3);
        Assert.Equal(0f,Pivot.GetProperty("R").GetSingle());
        // The field of view is UE's 90 degrees horizontal, at 16:9.
        var FovY=Turns(Members(Scene("FirstPerson","FirstPersonCharacter"),"Camera","Eyes"),"FovY")*360f;
        Assert.Equal(2f*MathF.Atan(MathF.Tan(MathF.PI/4f)*9f/16f)*180f/MathF.PI,FovY,1);
    }
    [Fact]
    public void TheFirstPersonBodyIsHiddenFromTheCameraButCastsItsShadowAndTheArmsCastNone(){
        var Root=Scene("FirstPerson","FirstPersonCharacter");
        var Body=Members(Root,"MeshVisibility","CharacterMesh0");
        Assert.False(Body.GetProperty("VisibleToCamera").GetBoolean());
        Assert.False(Body.TryGetProperty("CastsShadow",out var BodyCasts)&&!BodyCasts.GetBoolean());
        var Arms=Members(Root,"MeshVisibility","FirstPersonMesh");
        Assert.False(Arms.GetProperty("CastsShadow").GetBoolean());
        Assert.False(Arms.TryGetProperty("VisibleToCamera",out var ArmsVisible)&&!ArmsVisible.GetBoolean());
        _=Members(Root,"FirstPersonView","FirstPersonMesh");
        _=Members(Root,"FirstPersonPose","FirstPersonMesh");
        Assert.False(Members(Root,"CharacterMotion").GetProperty("FaceMovement").GetBoolean());
    }
    [Fact]
    public void TheTopDownCameraLagsAtSpeed3AndThePlaneConstraintHasUnrealsZeroNormal(){
        var Root=Scene("TopDown","TopDownCharacter");
        var Arm=Members(Root,"OrbitCamera");
        Assert.Equal(3f,Arm.GetProperty("Lag").GetSingle());
        Assert.Equal(14f,Arm.GetProperty("Distance").GetSingle());
        Assert.False(Arm.GetProperty("Collide").GetBoolean());
        Assert.False(Arm.TryGetProperty("RotationLag",out _));
        var Motion=Members(Root,"CharacterMotion");
        Assert.True(Motion.GetProperty("ConstrainToPlane").GetBoolean());
        Assert.True(Motion.GetProperty("SnapToPlaneAtStart").GetBoolean());
        Assert.False(Motion.TryGetProperty("PlaneNormal",out _));
        Assert.True(Motion.GetProperty("FaceMovement").GetBoolean());
    }
    [Theory]
    [InlineData("ThirdPerson","ThirdPersonCharacter","Mesh")]
    [InlineData("TopDown","TopDownCharacter","Mesh")]
    [InlineData("FirstPerson","FirstPersonCharacter","CharacterMesh0")]
    public void EveryTemplateCharacterHasTheEightDirectionsOfWalkingAndJoggingAndAFootIk(string Template,string Scene,string Node){
        var Root=TemplateCharacterSceneTests.Scene(Template,Scene);
        var Locomotion=Members(Root,"Locomotion",Node);
        var Clips=Locomotion.EnumerateObject().Where(Item=>Item.Value.ValueKind==JsonValueKind.String).ToArray();
        Assert.Equal(1+8+8+3,Clips.Length);
        foreach(var Clip in Clips){
            Assert.True(File.Exists(Path.Combine(TemplateDirectory(Template),Clip.Value.GetString()!)),Clip.Name+" "+Clip.Value);
        }
        Assert.Equal(8,Clips.Count(Item=>Item.Name.StartsWith("Walk",StringComparison.Ordinal)||Item.Name=="Walk"));
        Assert.Equal(8,Clips.Count(Item=>Item.Name.StartsWith("Jog",StringComparison.Ordinal)));
        _=Members(Root,"FootIk",Node);
    }
    [Theory]
    [InlineData("ThirdPerson","ThirdPersonCharacter")]
    [InlineData("TopDown","TopDownCharacter")]
    [InlineData("FirstPerson","FirstPersonCharacter")]
    public void EveryTemplatePawnCarriesUnrealsDefaultKillZ(string Template,string Scene){
        var KillZ=Members(TemplateCharacterSceneTests.Scene(Template,Scene),"KillZ");
        Assert.False(KillZ.TryGetProperty("Height",out _),"the scene leaves the default, UE's -1048575 cm");
        Assert.Equal(-10485.75f,new KillZ().Height.TryToSingle(out var Height)?Height:float.NaN);
    }
}
