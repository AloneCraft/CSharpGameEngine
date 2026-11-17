namespace CSharpGameEngine.Cooking;
/// <summary>レジストリーフィンガープリントとワーカー呼び出しで共通する、既定値設定／検証規則。</summary>
public static class ImportOptions {
    public const string LicenseKind="license.kind";
    public const string LicenseConfirmed="license.confirmed";
    public const string LicenseCredit="license.credit";
    public const string LicenseSourceUrl="license.sourceUrl";
    public const string LicenseRestrictions="license.restrictions";
    /// <summary>
    /// どのインポーターでも <c>.meta</c> に書けるエンジンの option（ADR-0080 §9 のライセンスの申告）。<see cref="LicenseKind"/> が既定の "Unknown" のままなら、
    /// インポーターが読み取った権利（VRM の meta など）をそのまま使う。制限はカンマ区切り（UE-Only、UnityCompanion、ModificationProhibited など）。
    /// </summary>
    public static readonly System.Collections.Immutable.ImmutableArray<ImportOptionDescriptor>EngineOptions=[
        new(LicenseKind,"Unknown","The licence of the source, such as UnrealEngineEula or CC-BY-4.0.",[]),
        new(LicenseConfirmed,"false","Whether the owner confirmed that the licence allows this use.",["false","true"]),
        new(LicenseCredit,string.Empty,"The credit the licence asks for.",[]),
        new(LicenseSourceUrl,string.Empty,"Where the licence or the listing is.",[]),
        new(LicenseRestrictions,string.Empty,"Comma-separated restrictions, such as UE-Only or ModificationProhibited.",[]),
    ];
    public static IReadOnlyDictionary<string,string>Resolve(ImporterDescriptor Importer,IEnumerable<ModelProcessorDescriptor>Processors,IReadOnlyDictionary<string,string>Values){
        var Declared=new Dictionary<string,ImportOptionDescriptor>(StringComparer.Ordinal);
        foreach(var Option in EngineOptions.Concat(Importer.Options).Concat(Processors.SelectMany(Processor=>Processor.Options))){
            if(string.IsNullOrWhiteSpace(Option.Name)||Option.DefaultValue is null||Option.AllowedValues.IsDefault||!Declared.TryAdd(Option.Name,Option)){
                throw new InvalidDataException("IMP004: missing or duplicated option schema.");
            }
        }
        foreach(var Key in Values.Keys){ if(!Declared.ContainsKey(Key)){ throw new InvalidDataException($"IMP004: unknown option '{Key}'."); } }
        var Result=new SortedDictionary<string,string>(StringComparer.Ordinal);
        foreach(var (Name,Option) in Declared){
            var Value=Values.TryGetValue(Name,out var Explicit)?Explicit:Option.DefaultValue;
            if(Value is null||(Option.AllowedValues.Length>0&&!Option.AllowedValues.Contains(Value,StringComparer.Ordinal))){ throw new InvalidDataException($"IMP004: invalid value for option '{Name}'."); }
            Result.Add(Name,Value);
        }
        if(Result.Count>1024){ throw new InvalidDataException("IMP004: resolved options exceed their limit."); }
        return new System.Collections.ObjectModel.ReadOnlyDictionary<string,string>(Result);
    }
}
