namespace CSharpGameEngine.Cooking;
/// <summary>レジストリーフィンガープリントとワーカー呼び出しで共通する、既定値設定／検証規則。</summary>
public static class ImportOptions {
    public static IReadOnlyDictionary<string,string>Resolve(ImporterDescriptor Importer,IEnumerable<ModelProcessorDescriptor>Processors,IReadOnlyDictionary<string,string>Values){
        var Declared=new Dictionary<string,ImportOptionDescriptor>(StringComparer.Ordinal);
        foreach(var Option in Importer.Options.Concat(Processors.SelectMany(Processor=>Processor.Options))){
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
