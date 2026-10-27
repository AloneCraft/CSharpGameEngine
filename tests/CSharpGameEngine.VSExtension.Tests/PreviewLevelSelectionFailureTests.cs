using System.Numerics;
using System.Reflection;
using System.Windows.Controls;
using CSharpGameEngine.Contracts;
using Microsoft.VisualStudio.Shell;
namespace CSharpGameEngine.VSExtension.Tests;
[Collection(MockedVsCollection.Name)]
public sealed class PreviewLevelSelectionFailureTests {
    public PreviewLevelSelectionFailureTests(MockedVsFixture Fixture){ }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ATemplateSelectionPreservesItsSynchronousOutcome(int Scenario){
        ThreadHelper.JoinableTaskFactory.Run(async()=>{
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var Path=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"CgePreviewSelection-"+Guid.NewGuid().ToString("N")+".fbx");
            using var Content=new StaticMeshEditorContent(Path);
            var Viewport=Content.ViewportForTesting;
            var Client=new RenderPreviewHostClient(Guid.NewGuid(),Request=>{
                var Replacing=Request.Command==RenderPreviewHostCommand.ReplaceScene;
                if(Replacing&&Scenario==1){
                    var Event=Viewport.GetType().GetField("TemplateRejected",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(Viewport);
                    ((Action<PreviewLevelTemplate,PreviewLevelTemplate,string>)Event!)(PreviewLevelBuiltIns.Studio(),PreviewLevelBuiltIns.Default(),"template failed");
                }
                return new RenderPreviewHostResponse {
                    RequestId=Request.RequestId,SessionId=Request.SessionId,Sequence=Request.Sequence,
                    State=Replacing&&Scenario==0?RenderPreviewHostResultState.Failed:RenderPreviewHostResultState.Succeeded,
                    DiagnosticCode="PREVIEW_COMMAND_FAILED",Message="template failed",
                };
            },()=>{ });
            Client.Open(1,64,64,"Stage.cgeshaderartifact",new RenderPreviewScene { Camera=new RenderPreviewCamera() });
            var Mesh=new SimpleMesh {
                Positions=[Vector3.Zero,Vector3.UnitX,Vector3.UnitY],Normals=[Vector3.UnitZ,Vector3.UnitZ,Vector3.UnitZ],Indices=[0,1,2],
                BaseColorFactor=Vector4.One,BoundsCenter=Vector3.Zero,BoundsRadius=1f,
            };
            SetField(Viewport,"Mesh",Mesh);
            SetField(Viewport,"Bounds",new PreviewBounds(Vector3.Zero,Vector3.One,new Vector3(0.5f),1f));
            SetField(Viewport,"PreviewClient",Client);
            SetField(Viewport,"CurrentOperation",AuthoringOperationSnapshot.Start(Guid.NewGuid(),AuthoringHostOperation.Import,"Preview","ready"));
            var Box=Content.ToolbarItems.OfType<ComboBox>().Single(Item=>Item.Items.OfType<ComboBoxItem>().Any(Entry=>Entry.Tag is PreviewLevelEntry));
            Box.SelectedItem=null;
            Box.SelectedItem=Box.Items.OfType<ComboBoxItem>().Single(Item=>Item.Tag is PreviewLevelEntry Entry&&Entry.Id==PreviewLevelBuiltIns.StudioId);
            if(Scenario==2){
                Assert.Equal(VsixText.Format("PreviewLevel_Applied",PreviewLevelBuiltIns.Studio().DisplayName),Content.CurrentStatus);
                Assert.Equal(PreviewLevelBuiltIns.StudioId,((PreviewLevelEntry)((ComboBoxItem)Box.SelectedItem).Tag).Id);
            } else {
                Assert.Contains("template failed",Content.CurrentStatus,StringComparison.Ordinal);
                if(Scenario==1){ Assert.Equal(PreviewLevelBuiltIns.DefaultId,((PreviewLevelEntry)((ComboBoxItem)Box.SelectedItem).Tag).Id); }
            }
        });
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void BrokenOrDeletedSelectedTemplateFallsBackToDefault(int Scenario){
        ThreadHelper.JoinableTaskFactory.Run(async()=>{
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var Project=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"CgePreviewRefresh-"+Guid.NewGuid().ToString("N"));
            var Store=new PreviewLevelStore(Project);
            var Entry=Store.Duplicate(PreviewLevelBuiltIns.Studio(),"Temporary Studio");
            var ProjectFile=System.IO.Path.Combine(Project,"Test.csproj");
            File.WriteAllText(ProjectFile,"<Project><ItemGroup><PackageReference Include=\"CSharpGameEngine.Sdk\" /></ItemGroup></Project>");
            try {
                using var Content=new StaticMeshEditorContent(System.IO.Path.Combine(Project,"model.fbx"),new MemorySettings());
                var Preferences=(PreviewLevelPreferences)typeof(StaticMeshEditorContent).GetField("Preferences",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(Content)!;
                Preferences.DefaultId=PreviewLevelBuiltIns.NeutralId;
                var Box=Content.ToolbarItems.OfType<ComboBox>().Single(Item=>Item.Items.OfType<ComboBoxItem>().Any(Option=>Option.Tag is PreviewLevelEntry));
                Box.SelectedItem=Box.Items.OfType<ComboBoxItem>().Single(Item=>Item.Tag is PreviewLevelEntry Option&&Option.Id==Entry.Id);
                Assert.Equal(Entry.Id,Content.ViewportForTesting.Template.Id);
                if(Scenario==0){ File.Delete(Entry.FilePath!); }
                else if(Scenario==1){ File.WriteAllText(Entry.FilePath!,"{ broken"); }
                else { File.WriteAllText(Entry.FilePath!,"{ \"DisplayName\": \"x\", \"Background\": { \"Mode\": 99 } }"); }
                typeof(StaticMeshEditorContent).GetMethod("RefreshEntries",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(Content,null);
                Assert.Equal(PreviewLevelBuiltIns.DefaultId,Content.ViewportForTesting.Template.Id);
                Assert.Equal(PreviewLevelBuiltIns.DefaultId,((PreviewLevelEntry)((ComboBoxItem)Box.SelectedItem).Tag).Id);
                Assert.Contains(Entry.Id,Content.CurrentStatus,StringComparison.Ordinal);
                Assert.Equal(Entry.Id,Preferences.LastUsedId);
                Assert.Equal(PreviewLevelBuiltIns.NeutralId,Preferences.DefaultId);
            } finally {
                File.Delete(Entry.FilePath!);
                Directory.Delete(Store.Directory);
                Directory.Delete(System.IO.Path.GetDirectoryName(Store.Directory)!);
                File.Delete(ProjectFile);
                Directory.Delete(Project);
            }
        });
    }
    [Fact]
    public void SwitchingOneViewportPreservesItsCameraAndTheOtherViewport(){
        ThreadHelper.JoinableTaskFactory.Run(async()=>{
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var Default=PreviewLevelBuiltIns.Default();
            using var First=new AssetViewportHost("first.fbx",Default);
            using var Second=new AssetViewportHost("second.fbx",PreviewLevelBuiltIns.Neutral());
            var Mesh=new SimpleMesh {
                Positions=[Vector3.Zero,Vector3.UnitX,Vector3.UnitY],Normals=[Vector3.UnitZ,Vector3.UnitZ,Vector3.UnitZ],Indices=[0,1,2],
                BaseColorFactor=Vector4.One,BoundsCenter=Vector3.Zero,BoundsRadius=1f,
            };
            var Bounds=new PreviewBounds(Vector3.Zero,Vector3.One,new Vector3(0.5f),1f);
            var Requests=new List<RenderPreviewHostRequest>();
            using var Client=new RenderPreviewHostClient(Guid.NewGuid(),Request=>{
                Requests.Add(Request);
                return new RenderPreviewHostResponse { RequestId=Request.RequestId,SessionId=Request.SessionId,Sequence=Request.Sequence,State=RenderPreviewHostResultState.Succeeded };
            },()=>{ });
            Client.Open(1,64,64,"Stage.cgeshaderartifact",new RenderPreviewScene { Camera=new RenderPreviewCamera() });
            SetField(First,"Mesh",Mesh);
            SetField(First,"Bounds",Bounds);
            SetField(First,"PreviewClient",Client);
            SetField(First,"Composition",PreviewSceneComposer.Compose(Mesh,Bounds,Default,800,600));
            First.Camera.Frame(Bounds.Center,Bounds.Radius,800f/600f,Default.Camera);
            First.Camera.Orbit(20f,-12f);
            First.Camera.Dolly(2);
            Second.Camera.Frame(Vector3.One,2f,1f,Second.Template.Camera);
            var Camera=First.Camera.View;
            var SecondCamera=Second.Camera.View;
            var SecondTemplate=Second.Template;
            Requests.Clear();

            First.SetTemplate(PreviewLevelBuiltIns.Studio());

            Assert.Equal(Camera,First.Camera.View);
            Assert.Same(SecondTemplate,Second.Template);
            Assert.Equal(SecondCamera,Second.Camera.View);
            var Replace=Assert.Single(Requests);
            Assert.Equal(RenderPreviewHostCommand.ReplaceScene,Replace.Command);
            Assert.Equal(new[]{ PreviewSceneComposer.AssetObjectId,PreviewSceneComposer.FloorObjectId },Replace.Scene!.Objects.Select(static Object=>Object.ObjectId));
            Assert.Same(Mesh,typeof(AssetViewportHost).GetField("Mesh",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(First));
            Assert.Equal(new[]{ First.Camera.Eye.X,First.Camera.Eye.Y,First.Camera.Eye.Z },Replace.Scene.Camera!.Position);
            Assert.Equal(Camera,First.Camera.View);
            Requests.Clear();
            var LightingOnly=PreviewLevelBuiltIns.Studio();
            LightingOnly.Lights[0].Intensity+=1f;
            First.SetTemplate(LightingOnly);
            Assert.Empty(Requests);
            Assert.Equal(Camera,First.Camera.View);
            Assert.Same(SecondTemplate,Second.Template);
        });
    }
    private static void SetField(object Instance,string Name,object Value){
        var Field=Instance.GetType().GetField(Name,BindingFlags.Instance|BindingFlags.NonPublic)??throw new InvalidOperationException(Name);
        Field.SetValue(Instance,Value);
    }
    private sealed class MemorySettings:IPreviewLevelSettings {
        private readonly Dictionary<string,string>Values=[];
        public string?Read(string Name)=>this.Values.TryGetValue(Name,out var Value)?Value:null;
        public void Write(string Name,string?Value){
            if(Value is null){ this.Values.Remove(Name); } else { this.Values[Name]=Value; }
        }
    }
}
