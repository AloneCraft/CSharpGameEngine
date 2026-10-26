using CSharpGameEngine.Content.Models;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Import.Fbx;
using A=Assimp;
namespace CSharpGameEngine.Import.Tests.Fbx;
public sealed class FbxCompositeTests {
    [Fact]
    public void RegisteredFilePublishesCompositeManifestAndPreservesSource(){
        var Root=Path.Combine(Path.GetTempPath(),"CgeFbx",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Root);
        try {
            var Source=Path.Combine(Root,"Box.fbx");var Bytes=File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"Fixtures","Box.fbx"));File.WriteAllBytes(Source,Bytes);
            var Snapshot=CSharpGameEngine.Content.AssetAuthoringService.Execute(new(){RequestId=Guid.NewGuid(),RepositoryRoot=Root,SourcePath=Source});
            var Registered=CSharpGameEngine.Content.AssetAuthoringService.Execute(new(){AssetId=Snapshot.AssetId,Importer=Snapshot.Importer,RequestId=Guid.NewGuid(),Command=AssetAuthoringCommand.SaveMetadata,RepositoryRoot=Root,SourcePath=Source,Options=new ModelImportSettings().ToOptions().Select(P=>new AssetAuthoringOption{Name=P.Key,Value=P.Value}).ToArray()});
            Assert.True(Registered.Success,string.Join(";",Registered.Diagnostics.Select(D=>D.Message)));
            var Result=FbxModelCompiler.Execute(new(){RequestId=Guid.NewGuid(),Root=Root,SourcePath=Source,ModelCommand=2});Assert.True(Result.Success,Result.Diagnostic);
            var Manifest=System.Text.Json.JsonSerializer.Deserialize<CompositeModelManifest>(File.ReadAllBytes(Result.ManifestPath))!;
            Assert.Contains(Manifest.Outputs,O=>O.Kind=="model");Assert.Contains(Manifest.Outputs,O=>O.Kind=="mesh");Assert.DoesNotContain(Manifest.Outputs,O=>O.Kind=="clip"||O.Kind=="skeleton");Assert.Equal(Bytes,File.ReadAllBytes(Source));
            var Before=File.ReadAllBytes(Result.ManifestPath);File.WriteAllText(Source,"broken");Assert.False(FbxModelCompiler.Execute(new(){RequestId=Guid.NewGuid(),Root=Root,SourcePath=Source,ModelCommand=2}).Success);Assert.Equal(Before,File.ReadAllBytes(Result.ManifestPath));
        }finally { Directory.Delete(Root,true); }
    }
    [Fact]
    public void StaticFileImportsAllMeshesWithoutInventingRigOrClips(){
        var Source=Path.Combine(AppContext.BaseDirectory,"Fixtures","Box.fbx");
        var Model=FbxCompositeImporter.Import(Source,new());Assert.NotEmpty(Model.Meshes);Assert.NotEmpty(Model.Nodes);
        Assert.Null(Model.Skeleton);Assert.Empty(Model.Clips);
        foreach(var Mesh in Model.Meshes){ Assert.NotEmpty(ModelArtifactCodec.WriteMesh(Mesh)); }
    }
    [Fact]
    public void MeshlessSceneHasOneSkeletonAndSeparateTakeClips(){
        var Scene=new A.Scene{RootNode=new A.Node("Root")};Scene.RootNode.Children.Add(new A.Node("Bone"));
        for(var Take=0;Take<2;Take++){
            var Clip=new A.Animation{Name="Take"+Take,DurationInTicks=25,TicksPerSecond=25};
            var Track=new A.NodeAnimationChannel{NodeName="Bone"};Track.PositionKeys.Add(new(0,new(0f,0f,0f)));Track.PositionKeys.Add(new(25,new(0f,1f,0f)));
            Track.RotationKeys.Add(new(0,new(1f,0f,0f,0f)));Track.ScalingKeys.Add(new(0,new(1f,1f,1f)));Clip.NodeAnimationChannels.Add(Track);Scene.Animations.Add(Clip);
        }
        var Model=FbxCompositeImporter.ConvertScene(Scene,new());Assert.Empty(Model.Meshes);Assert.Empty(Model.Materials);Assert.NotNull(Model.Skeleton);Assert.Equal(2,Model.Skeleton.JointNames.Length);Assert.Equal(2,Model.Clips.Length);
        foreach(var Clip in Model.Clips){ Assert.Equal(1f,Clip.EndSeconds);Assert.NotEmpty(ModelArtifactCodec.WriteClip(Clip)); }
        Assert.NotEmpty(ModelArtifactCodec.WriteSkeleton(Model.Skeleton));
        Assert.Empty(FbxCompositeImporter.ConvertScene(Scene,new(){Skeleton=false,Animations=false}).Clips);
        var Split=FbxCompositeImporter.ConvertScene(Scene,new(){Clips=[new(){Name="Cut",Take="Take0",StartFrame=5,EndFrame=15}]}).Clips.Single(C=>C.Name=="Cut");
        Assert.Equal(.4f,Split.EndSeconds,5);Assert.Equal(.2f,Split.Tracks[0].Translation!.Values[0].Y,5);Assert.Equal(.6f,Split.Tracks[0].Translation!.Values[^1].Y,5);
        Assert.NotEmpty(ModelArtifactCodec.WriteClip(Split));
    }
    [Fact]
    public void SkeletalMeshesKeepWeightsAndSharedSkeletonInsteadOfBoneAssets(){
        var Scene=new A.Scene{RootNode=new A.Node("Root")};Scene.RootNode.Children.Add(new A.Node("Bone"));
        var Mesh=new A.Mesh("Body",A.PrimitiveType.Triangle);Mesh.Vertices.AddRange([new(0f,0f,0f),new(1f,0f,0f),new(0f,1f,0f)]);Mesh.Faces.Add(new A.Face([0,1,2]));
        var Bone=new A.Bone{Name="Bone",OffsetMatrix=A.Matrix4x4.Identity};for(var I=0;I<3;I++){ Bone.VertexWeights.Add(new(I,1f)); }Mesh.Bones.Add(Bone);Scene.Meshes.Add(Mesh);Scene.RootNode.MeshIndices.Add(0);
        var Model=FbxCompositeImporter.ConvertScene(Scene,new());Assert.Single(Model.Meshes);Assert.Single(Model.Skins);Assert.NotNull(Model.Meshes[0].Influences);Assert.Equal(2,Model.Skeleton!.JointNames.Length);
        Assert.NotEmpty(ModelArtifactCodec.WriteMesh(Model.Meshes[0]));Assert.Equal(1f,Model.Meshes[0].Influences!.Weights[0]);
    }
}
