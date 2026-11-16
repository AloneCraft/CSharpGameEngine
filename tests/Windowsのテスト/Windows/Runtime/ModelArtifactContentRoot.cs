using Xunit;
using CSharpGameEngine.Numerics;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Testing;
namespace CSharpGameEngine.Windows.Tests.Runtime;
internal sealed class ModelArtifactContentRoot:IDisposable {
    private readonly string Directory=ContentRoot.Directory;
    private readonly string Models=ContentRoot.ModelDirectory;
    internal ModelArtifactContentRoot(ModelArtifactTestContent Content){ ContentRoot.Use(Content.ProjectDirectory,Content.ContentDirectory); }
    public void Dispose(){ CSharpGameEngine.Runtime.Animation.SkeletalAssetCache.Clear();ContentRoot.Use(this.Directory,this.Models); }
}
