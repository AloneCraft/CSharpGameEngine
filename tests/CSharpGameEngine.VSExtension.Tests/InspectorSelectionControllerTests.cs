using System.Linq;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class InspectorSelectionControllerTests {
    [Fact]
    public void Move_TraversesComponentAndMemberRowsInDisplayOrder(){
        EditorNodeId NodeId=new(1);
        SceneInspectorMember Member=new(
            NodeId,"global::Sample.Transform","PositionX","0f",true,
            InspectorEditorKind.Number,null,Microsoft.CodeAnalysis.SpecialType.System_Single,[]);
        InspectorMemberViewModel MemberViewModel=new(Member);
        SceneInspectorComponentGroup Group=new(
            NodeId,"global::Sample.Transform","Transform",
            [new SceneInspectorMemberRow(Member)]);
        InspectorComponentGroupViewModel GroupViewModel=new(Group,[MemberViewModel],true);
        InspectorSelectionController Controller=new();
        Controller.SetItems([GroupViewModel]);

        Assert.Same(GroupViewModel,Controller.Move(1));
        Assert.Same(MemberViewModel,Controller.Move(1));
        Assert.Same(MemberViewModel,Controller.Move(1));
        Assert.Same(GroupViewModel,Controller.Move(-1));
    }

    [Fact]
    public void SetItems_ClearsSelectionWhenRowsAreRebuilt(){
        EditorNodeId NodeId=new(1);
        SceneInspectorComponentGroup Group=new(
            NodeId,"global::Sample.Transform","Transform",[]);
        InspectorComponentGroupViewModel ViewModel=new(Group,[],true);
        InspectorSelectionController Controller=new();
        Controller.SetItems([ViewModel]);
        Controller.Select(ViewModel);

        Controller.SetItems([]);

        Assert.Null(Controller.Current);
        Assert.False(ViewModel.IsSelected);
    }

    [Fact]
    public void Select_ReplacesThePreviouslyHighlightedRow(){
        EditorNodeId NodeId=new(1);
        SceneInspectorMember Member=new(
            NodeId,"global::Sample.Transform","PositionX","0f",true,
            InspectorEditorKind.Number,null,Microsoft.CodeAnalysis.SpecialType.System_Single,[]);
        InspectorMemberViewModel MemberViewModel=new(Member);
        InspectorComponentGroupViewModel GroupViewModel=new(
            new SceneInspectorComponentGroup(NodeId,"global::Sample.Transform","Transform",
                [new SceneInspectorMemberRow(Member)]),
            [MemberViewModel],true);
        InspectorSelectionController Controller=new();
        Controller.SetItems([GroupViewModel]);

        Controller.Select(GroupViewModel);
        Controller.Select(MemberViewModel);

        Assert.False(GroupViewModel.IsSelected);
        Assert.True(MemberViewModel.IsSelected);
        Assert.Same(MemberViewModel,Controller.Current);
    }

    [Fact]
    public void Move_TraversesNestedRowsAndStopsAtBothEnds(){
        EditorNodeId NodeId=new(1);
        SceneInspectorMember PositionX=new(
            NodeId,"global::Sample.Transform","PositionX","0f",true,
            InspectorEditorKind.Number,null,Microsoft.CodeAnalysis.SpecialType.System_Single,[]);
        SceneInspectorMember PositionY=new(
            NodeId,"global::Sample.Transform","PositionY","0f",true,
            InspectorEditorKind.Number,null,Microsoft.CodeAnalysis.SpecialType.System_Single,[]);
        SceneInspectorMember RotationX=new(
            NodeId,"global::Sample.Transform","RotationX","0f",true,
            InspectorEditorKind.Number,null,Microsoft.CodeAnalysis.SpecialType.System_Single,[]);
        InspectorMemberViewModel PositionXViewModel=new(PositionX);
        InspectorMemberViewModel PositionYViewModel=new(PositionY);
        InspectorMemberViewModel RotationXViewModel=new(RotationX);
        InspectorNestedRowViewModel PositionViewModel=new(
            new SceneInspectorNestedRow(NodeId,"global::Sample.Transform",["Position"],"Position","Vector3",
                [new SceneInspectorMemberRow(PositionY)]),
            [PositionYViewModel],true);
        InspectorComponentGroupViewModel GroupViewModel=new(
            new SceneInspectorComponentGroup(NodeId,"global::Sample.Transform","Transform",
                [new SceneInspectorMemberRow(PositionX),
                 new SceneInspectorNestedRow(NodeId,"global::Sample.Transform",["Position"],"Position","Vector3",
                     [new SceneInspectorMemberRow(PositionY)]),
                 new SceneInspectorMemberRow(RotationX)]),
            [PositionXViewModel,PositionViewModel,RotationXViewModel],true);
        InspectorSelectionController Controller=new();
        Controller.SetItems([GroupViewModel]);
        Controller.Select(GroupViewModel);
        Assert.Same(GroupViewModel,Controller.Move(-1));
        Assert.Same(PositionXViewModel,Controller.Move(1));
        Assert.Same(PositionViewModel,Controller.Move(1));
        Assert.Same(PositionYViewModel,Controller.Move(1));
        Assert.Same(RotationXViewModel,Controller.Move(1));
        Assert.Same(RotationXViewModel,Controller.Move(1));
    }
    [Fact]
    public void SetItems_PreservesSelectionForTheSameLogicalRowAfterARefresh(){
        EditorNodeId NodeId=new(1);
        SceneInspectorMember OriginalMember=new(
            NodeId,"global::Sample.Transform","PositionX","0f",true,
            InspectorEditorKind.Number,null,Microsoft.CodeAnalysis.SpecialType.System_Single,[]);
        InspectorMemberViewModel OriginalViewModel=new(OriginalMember);
        InspectorComponentGroupViewModel OriginalGroup=new(
            new SceneInspectorComponentGroup(NodeId,"global::Sample.Transform","Transform",
                [new SceneInspectorMemberRow(OriginalMember)]),
            [OriginalViewModel],true);
        InspectorSelectionController Controller=new();
        Controller.SetItems([OriginalGroup]);
        Controller.Select(OriginalViewModel);

        SceneInspectorMember RefreshedMember=new(
            NodeId,"global::Sample.Transform","PositionX","1f",true,
            InspectorEditorKind.Number,null,Microsoft.CodeAnalysis.SpecialType.System_Single,[]);
        InspectorMemberViewModel RefreshedViewModel=new(RefreshedMember);
        InspectorComponentGroupViewModel RefreshedGroup=new(
            new SceneInspectorComponentGroup(NodeId,"global::Sample.Transform","Transform",
                [new SceneInspectorMemberRow(RefreshedMember)]),
            [RefreshedViewModel],true);

        Controller.SetItems([RefreshedGroup]);

        Assert.False(OriginalViewModel.IsSelected);
        Assert.True(RefreshedViewModel.IsSelected);
        Assert.Same(RefreshedViewModel,Controller.Current);
    }
}
