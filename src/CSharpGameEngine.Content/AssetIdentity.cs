using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.Content;

public readonly record struct AssetUri(AssetKey Key) {
    public const string Scheme="asset://";
    public bool IsValid=>this.Key.IsValid;

    public static bool TryParse(string?Value,out AssetUri Uri,out string Error){
        Uri=default;
        Error=string.Empty;
        if(Value is null||!Value.StartsWith(Scheme,StringComparison.Ordinal)){
            Error="Asset URI must start with 'asset://'.";
            return false;
        }
        string Identity=Value.Substring(Scheme.Length);
        if(Identity.Length==0||Identity.Contains('?')||Identity.Contains('#')||Identity.EndsWith("/",StringComparison.Ordinal)){
            Error="Asset URI contains an empty identity, query, fragment, or trailing separator.";
            return false;
        }
        string[]Parts=Identity.Split('/');
        if(Parts.Length is <1 or >2||!AssetId.TryParse(Parts[0],out var Asset)){
            Error="Asset URI contains an invalid AssetId.";
            return false;
        }
        SubAssetId?Sub=null;
        if(Parts.Length==2){
            if(!Hash256.TryParse(Parts[1],out var Hash)||!Hash.IsValid){
                Error="Asset URI contains an invalid SubAssetId.";
                return false;
            }
            Sub=new SubAssetId(Hash);
        }
        Uri=new AssetUri(new AssetKey(Asset,Sub));
        return true;
    }

    public static AssetUri Parse(string Value){
        if(!TryParse(Value,out var Uri,out var Error)){ throw new FormatException(Error); }
        return Uri;
    }
    public override string ToString()=>Scheme+this.Key;
}

public enum AssetMetadataErrorCode {
    None,
    InvalidJson,
    DuplicateField,
    UnknownField,
    MissingField,
    UnsupportedSchema,
    InvalidAssetId,
    InvalidValue,
}

public readonly record struct AssetMetadataError(AssetMetadataErrorCode Code,string Field,string Message) {
    public static AssetMetadataError None=>new(AssetMetadataErrorCode.None,string.Empty,string.Empty);
}

/// <summary>正本アセットの隣に保存する、正規形式のUTF-8メタデータ。</summary>
public sealed class AssetMetadata {
    public const int CurrentSchemaVersion=1;
    private readonly ImmutableSortedDictionary<string,string>OptionsValue;

    private AssetMetadata(
        AssetId AssetId,string SourceUri,string Importer,
        ImmutableSortedDictionary<string,string>Options){
        this.AssetId=AssetId;
        this.SourceUri=SourceUri;
        this.Importer=Importer;
        this.OptionsValue=Options;
    }

    public AssetId AssetId{get;}
    public string SourceUri{get;}
    public string Importer{get;}
    public IReadOnlyDictionary<string,string>Options=>this.OptionsValue;

    public static AssetMetadata Create(
        AssetId AssetId,string SourceUri,string Importer,
        IEnumerable<KeyValuePair<string,string>>?Options=null){
        if(!AssetId.IsValid){ throw new ArgumentException("A valid AssetId is required.",nameof(AssetId)); }
        string CanonicalSource=NormalizeSourceUri(SourceUri);
        string CanonicalImporter=NormalizeRequiredText(Importer,nameof(Importer));
        var Builder=ImmutableSortedDictionary.CreateBuilder<string,string>(StringComparer.Ordinal);
        if(Options is not null){
            foreach(var Pair in Options){
                string Key=NormalizeRequiredText(Pair.Key,nameof(Options));
                if(Builder.ContainsKey(Key)){ throw new ArgumentException($"Duplicate metadata option '{Key}'.",nameof(Options)); }
                Builder.Add(Key,Pair.Value??string.Empty);
            }
        }
        return new AssetMetadata(AssetId,CanonicalSource,CanonicalImporter,Builder.ToImmutable());
    }

    public byte[] ToCanonicalUtf8(){
        using var Stream=new MemoryStream();
        using(var Writer=new Utf8JsonWriter(Stream,new JsonWriterOptions {Indented=false})){
            Writer.WriteStartObject();
            Writer.WriteNumber("schemaVersion",CurrentSchemaVersion);
            Writer.WriteString("assetId",this.AssetId.ToString());
            Writer.WriteString("sourceUri",this.SourceUri);
            Writer.WriteString("importer",this.Importer);
            Writer.WritePropertyName("options");
            Writer.WriteStartObject();
            foreach(var Pair in this.OptionsValue){ Writer.WriteString(Pair.Key,Pair.Value); }
            Writer.WriteEndObject();
            Writer.WriteEndObject();
        }
        return Stream.ToArray();
    }

    public static bool TryParse(
        byte[]Utf8,out AssetMetadata?Metadata,out AssetMetadataError Error){
        Metadata=null;
        Error=AssetMetadataError.None;
        if(Utf8 is null){ Error=CreateError(AssetMetadataErrorCode.InvalidJson,string.Empty,"Metadata bytes are required.");return false; }
        try {
            using JsonDocument Document=JsonDocument.Parse(Utf8,new JsonDocumentOptions {
                AllowTrailingCommas=false,
                CommentHandling=JsonCommentHandling.Disallow,
                MaxDepth=16,
            });
            if(Document.RootElement.ValueKind!=JsonValueKind.Object){
                Error=CreateError(AssetMetadataErrorCode.InvalidJson,string.Empty,"Metadata root must be an object.");
                return false;
            }
            int?Schema=null;
            AssetId Asset=default;
            string?Source=null;
            string?Importer=null;
            var Options=ImmutableSortedDictionary.CreateBuilder<string,string>(StringComparer.Ordinal);
            var Seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(JsonProperty Property in Document.RootElement.EnumerateObject()){
                if(!Seen.Add(Property.Name)){
                    Error=CreateError(AssetMetadataErrorCode.DuplicateField,Property.Name,"Metadata contains a duplicate field.");
                    return false;
                }
                switch(Property.Name){
                    case "schemaVersion":
                        if(Property.Value.ValueKind!=JsonValueKind.Number||!Property.Value.TryGetInt32(out var Version)){
                            Error=CreateError(AssetMetadataErrorCode.InvalidValue,Property.Name,"schemaVersion must be an integer.");
                            return false;
                        }
                        Schema=Version;
                        break;
                    case "assetId":
                        if(Property.Value.ValueKind!=JsonValueKind.String
                            ||!AssetId.TryParse(Property.Value.GetString(),out Asset)||!Asset.IsValid){
                            Error=CreateError(AssetMetadataErrorCode.InvalidAssetId,Property.Name,"assetId is invalid.");
                            return false;
                        }
                        break;
                    case "sourceUri":
                        if(Property.Value.ValueKind!=JsonValueKind.String){ Error=CreateError(AssetMetadataErrorCode.InvalidValue,Property.Name,"sourceUri must be a string.");return false; }
                        Source=Property.Value.GetString();
                        break;
                    case "importer":
                        if(Property.Value.ValueKind!=JsonValueKind.String){ Error=CreateError(AssetMetadataErrorCode.InvalidValue,Property.Name,"importer must be a string.");return false; }
                        Importer=Property.Value.GetString();
                        break;
                    case "options":
                        if(!TryReadOptions(Property.Value,Options,out Error)){ return false; }
                        break;
                    default:
                        Error=CreateError(AssetMetadataErrorCode.UnknownField,Property.Name,"Metadata contains an unknown field.");
                        return false;
                }
            }
            if(Schema is null||!Asset.IsValid||Source is null||Importer is null||!Seen.Contains("options")){
                Error=CreateError(AssetMetadataErrorCode.MissingField,string.Empty,"Metadata requires schemaVersion, assetId, sourceUri, importer, and options.");
                return false;
            }
            if(Schema!=CurrentSchemaVersion){
                Error=CreateError(AssetMetadataErrorCode.UnsupportedSchema,"schemaVersion",$"Unsupported metadata schema {Schema}.");
                return false;
            }
            try {
                Metadata=Create(Asset,Source,Importer,Options);
                if(!Utf8.AsSpan().SequenceEqual(Metadata.ToCanonicalUtf8())){
                    Metadata=null;
                    Error=CreateError(AssetMetadataErrorCode.InvalidValue,"canonical","Metadata JSON is valid but not canonically encoded.");
                    return false;
                }
                return true;
            }catch(ArgumentException Exception){
                Error=CreateError(AssetMetadataErrorCode.InvalidValue,string.Empty,Exception.Message);
                return false;
            }
        }catch(JsonException Exception){
            Error=CreateError(AssetMetadataErrorCode.InvalidJson,string.Empty,Exception.Message);
            return false;
        }
    }

    private static bool TryReadOptions(
        JsonElement Element,
        ImmutableSortedDictionary<string,string>.Builder Options,
        out AssetMetadataError Error){
        Error=AssetMetadataError.None;
        if(Element.ValueKind!=JsonValueKind.Object){
            Error=CreateError(AssetMetadataErrorCode.InvalidValue,"options","options must be an object.");
            return false;
        }
        foreach(JsonProperty Option in Element.EnumerateObject()){
            if(Option.Value.ValueKind!=JsonValueKind.String){
                Error=CreateError(AssetMetadataErrorCode.InvalidValue,"options."+Option.Name,"Metadata option values must be strings.");
                return false;
            }
            if(Options.ContainsKey(Option.Name)){
                Error=CreateError(AssetMetadataErrorCode.DuplicateField,"options."+Option.Name,"Metadata contains a duplicate option.");
                return false;
            }
            if(string.IsNullOrWhiteSpace(Option.Name)){
                Error=CreateError(AssetMetadataErrorCode.InvalidValue,"options","Metadata option names cannot be empty.");
                return false;
            }
            Options.Add(Option.Name,Option.Value.GetString()??string.Empty);
        }
        return true;
    }

    private static string NormalizeSourceUri(string Value){
        string Result=NormalizeRequiredText(Value,nameof(Value)).Replace('\\','/');
        if(Result.Contains('\r')||Result.Contains('\n')){ throw new ArgumentException("Source URI cannot contain a line break.",nameof(Value)); }
        return Result;
    }
    private static string NormalizeRequiredText(string Value,string ParameterName){
        if(string.IsNullOrWhiteSpace(Value)){ throw new ArgumentException("A non-empty value is required.",ParameterName); }
        return Value.Trim();
    }
    private static AssetMetadataError CreateError(AssetMetadataErrorCode Code,string Field,string Message)=>new(Code,Field,Message);
}
