using System.Security.Cryptography;
using System.Text.Json;
using CSharpGameEngine.FeatureStages;

namespace CSharpGameEngine.FeatureRunner.Tests;

public sealed class GoldenArtifactPolicyValidatorTests {
    [Fact]
    public void RepositoryManifestPassesAndObservationalBaselineIsNotMisclassified(){
        string RepositoryRoot=FeatureStageRepositoryLocator.Find();

        IReadOnlyList<string>Errors=GoldenArtifactPolicyValidator.Validate(RepositoryRoot);

        Assert.Empty(Errors);
        Assert.True(File.Exists(Path.Combine(RepositoryRoot,"Docs","Verification","M0064-Regression-Baseline.json")));
    }

    [Fact]
    public void UnregisteredConventionalGoldenFailsClosed(){
        string Root=CreateRoot();
        try {
            WriteManifest(Root,[]);
            Write(Root,"tests/Golden/result.txt","unreviewed");

            string Error=Assert.Single(GoldenArtifactPolicyValidator.Validate(Root));

            Assert.Contains("not registered",Error,StringComparison.OrdinalIgnoreCase);
        } finally { Directory.Delete(Root,true); }
    }

    [Fact]
    public void RegisteredExactGoldenRequiresPinnedSourceGeneratorDiffAndReview(){
        string Root=CreateRoot();
        try {
            string SourceHash=Write(Root,"src/source.txt","source-v1");
            string GoldenHash=Write(Root,"tests/Goldens/result.bin","golden-v1");
            string DiffHash=Write(Root,"Docs/Verification/result.diff.txt","initial exact-byte review");
            string EvidenceHash=WriteReview(Root,GoldenHash,"0",DiffHash);
            WriteManifest(Root,[CreateEntry(GoldenHash,SourceHash,EvidenceHash,DiffHash,"0","0")]);

            Assert.Empty(GoldenArtifactPolicyValidator.Validate(Root));
        } finally { Directory.Delete(Root,true); }
    }

    [Fact]
    public void HashDriftAndToleranceWideningAreRejected(){
        string Root=CreateRoot();
        try {
            string SourceHash=Write(Root,"src/source.txt","source-v1");
            string GoldenHash=Write(Root,"tests/Goldens/result.bin","golden-v1");
            string DiffHash=Write(Root,"Docs/Verification/result.diff.txt","review");
            string EvidenceHash=WriteReview(Root,GoldenHash,"0.1",DiffHash);
            WriteManifest(Root,[CreateEntry(GoldenHash,SourceHash,EvidenceHash,DiffHash,"0.2","0.1","NumericText")]);
            Write(Root,"tests/Goldens/result.bin","drifted");

            IReadOnlyList<string>Errors=GoldenArtifactPolicyValidator.Validate(Root);

            Assert.Contains(Errors,Error=>Error.Contains("SHA-256 mismatch",StringComparison.Ordinal));
            Assert.Contains(Errors,Error=>Error.Contains("exceeds its reviewed maximumTolerance",StringComparison.Ordinal));
        } finally { Directory.Delete(Root,true); }
    }

    [Fact]
    public void ArtifactOrToleranceUpdateCannotReuseAStaleReviewRecord(){
        string Root=CreateRoot();
        try {
            string SourceHash=Write(Root,"src/source.txt","source-v1");
            string OriginalHash=Write(Root,"tests/Goldens/result.bin","golden-v1");
            string DiffHash=Write(Root,"Docs/Verification/result.diff.txt","review-v1");
            string EvidenceHash=WriteReview(Root,OriginalHash,"0.1",DiffHash);
            string UpdatedHash=Write(Root,"tests/Goldens/result.bin","golden-v2");
            WriteManifest(Root,[CreateEntry(UpdatedHash,SourceHash,EvidenceHash,DiffHash,"0.2","0.2","NumericText")]);

            IReadOnlyList<string>Errors=GoldenArtifactPolicyValidator.Validate(Root);

            Assert.Contains(Errors,Error=>Error.Contains("stale artifact SHA-256",StringComparison.Ordinal));
            Assert.Contains(Errors,Error=>Error.Contains("stale maximumTolerance",StringComparison.Ordinal));
        } finally { Directory.Delete(Root,true); }
    }

    private static object CreateEntry(string GoldenHash,string SourceHash,string EvidenceHash,string DiffHash,string Tolerance,string MaximumTolerance,string Kind="ExactBytes")=>new {
        id="result",
        path="tests/Goldens/result.bin",
        sha256=GoldenHash,
        sources=new[]{new {path="src/source.txt",sha256=SourceHash}},
        generator=new {fileName="dotnet",arguments=new[]{"run","--","generate-result"},workingDirectory=".",deterministic=true,environment=new Dictionary<string,string>()},
        comparison=new {kind=Kind,metric=Kind=="ExactBytes"?"bytes":"absolute-error",tolerance=Tolerance,maximumTolerance=MaximumTolerance,diff=new {format=Kind=="ExactBytes"?"BinarySummary":"NumericTable",command="dotnet run -- diff-result"}},
        review=new {requiredReviewerRole="engine-maintainer",changeReference="contract-test",evidencePath="Docs/Verification/result.review.json",evidenceSha256=EvidenceHash,diffEvidencePath="Docs/Verification/result.diff.txt",diffEvidenceSha256=DiffHash},
    };

    private static string WriteReview(string Root,string GoldenHash,string MaximumTolerance,string DiffHash)=>Write(
        Root,"Docs/Verification/result.review.json",JsonSerializer.Serialize(new {
            schema=GoldenArtifactPolicyValidator.ReviewSchema,
            artifactId="result",
            artifactSha256=GoldenHash,
            maximumTolerance=MaximumTolerance,
            requiredReviewerRole="engine-maintainer",
            changeReference="contract-test",
            diffSha256=DiffHash,
            decision="Approved",
        }));

    private static string CreateRoot(){
        string Root=Path.Combine(Path.GetTempPath(),"cge-golden-policy-tests",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        return Root;
    }

    private static string Write(string Root,string RelativePath,string Content){
        string FullPath=Path.Combine(Root,RelativePath.Replace('/',Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(FullPath)!);
        File.WriteAllText(FullPath,Content);
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(FullPath))).ToLowerInvariant();
    }

    private static void WriteManifest(string Root,IReadOnlyList<object>Artifacts){
        string PathValue=Path.Combine(Root,"Docs","Verification","Golden-Artifacts.json");
        Directory.CreateDirectory(Path.GetDirectoryName(PathValue)!);
        File.WriteAllText(PathValue,JsonSerializer.Serialize(new {schema=GoldenArtifactPolicyValidator.Schema,artifacts=Artifacts}));
    }
}
