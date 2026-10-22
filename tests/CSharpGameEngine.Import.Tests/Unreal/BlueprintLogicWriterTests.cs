using CSharpGameEngine.Unreal.SceneImporter;
namespace CSharpGameEngine.Import.Tests.Unreal;
/// <summary>ADR-0078/M0102: UnrealのT3D書き出しを読み、Blueprintの処理を疑似コードにする。</summary>
public sealed class BlueprintLogicWriterTests {
    private static string Pin(string Id,string Name,string Category,bool Output=false,string Links="",string Default="")=>
        $"CustomProperties Pin (PinId={Id},PinName=\"{Name}\",PinToolTip=\"a, (b) \\\"c\\\"\",{(Output?"Direction=\"EGPD_Output\",":"")}PinType.PinCategory=\"{Category}\",PinType.PinSubCategoryObject=None,{(Default.Length>0?$"DefaultValue=\"{Default}\",":"")}{(Links.Length>0?$"LinkedTo=({Links}),":"")}bHidden=False,)";
    /// <summary>入力のイベント → Jump()、Completed → StopJumping()、純粋な関数の式を引数に取る関数呼び出し。</summary>
    private static string Sample=>string.Join("\n",
        "Begin Object Class=/Script/Engine.Blueprint Name=\"BP_Test\" ExportPath=\"/Script/Engine.Blueprint'/Game/BP_Test.BP_Test'\"",
        "   Begin Object Class=/Script/Engine.EdGraph Name=\"EventGraph\" ExportPath=\"/Script/Engine.EdGraph'/Game/BP_Test.BP_Test:EventGraph'\"",
        "      Begin Object Class=/Script/InputBlueprintNodes.K2Node_EnhancedInputAction Name=\"Input_0\" ExportPath=\"/Script/InputBlueprintNodes.K2Node_EnhancedInputAction'/Game/BP_Test.BP_Test:EventGraph.Input_0'\"",
        "      End Object",
        "      Begin Object Class=/Script/BlueprintGraph.K2Node_CallFunction Name=\"Call_Jump\" ExportPath=\"/Script/BlueprintGraph.K2Node_CallFunction'/Game/BP_Test.BP_Test:EventGraph.Call_Jump'\"",
        "      End Object",
        "      Begin Object Class=/Script/BlueprintGraph.K2Node_CallFunction Name=\"Call_Stop\" ExportPath=\"/Script/BlueprintGraph.K2Node_CallFunction'/Game/BP_Test.BP_Test:EventGraph.Call_Stop'\"",
        "      End Object",
        "      Begin Object Class=/Script/BlueprintGraph.K2Node_CallFunction Name=\"Call_Add\" ExportPath=\"/Script/BlueprintGraph.K2Node_CallFunction'/Game/BP_Test.BP_Test:EventGraph.Call_Add'\"",
        "      End Object",
        "      Begin Object Class=/Script/BlueprintGraph.K2Node_Knot Name=\"Knot_0\" ExportPath=\"/Script/BlueprintGraph.K2Node_Knot'/Game/BP_Test.BP_Test:EventGraph.Knot_0'\"",
        "      End Object",
        "      Begin Object Name=\"Input_0\" ExportPath=\"/Script/InputBlueprintNodes.K2Node_EnhancedInputAction'/Game/BP_Test.BP_Test:EventGraph.Input_0'\"",
        "         InputAction=\"/Script/EnhancedInput.InputAction'/Game/Input/Actions/IA_Jump.IA_Jump'\"",
        "         NodePosX=-100",
        "         "+Pin("P1","Started","exec",true,"Knot_0 K1,"),
        "         "+Pin("P2","Completed","exec",true,"Call_Stop S1,"),
        "         "+Pin("P3","ActionValue","bool",true),
        "      End Object",
        "      Begin Object Name=\"Knot_0\" ExportPath=\"/Script/BlueprintGraph.K2Node_Knot'/Game/BP_Test.BP_Test:EventGraph.Knot_0'\"",
        "         "+Pin("K1","InputPin","exec",false,"Input_0 P1,"),
        "         "+Pin("K2","OutputPin","exec",true,"Call_Jump J1,"),
        "      End Object",
        "      Begin Object Name=\"Call_Jump\" ExportPath=\"/Script/BlueprintGraph.K2Node_CallFunction'/Game/BP_Test.BP_Test:EventGraph.Call_Jump'\"",
        "         FunctionReference=(MemberName=\"LaunchCharacter\",bSelfContext=True)",
        "         "+Pin("J1","execute","exec",false,"Knot_0 K2,"),
        "         "+Pin("J2","then","exec",true),
        "         "+Pin("J3","LaunchVelocity","struct",false,"Call_Add A3,"),
        "      End Object",
        "      Begin Object Name=\"Call_Stop\" ExportPath=\"/Script/BlueprintGraph.K2Node_CallFunction'/Game/BP_Test.BP_Test:EventGraph.Call_Stop'\"",
        "         FunctionReference=(MemberName=\"StopJumping\",bSelfContext=True)",
        "         "+Pin("S1","execute","exec",false,"Input_0 P2,"),
        "      End Object",
        "      Begin Object Name=\"Call_Add\" ExportPath=\"/Script/BlueprintGraph.K2Node_CallFunction'/Game/BP_Test.BP_Test:EventGraph.Call_Add'\"",
        "         FunctionReference=(MemberParent=\"/Script/CoreUObject.Class'/Script/Engine.KismetMathLibrary'\",MemberName=\"Add_VectorVector\")",
        "         "+Pin("A1","A","struct",false,"","0,0,500"),
        "         "+Pin("A2","B","struct",false,"","1,2,3"),
        "         "+Pin("A3","ReturnValue","struct",true,"Call_Jump J3,"),
        "      End Object",
        "   End Object",
        "End Object");
    [Fact]
    public void ParsesObjectsPropertiesPinsAndLinks(){
        var Document=T3dDocument.Parse(Sample);
        var Root=Assert.Single(Document.Roots);
        Assert.Equal("Blueprint",Root.Class);
        var Graph=Assert.Single(Root.Children);
        Assert.Equal(5,Graph.Children.Count);
        var Input=Graph.Children.Single(static Child=>Child.Name=="Input_0");
        Assert.Equal("K2Node_EnhancedInputAction",Input.Class);
        Assert.Equal("-100",Input.Property("NodePosX"));
        var Started=Assert.Single(Input.Pins,static Pin=>Pin.Name=="Started");
        Assert.True(Started.IsOutput);
        Assert.True(Started.IsExec);
        Assert.Equal(("Knot_0","K1"),Assert.Single(Started.Links));
        // ツールチップの中の引用符・括弧・コンマで、ピンの属性が切れない。
        Assert.Equal("bool",Input.Pins.Single(static Pin=>Pin.Name=="ActionValue").Category);
    }
    [Fact]
    public void WritesTheExecutionFlowWithPureExpressionsAndSkipsKnots(){
        var Text=BlueprintLogicWriter.Write(T3dDocument.Parse(Sample));
        Assert.Contains("input IA_Jump (ActionValue):",Text);
        Assert.Contains("[Started]:",Text);
        Assert.Contains("LaunchCharacter(LaunchVelocity=Add_VectorVector(A=0,0,500, B=1,2,3))",Text);
        Assert.Contains("[Completed]:",Text);
        Assert.Contains("StopJumping()",Text);
        Assert.DoesNotContain("Knot",Text);
        Assert.DoesNotContain("NodePosX",Text);
    }
    [Theory]
    [InlineData("/Script/Engine.Blueprint","Blueprint")]
    [InlineData("/Script/CoreUObject.Class'/Script/Engine.Character'","Character")]
    [InlineData("/Script/AnimGraph.AnimationStateGraph'ABP:AnimGraph.Machine.AnimStateNode_2.Walk / Run'","Walk / Run")]
    [InlineData("/Script/EnhancedInput.InputAction'/Game/Input/Actions/IA_Jump.IA_Jump'","IA_Jump")]
    public void ShortNameKeepsSlashesInsideObjectNames(string Value,string Expected){
        Assert.Equal(Expected,T3dDocument.ShortName(Value));
    }
    [Fact]
    public void SplitsOnlyOnTopLevelCommas(){
        var Fields=T3dDocument.ParseStruct("(A=1,B=(C=\"x,y\",D=(E=2)),F=\"(,)\")");
        Assert.Equal(["A","B","F"],Fields.Keys.ToArray());
        Assert.Equal("(C=\"x,y\",D=(E=2))",Fields["B"]);
        Assert.Equal("\"(,)\"",Fields["F"]);
    }
}
