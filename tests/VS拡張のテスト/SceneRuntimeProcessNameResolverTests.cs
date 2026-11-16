namespace CSharpGameEngine.VSExtension.Tests;
public sealed class SceneRuntimeProcessNameResolverTests {
    [Fact]
    public void Resolve_UsesProjectAssemblyNameInsteadOfSceneClassName(){
        SceneKey Key=new("project-id","global::Game.FirstPersonStageScene");
        string?ProcessName=SceneRuntimeProcessNameResolver.Resolve([
            new SceneRuntimeProjectAssembly("project-id","UserEcsProject")],Key);
        Assert.Equal("UserEcsProject",ProcessName);
        Assert.NotEqual(Key.ClassFullyQualifiedName,ProcessName);
    }
    [Fact]
    public void Resolve_ReturnsNullWhenTheSceneProjectIsNotAvailable(){
        Assert.Null(SceneRuntimeProcessNameResolver.Resolve([
            new SceneRuntimeProjectAssembly("other-project","OtherGame")],new SceneKey("project-id","global::Game.Scene")));
    }
}