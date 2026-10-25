using System.Globalization;
using System.Text;
namespace CSharpGameEngine.Destruction.Tests;
/// <summary>テスト用のゲームのフォルダ(<c>Scenes</c>と<c>Assets</c>)を一時の場所に作り、シーン JSON を書く。</summary>
internal sealed class TestProject:IDisposable {
    public static readonly string RepositoryRoot=FindRepositoryRoot();
    public string Root { get; }=Path.Combine(Path.GetTempPath(),"cge-destruction-tests-"+Guid.NewGuid().ToString("N"));
    public TestProject(){
        Directory.CreateDirectory(Path.Combine(this.Root,"Scenes"));
    }
    public void Dispose(){
        if(Directory.Exists(this.Root)){ Directory.Delete(this.Root,recursive:true); }
    }
    private static string FindRepositoryRoot(){
        var Directory=new DirectoryInfo(AppContext.BaseDirectory);
        while(Directory is not null&&!File.Exists(Path.Combine(Directory.FullName,"CSharpGameEngine.slnx"))){ Directory=Directory.Parent; }
        return Directory?.FullName??throw new InvalidOperationException("The repository root (CSharpGameEngine.slnx) was not found above the test assembly.");
    }
    public static string Node(string Name,string[]Components,params string[]Children)=>
        $$"""{ "id": "{{Guid.NewGuid()}}", "name": "{{Name}}", "components": [{{string.Join(",",Components)}}], "children": [{{string.Join(",",Children)}}] }""";
    public static string Component(string Type,string Members="{}")=>$$"""{ "type": "{{Type}}", "members": {{Members}} }""";
    public static string Transform(float R=0f,float U=0f,float F=0f,float Scale=1f)=>
        Component("CSharpGameEngine.Entities.Transform",string.Create(CultureInfo.InvariantCulture,
            $$"""{ "Position": { "R": {{R}}, "U": {{U}}, "F": {{F}} }, "Scale": { "R": {{Scale}}, "U": {{Scale}}, "F": {{Scale}} } }"""));
    public static string BoxCollider(float HalfR,float HalfU,float HalfF,string Extra="")=>
        Component("CSharpGameEngine.Runtime.Physics.Collider",string.Create(CultureInfo.InvariantCulture,
            $$"""{ "Shape": "Box", "HalfExtents": { "R": {{HalfR}}, "U": {{HalfU}}, "F": {{HalfF}} }{{Extra}} }"""));
    public static string Destructible(string Name,string Extra="")=>
        Component(DestructionDefaults.DestructibleType,$$"""{ "FractureAsset": "{{Name}}"{{Extra}} }""");
    public string WriteScene(string FileName,string ClassName,string Root){
        var Text=new StringBuilder();
        Text.Append(CultureInfo.InvariantCulture,$$"""{ "schemaVersion": 3, "namespace": "Test.Scenes", "className": "{{ClassName}}", "root": {{Root}} }""");
        var Path=System.IO.Path.Combine(this.Root,"Scenes",FileName);
        File.WriteAllText(Path,Text.ToString());
        return Path;
    }
    /// <summary>リポジトリのテスト用 glb(<c>assets/TestFixtures/Gltf/Box.glb</c>、閉じた立方体)をプロジェクトの<c>Assets</c>へ写す。</summary>
    public string CopyBox(string RelativePath="Assets/Meshes/Box.glb"){
        var Destination=Path.Combine(this.Root,RelativePath.Replace('/',Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(Destination)!);
        File.Copy(Path.Combine(RepositoryRoot,"assets","TestFixtures","Gltf","Box.glb"),Destination,overwrite:true);
        return RelativePath;
    }
}
