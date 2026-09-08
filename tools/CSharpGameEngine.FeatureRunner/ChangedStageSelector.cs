namespace CSharpGameEngine.FeatureStages;

public static class ChangedStageSelector {
    private static readonly HashSet<string>GlobalBuildInputs=new(StringComparer.OrdinalIgnoreCase){
        "CSharpGameEngine.slnx",
        "Directory.Build.props",
        "Directory.Build.targets",
        "Directory.Packages.props",
        "global.json",
    };

    public static IReadOnlyList<FeatureStageDefinition>Select(
        FeatureStageCatalog Catalog,IReadOnlyList<string>ChangedPaths){
        ArgumentNullException.ThrowIfNull(Catalog);
        ArgumentNullException.ThrowIfNull(ChangedPaths);
        string[]Normalized=[.. ChangedPaths.Select(Normalize).Where(static Path=>Path.Length>0)
            .Distinct(StringComparer.OrdinalIgnoreCase)];
        bool HasGlobalInput=Normalized.Any(GlobalBuildInputs.Contains);
        FeatureStageDefinition[]Selected=[.. Catalog.Stages
            .Where(static Stage=>Stage.Availability==FeatureStageAvailability.Executable)
            .Where(static Stage=>Stage.Profiles.Contains("changed",StringComparer.OrdinalIgnoreCase))
            .Where(Stage=>HasGlobalInput||Normalized.Any(Path=>
                Stage.InputPaths.Any(Input=>IsSameOrChild(Path,Normalize(Input)))))];
        return Selected.Length>0?Selected:[Catalog.GetStage("runner.contract")];
    }

    private static bool IsSameOrChild(string Path,string Input){
        if(Input.Length==0){ return false; }
        return Path.Equals(Input,StringComparison.OrdinalIgnoreCase)
            ||Path.Length>Input.Length&&Path.StartsWith(Input,StringComparison.OrdinalIgnoreCase)
                &&Path[Input.Length]=='/';
    }

    private static string Normalize(string PathValue){
        if(string.IsNullOrWhiteSpace(PathValue)){ return string.Empty; }
        string Path=PathValue.Trim().Replace((char)92,'/');
        while(Path.StartsWith("./",StringComparison.Ordinal)){ Path=Path[2..]; }
        return Path.Trim('/');
    }
}
