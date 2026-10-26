namespace CSharpGameEngine.Import.Fbx.Tests;

public class FbxImporterTests {
    private static string FixturePath(string FileName)=>Path.Combine(AppContext.BaseDirectory,"Fixtures",FileName);

    [Fact]
    public void Import_BoxFbx_Succeeds_WithPositionsNormalsAndIndices(){
        var Result=FbxImporter.Import(FixturePath("Box.fbx"));

        Assert.True(Result.Success,string.Join(Environment.NewLine,Result.Diagnostics));
        Assert.NotNull(Result.Mesh);
        Assert.Equal(Result.Mesh.Positions.Count,Result.Mesh.Normals.Count);
        Assert.Equal(Result.Mesh.Positions.Count,Result.Mesh.Uvs.Count);
        Assert.True(Result.Mesh.Indices.Count>0);
        Assert.Equal(0,Result.Mesh.Indices.Count%3);
        Assert.DoesNotContain(Result.Diagnostics,D=>D.Severity==FbxImportDiagnosticSeverity.Error);

        foreach(var Index in Result.Mesh.Indices){
            Assert.True(Index<Result.Mesh.Positions.Count);
        }
    }

    /// <summary>
    /// 単一の ANSI コードページでは表現できないパス（日本語と韓国語の名前、または英語版 Windows 上の日本語フォルダー）をインポートできる。AssimpNet はネイティブインポーターにファイル名を ANSI 文字列として渡すため、このような名前は「?」になっていた（「Unable to open file」）。
    /// </summary>
    [Fact]
    public void Import_BoxFbx_FromAPathOutsideTheAnsiCodePage_Succeeds(){
        var Folder=Path.Combine(Path.GetTempPath(),"cge 日本語 "+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Folder);
        try {
            var Path_=Path.Combine(Folder,"箱 한글 모델.fbx");
            File.Copy(FixturePath("Box.fbx"),Path_);

            var Result=FbxImporter.Import(Path_);

            Assert.True(Result.Success,string.Join(Environment.NewLine,Result.Diagnostics));
            Assert.True(Result.Mesh!.Indices.Count>0);
        } finally {
            Directory.Delete(Folder,true);
        }
    }

    [Fact]
    public void Import_BoxFbx_BoundingBox_IsPlausible(){
        var Result=FbxImporter.Import(FixturePath("Box.fbx"));

        Assert.True(Result.Success,string.Join(Environment.NewLine,Result.Diagnostics));
        var Bounds=Result.Mesh!.Bounds;

        Assert.True(Bounds.Min.X<Bounds.Max.X);
        Assert.True(Bounds.Min.Y<Bounds.Max.Y);
        Assert.True(Bounds.Min.Z<Bounds.Max.Z);
        Assert.InRange(Bounds.BoundingSphereRadius,0.01f,100f);
    }

    [Fact]
    public void Import_FileDoesNotExist_ReturnsFailedWithError(){
        var MissingPath=FixturePath("DoesNotExist.fbx");

        var Result=FbxImporter.Import(MissingPath);

        Assert.False(Result.Success);
        Assert.Null(Result.Mesh);
        Assert.Contains(Result.Diagnostics,D=>D.Severity==FbxImportDiagnosticSeverity.Error&&D.Message.Contains(MissingPath));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Import_NullOrWhitespacePath_Throws(string?Path){
        Assert.ThrowsAny<ArgumentException>(()=>FbxImporter.Import(Path!));
    }

    [Fact]
    public void Import_MalformedFile_ReturnsFailedNotException(){
        var TempPath=Path.Combine(Path.GetTempPath(), $"malformed-{Guid.NewGuid():N}.fbx");
        File.WriteAllText(TempPath,"this is not a valid FBX file");

        try {
            var Result=FbxImporter.Import(TempPath);

            Assert.False(Result.Success);
            Assert.Null(Result.Mesh);
            Assert.Contains(Result.Diagnostics,D=>D.Severity==FbxImportDiagnosticSeverity.Error);
        } finally {
            File.Delete(TempPath);
        }
    }

    [Fact]
    public void ConcurrentImports_KeepTheProcessAndTheirOwnDiagnostics(){
        // Assimp のログストリームはプロセス全体で共有される。並列インポート間で接続・切断するとテストホストがクラッシュした。
        // （ProviderNeutralImportTests はこのクラスと並列に FBX をインポートする）。ストリームにはすべてのインポートのメッセージが届く。
        var Box=FixturePath("Box.fbx");
        var Malformed=Path.Combine(Path.GetTempPath(),$"malformed-{Guid.NewGuid():N}.fbx");
        File.WriteAllText(Malformed,"this is not a valid FBX file");
        try {
            // Assimp の各行にはスレッド名（"T1234:"）が含まれ、インポートごとに異なる。
            static string[]Lines(FbxImportResult Result)=>[.. Result.Diagnostics.Select(D=>System.Text.RegularExpressions.Regex.Replace(D.ToString()??"",@"\bT\d+:","T:"))];
            var Alone=Lines(FbxImporter.Import(Box));
            var Results=new FbxImportResult[64];
            Parallel.For(0,Results.Length,new ParallelOptions { MaxDegreeOfParallelism=8 },Index=>
                Results[Index]=FbxImporter.Import(Index%2==0?Box:Malformed));

            for(var Index=0;Index<Results.Length;Index++){
                Assert.Equal(Index%2==0,Results[Index].Success);
                if(Index%2==0){
                    Assert.Equal(Alone,Lines(Results[Index]));
                }
            }
        } finally {
            File.Delete(Malformed);
        }
    }
}
