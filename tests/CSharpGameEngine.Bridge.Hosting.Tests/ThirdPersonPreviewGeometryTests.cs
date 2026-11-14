using System.Numerics;
using System.Text.Json;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Import.AssetAuthoring.Cli;
using CSharpGameEngine.Import.Gltf;
namespace CSharpGameEngine.Bridge.Hosting.Tests;
public sealed class ThirdPersonPreviewGeometryTests {
    private sealed class TemporaryDirectory:IDisposable {
        internal string Path { get;}=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"CgeWallPreview-"+Guid.NewGuid().ToString("N"));
        internal TemporaryDirectory()=>System.IO.Directory.CreateDirectory(this.Path);
        public void Dispose()=>System.IO.Directory.Delete(this.Path,true);
    }
    [Fact]
    public async Task StageWallsUseTheSameCornerPivotAndForwardDirectionAsTheGame(){
        var Repository=new DirectoryInfo(AppContext.BaseDirectory);
        while(Repository is not null&&!File.Exists(Path.Combine(Repository.FullName,"CSharpGameEngine.slnx"))){ Repository=Repository.Parent; }
        Assert.NotNull(Repository);using var Directory=new TemporaryDirectory();
        var Template=Path.Combine(Repository.FullName,"Templates","CSharpGameEngine.Templates.ThirdPerson");var Assets=Path.Combine(Directory.Path,"Assets");System.IO.Directory.CreateDirectory(Assets);
        foreach(var FileName in System.IO.Directory.GetFiles(Path.Combine(Template,"Assets","Stage","LegacyThirdPerson","Meshes"),"*.glb")){ File.Copy(FileName,Path.Combine(Assets,Path.GetFileName(FileName))); }
        var Sources=System.IO.Directory.GetFiles(Assets,"*.glb");
#if DEBUG
        const string Configuration="Debug";
#else
        const string Configuration="Release";
#endif
        var Worker=Path.Combine(Repository.FullName,"tools","CSharpGameEngine.Import.AssetAuthoring.Cli","bin",Configuration,"net10.0","CSharpGameEngine.Import.AssetAuthoring.Cli.dll");
        var Input=Path.Combine(Directory.Path,"request.json");var Output=Path.Combine(Directory.Path,"result.json");
        File.WriteAllText(Input,JsonSerializer.Serialize(new AssetModelRequest{ModelCommand=4,RequestId=Guid.NewGuid(),Root=Directory.Path,SourcePath=Sources[0],PreviewSourcePaths=Sources}));
        var Start=new System.Diagnostics.ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};Start.ArgumentList.Add(Worker);Start.ArgumentList.Add(Input);Start.ArgumentList.Add(Output);
        using(var Process=System.Diagnostics.Process.Start(Start)!){ var Standard=Process.StandardOutput.ReadToEndAsync();var Error=Process.StandardError.ReadToEndAsync();await Process.WaitForExitAsync();Assert.True(Process.ExitCode==0,await Error+await Standard); }
        var Ready=JsonSerializer.Deserialize<AssetModelResult>(File.ReadAllText(Output))!;Assert.True(Ready.Success,Ready.Diagnostic);
        var Prepared=new Dictionary<string,RenderPreviewScene>();var Original=new Dictionary<string,RenderPreviewScene>();
        foreach(var Source in Ready.PreparedSources){
            var Raw=GltfImporter.Import(Source.SourcePath);Assert.True(Raw.Success);var Mesh=Raw.Mesh!;
            Original[Path.GetFileName(Source.SourcePath)]=new(){Meshes=[new(){Positions=Flatten(Mesh.Positions),Normals=Flatten(Mesh.Normals),Indices=[..Mesh.Indices]}],Materials=[new(){BaseColor=[Mesh.BaseColorFactor.X,Mesh.BaseColorFactor.Y,Mesh.BaseColorFactor.Z,Mesh.BaseColorFactor.W]}],Objects=[new(){ObjectId="raw",MeshIndex=0,MaterialIndex=0}]};
            Prepared[Path.GetFileName(Source.SourcePath)]=await ModelPreviewGeometry.ReadAsync(Source,CancellationToken.None);
        }
        const string Wall="SM_Cube_bfe650e90f.glb";
        Assert.Equal(0f,Original[Wall].Meshes[0].Positions.Where((_,Index)=>Index%3==2).Min(),5);Assert.Equal(1f,Original[Wall].Meshes[0].Positions.Where((_,Index)=>Index%3==2).Max(),5);
        Assert.Equal(-1f,Prepared[Wall].Meshes[0].Positions.Where((_,Index)=>Index%3==2).Min(),5);Assert.Equal(0f,Prepared[Wall].Meshes[0].Positions.Where((_,Index)=>Index%3==2).Max(),5);
        using var Document=JsonDocument.Parse(File.ReadAllText(Path.Combine(Template,"Scenes","Lvl_ThirdPerson.json")));
        var StartNode=Document.RootElement.GetProperty("root").GetProperty("children").EnumerateArray().Single(Node=>Node.GetProperty("name").GetString()=="PlayerStart");
        var StartPosition=StartNode.GetProperty("components").EnumerateArray().Single(Component=>Component.GetProperty("type").GetString()=="CSharpGameEngine.Entities.Transform").GetProperty("members").GetProperty("Position");
        Assert.Equal(9f,StartPosition.GetProperty("R").GetSingle(),3);Assert.Equal(-11.1f,StartPosition.GetProperty("F").GetSingle(),3);
        var Before=Compose(Original);var After=Compose(Prepared);
        var WallObject=After.Objects.Single(Object=>Object.ObjectId.StartsWith("SM_Cube2/",StringComparison.Ordinal));var WallMatrix=ToMatrix(WallObject.World);
        var WallPositions=After.Meshes[WallObject.MeshIndex].Positions;var Forward=Enumerable.Range(0,WallPositions.Length/3).Select(Index=>Vector3.Transform(new(WallPositions[Index*3],WallPositions[Index*3+1],WallPositions[Index*3+2]),WallMatrix).Z).ToArray();
        Assert.Equal(-1f,Forward.Min(),3);Assert.Equal(0f,Forward.Max(),3);
        var Nodes=After.Objects.Select(Object=>Object.ObjectId.Split('/')[0]).Distinct().ToArray();
        Assert.Equal(30,Nodes.Length);
        Assert.Contains("Third Person Template",Nodes);
        var GridMaterials=After.Materials.Where(Material=>Material.WorldGrid is not null).ToArray();
        Assert.NotEmpty(GridMaterials);
        Assert.All(GridMaterials,Material=>Assert.True(Material.WorldGrid!.AnchorToObject));
        Assert.Equal(3,Nodes.Count(Name=>Name.StartsWith("SM_ChamferCube",StringComparison.Ordinal)));
        var Evidence=Environment.GetEnvironmentVariable("CGE_WALL_PREVIEW_EVIDENCE");
        if(!string.IsNullOrWhiteSpace(Evidence)){ System.IO.Directory.CreateDirectory(Evidence);File.WriteAllText(Path.Combine(Evidence,"before.json"),JsonSerializer.Serialize(Before));File.WriteAllText(Path.Combine(Evidence,"after.json"),JsonSerializer.Serialize(After)); }
        var Reference=Ready.PreparedSources.Single(Source=>Path.GetFileName(Source.SourcePath)==Wall);File.Delete(Reference.SourcePath);
        Assert.NotEmpty((await ModelPreviewGeometry.ReadAsync(Reference,CancellationToken.None)).Meshes);Reference.ModelHash=new string('0',64);
        await Assert.ThrowsAsync<InvalidDataException>(()=>ModelPreviewGeometry.ReadAsync(Reference,CancellationToken.None));
        RenderPreviewScene Compose(Dictionary<string,RenderPreviewScene>Geometry){
            var Meshes=new List<RenderPreviewMesh>();var Materials=new List<RenderPreviewMaterial>();var Objects=new List<RenderPreviewObject>();
            Visit(Document.RootElement.GetProperty("root"),Matrix4x4.Identity);
            var Eye=new Vector3(77,85,95);var View=Matrix4x4.CreateLookAt(Eye,new(0,0,8),Vector3.UnitY);var Projection=Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI/3,1,0.1f,500);
            return new(){Meshes=[..Meshes],Materials=[..Materials],Objects=[..Objects],Camera=new(){Position=[Eye.X,Eye.Y,Eye.Z],ViewProjection=Wire(View*Projection),FovYRadians=MathF.PI/3,NearZ=0.1f,FarZ=500}};
            void Visit(JsonElement Node,Matrix4x4 Parent){
                var World=Parent;string?Source=null;
                foreach(var Component in Node.GetProperty("components").EnumerateArray()){
                    var Type=Component.GetProperty("type").GetString();
                    var Members=Component.TryGetProperty("members",out var Written)?Written:default;
                    if(Type=="CSharpGameEngine.Entities.Transform"){
                        // シーン JSON は省略したメンバーと軸を既定値にする（位置 0、回転なし、拡縮 1）。ゲームと同じ読み方をする。
                        var Rotation=Member(Members,"Rotation");
                        var Euler=VectorOr(Rotation.ValueKind==JsonValueKind.Object?Member(Rotation,"EulerRuf"):default,Vector3.Zero)*MathF.Tau;
                        World=Matrix4x4.CreateScale(VectorOr(Member(Members,"Scale"),Vector3.One))*Matrix4x4.CreateFromQuaternion(Quaternion.CreateFromYawPitchRoll(Euler.Y,Euler.X,Euler.Z))*Matrix4x4.CreateTranslation(VectorOr(Member(Members,"Position"),Vector3.Zero))*Parent;
                    }
                    if(Type=="CSharpGameEngine.Runtime.Windows.StaticMesh"){ Source=Path.GetFileName(Members.GetProperty("AssetPath").GetString()); }
                }
                if(Source is not null&&Geometry.TryGetValue(Source,out var Model)){
                    foreach(var Object in Model.Objects){ var MeshIndex=Meshes.Count;var MaterialIndex=Materials.Count;Meshes.Add(Model.Meshes[Object.MeshIndex]);Materials.Add(Model.Materials[Object.MaterialIndex]);Objects.Add(new(){ObjectId=Node.GetProperty("name").GetString()+"/"+Object.ObjectId,MeshIndex=MeshIndex,MaterialIndex=MaterialIndex,World=Wire(ToMatrix(Object.World)*World)}); }
                }
                if(Node.TryGetProperty("children",out var Children)){ foreach(var Child in Children.EnumerateArray()){ Visit(Child,World); } }
            }
        }
    }
    private static Vector3 Vector(JsonElement Value)=>new(Value.GetProperty("R").GetSingle(),Value.GetProperty("U").GetSingle(),Value.GetProperty("F").GetSingle());
    private static JsonElement Member(JsonElement Value,string Name)=>Value.ValueKind==JsonValueKind.Object&&Value.TryGetProperty(Name,out var Found)?Found:default;
    /// <summary>省略したベクトルは <paramref name="Default"/>、省略した軸はその軸の既定値（シーン JSON の値の規則）。</summary>
    private static Vector3 VectorOr(JsonElement Value,Vector3 Default)=>Value.ValueKind!=JsonValueKind.Object?Default:new(
        Value.TryGetProperty("R",out var R)?R.GetSingle():Default.X,Value.TryGetProperty("U",out var U)?U.GetSingle():Default.Y,Value.TryGetProperty("F",out var F)?F.GetSingle():Default.Z);
    private static float[]Flatten(IReadOnlyList<Vector3>Values)=>Values.SelectMany(static Value=>new[]{Value.X,Value.Y,Value.Z}).ToArray();
    private static Matrix4x4 ToMatrix(RenderPreviewMatrix4x4 Value){ var V=Value.Values;return new(V[0],V[1],V[2],V[3],V[4],V[5],V[6],V[7],V[8],V[9],V[10],V[11],V[12],V[13],V[14],V[15]); }
    private static RenderPreviewMatrix4x4 Wire(Matrix4x4 V)=>new(){Values=[V.M11,V.M12,V.M13,V.M14,V.M21,V.M22,V.M23,V.M24,V.M31,V.M32,V.M33,V.M34,V.M41,V.M42,V.M43,V.M44]};
}
