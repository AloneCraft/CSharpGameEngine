namespace CSharpGameEngine.Destruction;
/// <summary>
/// Where cooked fracture assets live, what their names may be and the defaults of the cut (ADR-0078/M0113). The plugin's Runtime and its Cook module both
/// compile this file, so the scene's <c>Destructible</c> and the cook agree on a member a scene leaves out.
/// </summary>
public static class DestructionDefaults {
    public const string Folder="Assets/Destruction";
    public const string Extension=".cgefracture";
    public const string DestructibleType="CSharpGameEngine.Destruction.Destructible";
    public const int FragmentCount=16;
    public const int Seed=0;
    public const int ClusterCount=0;
    /// <summary>Levels of clusters (M0124): 1 is one level of clusters over the fragments, as M0113.</summary>
    public const int ClusterLevels=1;
    /// <summary>kg/m³.</summary>
    public const int Density=500;
    /// <summary>N·s per m² of bond.</summary>
    public const int BondStrength=2000;
    /// <summary>The content path of a cooked asset.</summary>
    public static string AssetPath(string Name)=>Folder+"/"+Name+Extension;
    /// <summary>A name is a file name: 1 to 64 of letters, digits and <c>-_.</c>, not starting with a dot.</summary>
    public static bool IsValidName(string?Name)=>Name is { Length:>0 and <=64 }&&Name.All(static Character=>char.IsAsciiLetterOrDigit(Character)||Character is '-' or '_' or '.')&&Name[0]!='.';
}
