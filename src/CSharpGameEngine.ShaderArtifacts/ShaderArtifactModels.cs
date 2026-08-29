namespace CSharpGameEngine.ShaderArtifacts;
public enum ShaderArtifactTargetKind:byte {
    Unknown=0,
    Stage=1,
    Model=2,
    Triangle=3,
}
public enum ShaderArtifactEntryStage:byte {
    Unknown=0,
    Vertex=1,
    Pixel=2,
}
public sealed class ShaderCompilerProvenance {
    public ShaderCompilerProvenance(string CompilerName,string CompilerVersion,string DxcVersion) {
        this.CompilerName=CompilerName;
        this.CompilerVersion=CompilerVersion;
        this.DxcVersion=DxcVersion;
    }
    public string CompilerName { get; }
    public string CompilerVersion { get; }
    public string DxcVersion { get; }
}
public sealed class ShaderArtifactEntry {
    public string LogicalName { get; set; }=string.Empty;
    public string Profile { get; set; }=string.Empty;
    public ShaderArtifactEntryStage Stage { get; set; }
    public byte[]Dxil { get; set; }=[];
    public byte[]Reflection { get; set; }=[];
    public string PsoKey { get; set; }=string.Empty;
}
public sealed class ShaderArtifactDocument {
    public ShaderArtifactTargetKind Target { get; set; }
    public string AbiHash { get; set; }=string.Empty;
    public string SemanticHash { get; set; }=string.Empty;
    public string DependencyHash { get; set; }=string.Empty;
    public ShaderCompilerProvenance CompilerProvenance { get; set; }=new("","","");
    public System.Collections.Generic.IReadOnlyList<ShaderArtifactEntry>Entries { get; set; }=new System.Collections.Generic.List<ShaderArtifactEntry>();
}