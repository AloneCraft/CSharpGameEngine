using System;
using CSharpGameEngine.VSExtension;
using Xunit;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class SceneDocumentMenuSnapshotTests {
    [Fact]
    public void InnermostTypeRange_DeterminesTheRightClickTarget(){
        SceneDocumentMenuSnapshot Snapshot=new(
            SceneDesignerTargetState.SceneType,
            [
                new SceneDocumentTypeRange(0,200,IsScene:true),
                new SceneDocumentTypeRange(30,80,IsScene:false),
            ],
            [new SceneDesignerMenuItem("MainScene","Project.MainScene")]);

        Assert.Equal(SceneDesignerTargetState.SceneType,Snapshot.GetTargetState(20));
        Assert.Equal(SceneDesignerTargetState.NonSceneType,Snapshot.GetTargetState(40));
        Assert.Equal(SceneDesignerTargetState.OutsideType,Snapshot.GetTargetState(220));
    }

    [Fact]
    public void AnalyzingSnapshot_DoesNotExposeStaleSceneSpans(){
        SceneDocumentMenuSnapshot Snapshot=new(
            SceneDesignerTargetState.Analyzing,
            [new SceneDocumentTypeRange(0,200,IsScene:true)],
            [new SceneDesignerMenuItem("OldScene","Project.OldScene")]);

        Assert.Equal(SceneDesignerTargetState.Analyzing,Snapshot.GetTargetState(20));
    }

    [Fact]
    public void SceneTypeRange_ExposesOnlyTheSceneAtTheRightClickPosition(){
        SceneDocumentMenuSnapshot Snapshot=new(
            SceneDesignerTargetState.SceneType,
            [
                new SceneDocumentTypeRange(0,40,IsScene:true,"Project.BoxScene"),
                new SceneDocumentTypeRange(50,90,IsScene:true,"Project.SphereScene"),
            ],
            [
                new SceneDesignerMenuItem("BoxScene","Project.BoxScene"),
                new SceneDesignerMenuItem("SphereScene","Project.SphereScene"),
            ]);

        Assert.Equal("Project.BoxScene",Snapshot.GetTargetSceneFullyQualifiedName(20));
        Assert.Equal("Project.SphereScene",Snapshot.GetTargetSceneFullyQualifiedName(60));
        Assert.Null(Snapshot.GetTargetSceneFullyQualifiedName(100));
    }

    [Fact]
    public void DynamicCommand_OnlyMatchesItsReservedRange(){
        const int FirstCommandId=0x0110;
        Assert.True(DynamicSceneMenuIdRange.Contains(FirstCommandId,FirstCommandId));
        Assert.True(DynamicSceneMenuIdRange.Contains(
            FirstCommandId,
            FirstCommandId+DynamicSceneMenuIdRange.MaximumItemCount-1));
        Assert.False(DynamicSceneMenuIdRange.Contains(
            FirstCommandId,
            FirstCommandId+DynamicSceneMenuIdRange.MaximumItemCount));
    }
}
