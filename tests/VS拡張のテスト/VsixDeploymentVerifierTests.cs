namespace CSharpGameEngine.VSExtension.Tests;

public sealed class VsixDeploymentVerifierTests {
    [Fact]
    public void Verify_EqualAssemblyBytes_IsCurrent(){
        using var Files=new TemporaryAssemblyFiles(new byte[]{ 1,2,3 },new byte[]{ 1,2,3 });

        VsixDeploymentVerification Result=VsixDeploymentVerifier.Verify(Files.Loaded,Files.Build);

        Assert.True(Result.IsVerifiable);
        Assert.True(Result.IsCurrent);
        Assert.Equal(Result.LoadedHash,Result.BuildHash);
        Assert.Equal("current",Result.CompactState);
    }

    [Fact]
    public void Verify_DifferentAssemblyBytes_IsStale(){
        using var Files=new TemporaryAssemblyFiles(new byte[]{ 1,2,3 },new byte[]{ 3,2,1 });

        VsixDeploymentVerification Result=VsixDeploymentVerifier.Verify(Files.Loaded,Files.Build);

        Assert.True(Result.IsVerifiable);
        Assert.False(Result.IsCurrent);
        Assert.NotEqual(Result.LoadedHash,Result.BuildHash);
        Assert.Equal("STALE",Result.CompactState);
        Assert.Contains("Launch-VSExtension.ps1",Result.Message);
    }

    [Fact]
    public void Verify_MissingBuildOutput_IsUnverified(){
        using var Files=new TemporaryAssemblyFiles(new byte[]{ 1 },new byte[]{ 2 });
        File.Delete(Files.Build);

        VsixDeploymentVerification Result=VsixDeploymentVerifier.Verify(Files.Loaded,Files.Build);

        Assert.False(Result.IsVerifiable);
        Assert.False(Result.IsCurrent);
        Assert.Equal("unverified",Result.CompactState);
    }

    private sealed class TemporaryAssemblyFiles:IDisposable {
        private readonly string DirectoryPath=Path.Combine(Path.GetTempPath(),"CSharpGameEngine.VsixTests",Guid.NewGuid().ToString("N"));

        internal TemporaryAssemblyFiles(byte[] LoadedBytes,byte[] BuildBytes){
            Directory.CreateDirectory(this.DirectoryPath);
            this.Loaded=Path.Combine(this.DirectoryPath,"loaded.dll");
            this.Build=Path.Combine(this.DirectoryPath,"build.dll");
            File.WriteAllBytes(this.Loaded,LoadedBytes);
            File.WriteAllBytes(this.Build,BuildBytes);
        }

        internal string Loaded { get; }
        internal string Build { get; }

        public void Dispose(){
            if(Directory.Exists(this.DirectoryPath)){
                Directory.Delete(this.DirectoryPath,recursive:true);
            }
        }
    }
}
