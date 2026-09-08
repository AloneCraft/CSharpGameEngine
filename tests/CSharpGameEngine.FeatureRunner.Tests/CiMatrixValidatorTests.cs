using System.Text.Json.Nodes;
using CSharpGameEngine.FeatureStages;

namespace CSharpGameEngine.FeatureRunner.Tests;

public sealed class CiMatrixValidatorTests {
    [Fact]
    public void RepositoryMatrixAndWorkflowAgree(){
        string RepositoryRoot=FeatureStageRepositoryLocator.Find();

        Assert.Empty(CiMatrixValidator.Validate(RepositoryRoot,FeatureStageCatalog.CreateDefault()));
    }

    [Fact]
    public void MissingRequiredReleaseCellFailsClosed(){
        string Root=CopyContractFiles();
        try {
            string ManifestPath=Path.Combine(Root,"Docs","Verification","CI-Matrix.json");
            JsonNode Document=JsonNode.Parse(File.ReadAllText(ManifestPath))!;
            JsonArray Cells=Document["cells"]!.AsArray();
            JsonNode Missing=Cells.Single(Cell=>Cell!["id"]!.GetValue<string>()=="warp-release")!;
            Cells.Remove(Missing);
            File.WriteAllText(ManifestPath,Document.ToJsonString());

            IReadOnlyList<string>Errors=CiMatrixValidator.Validate(Root,FeatureStageCatalog.CreateDefault());

            Assert.Contains(Errors,Error=>Error.Contains("Missing required CI cell: warp-release",StringComparison.Ordinal));
        } finally { Directory.Delete(Root,true); }
    }

    [Fact]
    public void WorkflowCellDriftAndContinueOnErrorAreRejected(){
        string Root=CopyContractFiles();
        try {
            string WorkflowPath=Path.Combine(Root,".github","workflows","ci.yml");
            string Workflow=File.ReadAllText(WorkflowPath).Replace("targetFramework: net472","targetFramework: net471",StringComparison.Ordinal)+Environment.NewLine+"continue-on-error: true";
            File.WriteAllText(WorkflowPath,Workflow);

            IReadOnlyList<string>Errors=CiMatrixValidator.Validate(Root,FeatureStageCatalog.CreateDefault());

            Assert.Contains(Errors,Error=>Error.Contains("targetFramework must be net472",StringComparison.Ordinal));
            Assert.Contains(Errors,Error=>Error.Contains("cannot continue on error",StringComparison.Ordinal));
        } finally { Directory.Delete(Root,true); }
    }

    private static string CopyContractFiles(){
        string RepositoryRoot=FeatureStageRepositoryLocator.Find();
        string Root=Path.Combine(Path.GetTempPath(),"cge-ci-matrix-tests",Guid.NewGuid().ToString("N"));
        string ManifestDirectory=Path.Combine(Root,"Docs","Verification");
        string WorkflowDirectory=Path.Combine(Root,".github","workflows");
        Directory.CreateDirectory(ManifestDirectory);
        Directory.CreateDirectory(WorkflowDirectory);
        File.Copy(Path.Combine(RepositoryRoot,"Docs","Verification","CI-Matrix.json"),Path.Combine(ManifestDirectory,"CI-Matrix.json"));
        File.Copy(Path.Combine(RepositoryRoot,".github","workflows","ci.yml"),Path.Combine(WorkflowDirectory,"ci.yml"));
        return Root;
    }
}
