using System.Security.Cryptography;
using System.Text.Json;

namespace CSharpGameEngine.FeatureStages;

public static class GoldenArtifactPolicyValidator {
    public const string Schema="csharp-game-engine-golden-manifest-v1";
    public const string ReviewSchema="csharp-game-engine-golden-review-v1";
    private static readonly HashSet<string>ExcludedDirectoryNames=new(StringComparer.OrdinalIgnoreCase){".git",".vs",".artifacts","bin","obj"};
    private static readonly JsonSerializerOptions JsonOptions=new(){PropertyNameCaseInsensitive=true};

    public static IReadOnlyList<string>Validate(string RepositoryRoot){
        ArgumentException.ThrowIfNullOrWhiteSpace(RepositoryRoot);
        var Root=Path.GetFullPath(RepositoryRoot);
        var Errors=new List<string>();
        var ManifestPath=Path.Combine(Root,"Docs","Verification","Golden-Artifacts.json");
        if(!File.Exists(ManifestPath)){ return [$"Golden manifest is missing: {ToRepositoryPath(Root,ManifestPath)}"]; }
        GoldenArtifactManifest?Manifest;
        try {
            Manifest=JsonSerializer.Deserialize<GoldenArtifactManifest>(File.ReadAllText(ManifestPath),JsonOptions);
        } catch(Exception Ex)when(Ex is JsonException or IOException or UnauthorizedAccessException){
            return [$"Golden manifest cannot be read: {Ex.Message}"];
        }
        if(Manifest is null){ return ["Golden manifest is empty."]; }
        if(!string.Equals(Manifest.Schema,Schema,StringComparison.Ordinal)){ Errors.Add($"Golden manifest schema must be '{Schema}'."); }
        var Entries=Manifest.Artifacts??[];
        var RegisteredPaths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var RegisteredIds=new HashSet<string>(StringComparer.Ordinal);
        foreach(var Entry in Entries){ ValidateEntry(Root,Entry,RegisteredIds,RegisteredPaths,Errors); }
        foreach(var GoldenPath in DiscoverGoldenPaths(Root)){
            if(!RegisteredPaths.Contains(GoldenPath)){ Errors.Add($"Golden artifact is not registered: {GoldenPath}"); }
        }
        return Errors;
    }

    private static void ValidateEntry(string Root,GoldenArtifactEntry?Entry,HashSet<string>Ids,HashSet<string>Paths,List<string>Errors){
        if(Entry is null){ Errors.Add("Golden manifest contains a null artifact entry.");return; }
        var Label=string.IsNullOrWhiteSpace(Entry.Id)?"<missing-id>":Entry.Id;
        if(string.IsNullOrWhiteSpace(Entry.Id)){ Errors.Add("Golden artifact ID is required."); }
        else if(!Ids.Add(Entry.Id)){ Errors.Add($"Golden artifact ID is duplicated: {Entry.Id}"); }
        if(!TryResolvePath(Root,Entry.Path,out var ArtifactPath,out var RepositoryPath,out var PathError)){
            Errors.Add($"Golden artifact {Label} path is invalid: {PathError}");
        } else {
            if(!IsGoldenConventionPath(RepositoryPath)){ Errors.Add($"Golden artifact {Label} must use a .golden. filename or a Golden/Goldens directory: {RepositoryPath}"); }
            if(!Paths.Add(RepositoryPath)){ Errors.Add($"Golden artifact path is duplicated: {RepositoryPath}"); }
            ValidateHashedFile(ArtifactPath,Entry.Sha256,$"Golden artifact {Label}",Errors);
        }
        if(Entry.Sources is null||Entry.Sources.Count==0){ Errors.Add($"Golden artifact {Label} requires at least one source with pinned provenance."); }
        else {
            var Sources=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var Source in Entry.Sources){
                if(Source is null){ Errors.Add($"Golden artifact {Label} source entry is null.");continue; }
                if(!TryResolvePath(Root,Source.Path,out var SourcePath,out var SourceRepositoryPath,out var SourceError)){
                    Errors.Add($"Golden artifact {Label} source path is invalid: {SourceError}");
                    continue;
                }
                if(!Sources.Add(SourceRepositoryPath)){ Errors.Add($"Golden artifact {Label} source is duplicated: {SourceRepositoryPath}"); }
                ValidateHashedFile(SourcePath,Source.Sha256,$"Golden artifact {Label} source {SourceRepositoryPath}",Errors);
            }
        }
        ValidateGenerator(Root,Label,Entry.Generator,Errors);
        ValidateComparison(Label,Entry.Comparison,Errors);
        ValidateReview(Root,Label,Entry.Sha256,Entry.Comparison?.MaximumTolerance,Entry.Review,Errors);
    }

    private static void ValidateGenerator(string Root,string Label,GoldenArtifactGenerator?Generator,List<string>Errors){
        if(Generator is null){ Errors.Add($"Golden artifact {Label} requires a generator contract.");return; }
        if(!Generator.Deterministic){ Errors.Add($"Golden artifact {Label} generator must declare deterministic=true."); }
        if(string.IsNullOrWhiteSpace(Generator.FileName)){ Errors.Add($"Golden artifact {Label} generator fileName is required."); }
        if(Generator.Arguments is null||Generator.Arguments.Count==0||Generator.Arguments.Any(string.IsNullOrWhiteSpace)){ Errors.Add($"Golden artifact {Label} generator arguments must be explicit and non-empty."); }
        if(!TryResolveDirectory(Root,Generator.WorkingDirectory,out var DirectoryError)){ Errors.Add($"Golden artifact {Label} generator workingDirectory is invalid: {DirectoryError}"); }
        if(Generator.Environment is null){ Errors.Add($"Golden artifact {Label} generator environment must be explicit, even when empty."); }
        else if(Generator.Environment.Any(Pair=>string.IsNullOrWhiteSpace(Pair.Key)||Pair.Value is null)){ Errors.Add($"Golden artifact {Label} generator environment contains an invalid entry."); }
    }

    private static void ValidateComparison(string Label,GoldenArtifactComparison?Comparison,List<string>Errors){
        if(Comparison is null){ Errors.Add($"Golden artifact {Label} requires a comparison contract.");return; }
        if(Comparison.Kind is not("ExactBytes" or "NumericText")){ Errors.Add($"Golden artifact {Label} comparison kind must be ExactBytes or NumericText.");return; }
        if(string.IsNullOrWhiteSpace(Comparison.Metric)){ Errors.Add($"Golden artifact {Label} comparison metric is required."); }
        if(!TryCanonicalNonNegativeDecimal(Comparison.Tolerance,out var Tolerance)){ Errors.Add($"Golden artifact {Label} tolerance must be a canonical non-negative decimal string.");return; }
        if(!TryCanonicalNonNegativeDecimal(Comparison.MaximumTolerance,out var Maximum)){ Errors.Add($"Golden artifact {Label} maximumTolerance must be a canonical non-negative decimal string.");return; }
        if(CompareDecimal(Tolerance,Maximum)>0){ Errors.Add($"Golden artifact {Label} tolerance exceeds its reviewed maximumTolerance."); }
        if(Comparison.Kind=="ExactBytes"&&(Tolerance!="0"||Maximum!="0")){ Errors.Add($"Golden artifact {Label} ExactBytes comparison requires zero tolerance and maximumTolerance."); }
        if(Comparison.Diff is null||Comparison.Diff.Format is not("UnifiedText" or "BinarySummary" or "NumericTable")||string.IsNullOrWhiteSpace(Comparison.Diff.Command)){
            Errors.Add($"Golden artifact {Label} requires an explicit reviewable diff format and command.");
        }
    }

    private static void ValidateReview(string Root,string Label,string ArtifactHash,string?MaximumTolerance,GoldenArtifactReview?Review,List<string>Errors){
        if(Review is null){ Errors.Add($"Golden artifact {Label} requires review evidence.");return; }
        if(string.IsNullOrWhiteSpace(Review.RequiredReviewerRole)){ Errors.Add($"Golden artifact {Label} requiredReviewerRole is missing."); }
        if(string.IsNullOrWhiteSpace(Review.ChangeReference)){ Errors.Add($"Golden artifact {Label} changeReference is missing."); }
        if(!TryResolvePath(Root,Review.EvidencePath,out var EvidencePath,out _,out var PathError)){
            Errors.Add($"Golden artifact {Label} review evidence path is invalid: {PathError}");
            return;
        }
        ValidateHashedFile(EvidencePath,Review.EvidenceSha256,$"Golden artifact {Label} review evidence",Errors);
        if(!TryResolvePath(Root,Review.DiffEvidencePath,out var DiffPath,out _,out var DiffPathError)){
            Errors.Add($"Golden artifact {Label} diff evidence path is invalid: {DiffPathError}");
            return;
        }
        ValidateHashedFile(DiffPath,Review.DiffEvidenceSha256,$"Golden artifact {Label} diff evidence",Errors);
        GoldenArtifactReviewRecord?Record;
        try { Record=JsonSerializer.Deserialize<GoldenArtifactReviewRecord>(File.ReadAllText(EvidencePath),JsonOptions); }
        catch(Exception Ex)when(Ex is JsonException or IOException or UnauthorizedAccessException){ Errors.Add($"Golden artifact {Label} review record cannot be read: {Ex.Message}");return; }
        if(Record is null){ Errors.Add($"Golden artifact {Label} review record is empty.");return; }
        if(Record.Schema!=ReviewSchema){ Errors.Add($"Golden artifact {Label} review schema must be {ReviewSchema}."); }
        if(Record.ArtifactId!=Label){ Errors.Add($"Golden artifact {Label} review record artifactId does not match."); }
        if(!string.Equals(Record.ArtifactSha256,ArtifactHash,StringComparison.OrdinalIgnoreCase)){ Errors.Add($"Golden artifact {Label} review record has a stale artifact SHA-256."); }
        if(Record.MaximumTolerance!=MaximumTolerance){ Errors.Add($"Golden artifact {Label} review record has a stale maximumTolerance."); }
        if(Record.RequiredReviewerRole!=Review.RequiredReviewerRole){ Errors.Add($"Golden artifact {Label} review record reviewer role does not match."); }
        if(Record.ChangeReference!=Review.ChangeReference){ Errors.Add($"Golden artifact {Label} review record changeReference does not match."); }
        if(!string.Equals(Record.DiffSha256,Review.DiffEvidenceSha256,StringComparison.OrdinalIgnoreCase)){ Errors.Add($"Golden artifact {Label} review record has a stale diff SHA-256."); }
        if(Record.Decision!="Approved"){ Errors.Add($"Golden artifact {Label} review decision must be Approved."); }
    }

    private static void ValidateHashedFile(string PathValue,string?ExpectedHash,string Label,List<string>Errors){
        if(!File.Exists(PathValue)){ Errors.Add($"{Label} file is missing: {PathValue}");return; }
        if(!IsSha256(ExpectedHash)){ Errors.Add($"{Label} SHA-256 must contain 64 hexadecimal characters.");return; }
        string Actual;
        try { using var Stream=File.OpenRead(PathValue);Actual=Convert.ToHexString(SHA256.HashData(Stream)).ToLowerInvariant(); }
        catch(Exception Ex)when(Ex is IOException or UnauthorizedAccessException){ Errors.Add($"{Label} cannot be hashed: {Ex.Message}");return; }
        if(!string.Equals(Actual,ExpectedHash,StringComparison.OrdinalIgnoreCase)){ Errors.Add($"{Label} SHA-256 mismatch. Expected {ExpectedHash}, actual {Actual}."); }
    }

    private static IReadOnlyList<string>DiscoverGoldenPaths(string Root){
        var Result=new List<string>();
        var Pending=new Stack<string>();
        Pending.Push(Root);
        while(Pending.Count>0){
            var DirectoryPath=Pending.Pop();
            foreach(var Child in Directory.EnumerateDirectories(DirectoryPath)){
                if(!ExcludedDirectoryNames.Contains(Path.GetFileName(Child))&&(File.GetAttributes(Child)&FileAttributes.ReparsePoint)==0){ Pending.Push(Child); }
            }
            foreach(var FilePath in Directory.EnumerateFiles(DirectoryPath)){
                var RepositoryPath=ToRepositoryPath(Root,FilePath);
                if(IsGoldenConventionPath(RepositoryPath)){ Result.Add(RepositoryPath); }
            }
        }
        Result.Sort(StringComparer.OrdinalIgnoreCase);
        return Result;
    }

    private static bool IsGoldenConventionPath(string RepositoryPath){
        var Segments=RepositoryPath.Split('/',StringSplitOptions.RemoveEmptyEntries);
        if(Segments.Any(Segment=>Segment.Equals("Golden",StringComparison.OrdinalIgnoreCase)||Segment.Equals("Goldens",StringComparison.OrdinalIgnoreCase))){ return true; }
        return Path.GetFileName(RepositoryPath).Contains(".golden.",StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryResolvePath(string Root,string?Relative,out string FullPath,out string RepositoryPath,out string Error){
        FullPath=string.Empty;RepositoryPath=string.Empty;Error=string.Empty;
        if(string.IsNullOrWhiteSpace(Relative)){ Error="path is required.";return false; }
        if(Path.IsPathRooted(Relative)){ Error="absolute paths are forbidden.";return false; }
        FullPath=Path.GetFullPath(Path.Combine(Root,Relative.Replace('/',Path.DirectorySeparatorChar)));
        var Prefix=Root.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
        if(!FullPath.Equals(Root,StringComparison.OrdinalIgnoreCase)&&!FullPath.StartsWith(Prefix,StringComparison.OrdinalIgnoreCase)){ Error="path escapes the repository root.";return false; }
        if(ContainsReparsePoint(Root,FullPath)){ Error="symbolic links and junctions are forbidden.";return false; }
        RepositoryPath=ToRepositoryPath(Root,FullPath);
        return true;
    }

    private static bool ContainsReparsePoint(string Root,string FullPath){
        string?Current=FullPath;
        while(Current is not null&&!Current.Equals(Root,StringComparison.OrdinalIgnoreCase)){
            if((File.Exists(Current)||Directory.Exists(Current))&&(File.GetAttributes(Current)&FileAttributes.ReparsePoint)!=0){ return true; }
            Current=Path.GetDirectoryName(Current);
        }
        return false;
    }

    private static bool TryResolveDirectory(string Root,string?Relative,out string Error){
        Error=string.Empty;
        if(!TryResolvePath(Root,string.IsNullOrWhiteSpace(Relative)?".":Relative,out var FullPath,out _,out Error)){ return false; }
        if(!Directory.Exists(FullPath)){ Error="directory does not exist.";return false; }
        return true;
    }

    private static string ToRepositoryPath(string Root,string FullPath)=>Path.GetRelativePath(Root,FullPath).Replace(Path.DirectorySeparatorChar,'/');
    private static bool IsSha256(string?Value)=>Value is {Length:64}&&Value.All(static Character=>(Character>='0'&&Character<='9')||(Character>='a'&&Character<='f')||(Character>='A'&&Character<='F'));

    private static bool TryCanonicalNonNegativeDecimal(string?Value,out string Canonical){
        Canonical=string.Empty;
        if(string.IsNullOrWhiteSpace(Value)||Value[0]=='+'||Value[0]=='-'||Value.Contains('e')||Value.Contains('E')){ return false; }
        var Dot=Value.IndexOf('.');
        if(Dot>=0&&Dot!=Value.LastIndexOf('.')){ return false; }
        var Integer=Dot<0?Value:Value[..Dot];
        var Fraction=Dot<0?string.Empty:Value[(Dot+1)..];
        if(Integer.Length==0||Integer.Any(static Character=>Character<'0'||Character>'9')||Fraction.Any(static Character=>Character<'0'||Character>'9')){ return false; }
        if(Dot>=0&&Fraction.Length==0){ return false; }
        var NormalInteger=Integer.TrimStart('0');
        if(NormalInteger.Length==0){ NormalInteger="0"; }
        var NormalFraction=Fraction.TrimEnd('0');
        Canonical=NormalFraction.Length==0?NormalInteger:$"{NormalInteger}.{NormalFraction}";
        return string.Equals(Value,Canonical,StringComparison.Ordinal);
    }

    private static int CompareDecimal(string Left,string Right){
        Split(Left,out var LeftInteger,out var LeftFraction);
        Split(Right,out var RightInteger,out var RightFraction);
        if(LeftInteger.Length!=RightInteger.Length){ return LeftInteger.Length.CompareTo(RightInteger.Length); }
        var IntegerComparison=string.CompareOrdinal(LeftInteger,RightInteger);
        if(IntegerComparison!=0){ return IntegerComparison; }
        var Width=Math.Max(LeftFraction.Length,RightFraction.Length);
        return string.CompareOrdinal(LeftFraction.PadRight(Width,'0'),RightFraction.PadRight(Width,'0'));
    }

    private static void Split(string Value,out string Integer,out string Fraction){
        var Dot=Value.IndexOf('.');
        Integer=Dot<0?Value:Value[..Dot];
        Fraction=Dot<0?string.Empty:Value[(Dot+1)..];
    }

    private sealed record GoldenArtifactManifest(string Schema,IReadOnlyList<GoldenArtifactEntry?>?Artifacts);
    private sealed record GoldenArtifactEntry(string Id,string Path,string Sha256,IReadOnlyList<GoldenArtifactSource?>?Sources,GoldenArtifactGenerator?Generator,GoldenArtifactComparison?Comparison,GoldenArtifactReview?Review);
    private sealed record GoldenArtifactSource(string Path,string Sha256);
    private sealed record GoldenArtifactGenerator(string FileName,IReadOnlyList<string>?Arguments,string WorkingDirectory,bool Deterministic,IReadOnlyDictionary<string,string?>?Environment);
    private sealed record GoldenArtifactComparison(string Kind,string Metric,string Tolerance,string MaximumTolerance,GoldenArtifactDiff?Diff);
    private sealed record GoldenArtifactDiff(string Format,string Command);
    private sealed record GoldenArtifactReview(string RequiredReviewerRole,string ChangeReference,string EvidencePath,string EvidenceSha256,string DiffEvidencePath,string DiffEvidenceSha256);
    private sealed record GoldenArtifactReviewRecord(string Schema,string ArtifactId,string ArtifactSha256,string MaximumTolerance,string RequiredReviewerRole,string ChangeReference,string DiffSha256,string Decision);
}
