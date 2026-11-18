using System.IO;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.VisualStudio.Shell;
namespace CSharpGameEngine.VSExtension.Tests;
/// <summary>
/// Windows のパスは大文字小文字を区別しない。シーンを開いたパスの綴りが違うだけで、同じゲームの画像を Inspector へ落とせなくなってはいけない。
/// </summary>
[Collection(MockedVsCollection.Name)]
public sealed class ImageOutputProjectIdentityTests {
    public ImageOutputProjectIdentityTests(MockedVsFixture Fixture){ }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameWindowsProjectAcceptsImagesDespiteScenePathCasing(bool LowercaseScene)=>ThreadHelper.JoinableTaskFactory.Run(async()=>{
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var TempRoot=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        var Root=Path.GetFullPath(Path.Combine(TempRoot,"CgeImageProject-"+Guid.NewGuid().ToString("N")));
        Assert.False(Directory.Exists(Root));Directory.CreateDirectory(Root);
        var Previous=SceneEditorHub.ActiveSceneModel;
        try {
            var Game=Path.Combine(Root,"MixedGame");Directory.CreateDirectory(Path.Combine(Game,"Scenes"));
            var Project=Path.Combine(Game,"Game.csproj");File.WriteAllText(Project,"<Project><ItemGroup><PackageReference Include=\"CSharpGameEngine.Sdk\" Version=\"0.1.40\" /></ItemGroup></Project>");
            var Scene=Path.Combine(Game,"Scenes","Scene.json");File.WriteAllText(Scene,"{}");
            var ScenePath=LowercaseScene?Scene.ToLowerInvariant():Scene;
            var Resolved=CgeProjectContext.Resolve(ScenePath);
            Assert.NotNull(Resolved);Assert.Equal(Project,Resolved!.ProjectFilePath,StringComparer.OrdinalIgnoreCase);
            if(LowercaseScene){ Assert.NotEqual(Project,Resolved.ProjectFilePath); }
            SceneEditorHub.PublishSceneModel(new("Probe","Scene","Probe.Scene",null,null,false,0,[],ScenePath,0));
            var Output=ImageOutputSettings.For(TextureUsage.Color);
            var Asset=new AssetWorkspaceItem{AssetId=Guid.NewGuid().ToString(),Kind=AssetWorkspaceKind.Texture,
                Source=new(){ProjectPath=Project,ProjectRoot=Game,SourcePath=Path.Combine(Game,"Assets","Texture.png"),RelativePath="Assets/Texture.png"}};
            var Drag=new ImageOutputDrag(Asset,Output);
            var Member=new SceneInspectorMember(new EditorNodeId(1),"Probe","Texture","default",true,InspectorEditorKind.AssetReference,null,null,[],AssetTypeName:"CSharpGameEngine.Authoring.TextureAsset",TextureUsage:TextureUsage.Color);
            Assert.True(ImageOutputDrop.Accepts(Member,Asset,Drag));
            Assert.False(ImageOutputDrop.Accepts(Member,Asset,new(Asset,ImageOutputSettings.For(TextureUsage.Normal))));
            Asset.Source.ReadOnly=true;Assert.False(ImageOutputDrop.Accepts(Member,Asset,Drag));Asset.Source.ReadOnly=false;
            Asset.Source.ProjectPath=Path.Combine(Root,"OtherGame","Game.csproj");Assert.False(ImageOutputDrop.Accepts(Member,Asset,Drag));
        }finally{
            SceneEditorHub.PublishSceneModel(Previous);
            Assert.StartsWith(TempRoot,Path.GetFullPath(Root));Directory.Delete(Root,true);
        }
    });
}
