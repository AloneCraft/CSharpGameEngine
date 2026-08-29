using System.IO.Compression;
using System.Xml.Linq;
using CSharpGameEngine.ShaderGraph;
namespace CSharpGameEngine.VSExtension.Tests;
public sealed class ShaderGraphEditorTests {
    private static string SubgraphPath=>Path.Combine(AppContext.BaseDirectory,"TestAssets","ItemTemplates","CSharp","GameEngine","ShaderSubgraph","ShaderSubgraph.cgesubgraph");
    [Fact]
    public void Document_DuplicateUndoRedoAndDeletePreserveGraphIntegrity() {
        var Temporary=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".cgesubgraph");
        File.Copy(SubgraphPath,Temporary);
        try {
            var Document=ShaderGraphEditorDocument.Load(Temporary);
            var OriginalIds=Document.GetNodes().Select(Node=>Node.Id).ToArray();
            var Added=Document.Duplicate(OriginalIds);
            Assert.Equal(2,Added.Count);
            Assert.Equal(4,Document.GetNodes().Count);
            Assert.True(Document.Undo());
            Assert.Equal(2,Document.GetNodes().Count);
            Assert.True(Document.Redo());
            Assert.Equal(4,Document.GetNodes().Count);
            Assert.True(Document.Delete(Added));
            Assert.Equal(2,Document.GetNodes().Count);
            Assert.True(Document.Validate().IsValid);
        } finally { File.Delete(Temporary); }
    }
    [Fact]
    public void Document_LayoutOnlyMoveDoesNotChangeSemanticHash() {
        var Document=ShaderGraphEditorDocument.Load(SubgraphPath);
        var Node=Document.GetNodes().First();
        var Before=ShaderGraphCanonicalization.ComputeSemanticHash(Document.Graph);
        Assert.True(Document.MoveNode(Node.Id,900,700));
        var After=ShaderGraphCanonicalization.ComputeSemanticHash(Document.Graph);
        Assert.Equal(Before,After);
    }
    [Fact]
    public void Document_RejectsConnectionToUndeclaredTypedPort() {
        var Document=ShaderGraphEditorDocument.Load(SubgraphPath);
        var Nodes=Document.GetNodes();
        Assert.False(Document.TryConnect(Nodes[0].Id,Nodes[1].Id,"Missing",out var Error));
        Assert.NotEmpty(Error);
    }
    [Fact]
    public void Vsix_ContainsEditorFactoryAndLocalizedGraphTemplates() {
        var VsixPath=Path.Combine(AppContext.BaseDirectory,"TestAssets","CSharpGameEngine.VSExtension.vsix");
        using var Archive=ZipFile.OpenRead(VsixPath);
        foreach(var Culture in new[]{"1033","1041"}) {
            Assert.NotNull(Archive.GetEntry($"ItemTemplates/CSharp/GameEngine/{Culture}/ShaderGraph/ShaderGraph.vstemplate"));
            Assert.NotNull(Archive.GetEntry($"ItemTemplates/CSharp/GameEngine/{Culture}/ShaderSubgraph/ShaderSubgraph.vstemplate"));
        }
        Assert.NotNull(Archive.GetEntry("ShaderGraphTemplates/Stage.cgeshadergraph"));
        Assert.NotNull(Archive.GetEntry("ShaderGraphTemplates/Model.cgeshadergraph"));
        Assert.NotNull(Archive.GetEntry("ShaderGraphTemplates/Triangle.cgeshadergraph"));
        var PkgDef=Archive.GetEntry("CSharpGameEngine.VSExtension.pkgdef");
        Assert.NotNull(PkgDef);
        using var Reader=new StreamReader(PkgDef!.Open());
        var Text=Reader.ReadToEnd();
        Assert.Contains("cgeshadergraph",Text,StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cgesubgraph",Text,StringComparison.OrdinalIgnoreCase);
        Assert.Contains(ShaderGraphEditorFactory.FactoryGuidString,Text,StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public void Templates_RegisterGraphAndSubgraphWizards() {
        var Root=Path.Combine(AppContext.BaseDirectory,"TestAssets","ItemTemplates","CSharp","GameEngine");
        var Graph=XDocument.Load(Path.Combine(Root,"ShaderGraph","ShaderGraph.vstemplate")).ToString();
        var Subgraph=XDocument.Load(Path.Combine(Root,"ShaderSubgraph","ShaderSubgraph.vstemplate")).ToString();
        Assert.Contains(typeof(ShaderGraphTemplateWizard).FullName!,Graph);
        Assert.Contains(typeof(ShaderSubgraphTemplateWizard).FullName!,Subgraph);
    }
}