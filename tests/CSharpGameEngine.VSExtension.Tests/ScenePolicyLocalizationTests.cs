using System.Globalization;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Tests;
using CSharpGameEngine.VSExtension;
using Xunit;
namespace CSharpGameEngine.VSExtension.Tests;
public sealed class ScenePolicyLocalizationTests {
    private static readonly CultureInfo English=CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo Japanese=CultureInfo.GetCultureInfo("ja-JP");
    [Fact]
    public void ContextMenu_UsesTheRequestedCultureWithoutTranslatingSceneNames(){
        SceneDesignerMenuItem Item=new("SampleScene","Game.SampleScene");
        var EnglishText=SceneDesignerContextMenuPolicy.ItemText(English,Item,IsAtCaret:true);
        var JapaneseText=SceneDesignerContextMenuPolicy.ItemText(Japanese,Item,IsAtCaret:true);
        Assert.Equal("Open SampleScene in Scene Designer (caret position)",EnglishText);
        Assert.Equal("SampleScene をシーン デザイナーで開く（カーソル位置）",JapaneseText);
    }
    [Fact]
    public void ContextMenu_DisabledReasonAndOverflowUseTheRequestedCulture(){
        var EnglishPresentation=SceneDesignerContextMenuPolicy.Create(
            English,@"G:\Project\Components.scene.json",SceneDesignerTargetState.NonSceneType,[]);
        var JapanesePresentation=SceneDesignerContextMenuPolicy.Create(
            Japanese,@"G:\Project\Components.scene.json",SceneDesignerTargetState.NonSceneType,[]);
        Assert.Equal("Open in Scene Designer (this class does not implement IScene)",EnglishPresentation.Text);
        Assert.Equal("シーン デザイナーで開く（このクラスは IScene を実装していません）",JapanesePresentation.Text);
        Assert.Equal(
            "Select 5 more Scenes from the combo box in Scene Designer.",
            SceneDesignerContextMenuPolicy.OverflowText(English,HiddenCount:5));
        Assert.Equal(
            "他 5 件はシーン デザイナーのコンボボックスから選択してください",
            SceneDesignerContextMenuPolicy.OverflowText(Japanese,HiddenCount:5));
    }
    [Fact]
    public void CameraValidation_UsesTheRequestedCultureAndKeepsMemberNamesInvariant(){
        SceneModel Model=SceneAnalysisTestHelper.Analyze([CameraComponents,"""
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Runtime.Windows;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Components=[
                    new Transform { RotationW=1f,ScaleX=1f,ScaleY=1f,ScaleZ=1f },
                    new Camera { NearZ=-1f,FarZ=2f }
                ] };
            }
            """]);
        var EnglishPreview=Assert.Single(SceneCameraPreviewBinding.Build(
            English,Model,[NodePath.Root],AspectRatio:1f));
        var JapanesePreview=Assert.Single(SceneCameraPreviewBinding.Build(
            Japanese,Model,[NodePath.Root],AspectRatio:1f));
        Assert.Equal("Camera NearZ/FarZ values are invalid.",EnglishPreview.ErrorMessage);
        Assert.Equal("CameraのNearZ/FarZの値が無効です。",JapanesePreview.ErrorMessage);
    }
    [Fact]
    public void ProjectionMismatch_UsesTheRequestedCultureAndKeepsPathInvariant(){
        SceneModel Model=SceneAnalysisTestHelper.Analyze([CameraComponents,"""
            using CSharpGameEngine.Entities;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Name="Root" };
            }
            """]);
        SceneKey Key=new("project","Game.MainScene");
        SceneProjection Left=new SceneProjectionReconciler().Reconcile(Key,Model,Previous:null);
        SceneProjection Right=new(Key,[]);
        Assert.Equal(
            "Node count differs (1 / 0).",
            SceneProjectionIdentityComparer.FindFirstMismatch(English,Left,Right));
        Assert.Equal(
            "ノード数が違います（1 / 0）。",
            SceneProjectionIdentityComparer.FindFirstMismatch(Japanese,Left,Right));
    }
    [Fact]
    public void UndoTransactionName_UsesAFullSentenceResource(){
        Assert.Equal(
            "Scene Designer: Remove node",
            SceneUndoTransactionNaming.Format(English,"Remove node"));
        Assert.Equal(
            "シーン デザイナー: Remove node",
            SceneUndoTransactionNaming.Format(Japanese,"Remove node"));
    }
    private const string CameraComponents="""
        namespace CSharpGameEngine.Runtime.Windows {
            public struct Transform {
                public float PositionX; public float PositionY; public float PositionZ;
                public float RotationX; public float RotationY; public float RotationZ; public float RotationW;
                public float ScaleX; public float ScaleY; public float ScaleZ;
            }
            public enum CameraProjection { Perspective, Orthographic }
            public struct Camera {
                public CameraProjection Projection; public float FovYRadians;
                public float NearZ; public float FarZ; public float OrthographicSize;
            }
        }
        """;
}
