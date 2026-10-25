using System.Text;
namespace CSharpGameEngine.Contracts;
public enum SubAssetKind:byte { Mesh,Material,Texture,Skeleton,Clip,DistanceField,License }
public static class SubAssetIdentity {
    public static string KindName(SubAssetKind Kind)=>Kind switch {
        SubAssetKind.Mesh=>"mesh",SubAssetKind.Material=>"material",SubAssetKind.Texture=>"texture",
        SubAssetKind.Skeleton=>"skeleton",SubAssetKind.Clip=>"clip",SubAssetKind.DistanceField=>"sdf",SubAssetKind.License=>"license",
        _=>throw new ArgumentOutOfRangeException(nameof(Kind))
    };
    public static SubAssetId Derive(SubAssetKind Kind,string CanonicalName){
        var Token=KindName(Kind);
        if(CanonicalName is null){ throw new ArgumentNullException(nameof(CanonicalName)); }
        if(CanonicalName.Length==0&&Kind!=SubAssetKind.License){ throw new ArgumentException("A subasset requires a name.",nameof(CanonicalName)); }
        if(CanonicalName.Any(char.IsControl)){ throw new ArgumentException("Subasset names cannot contain control characters.",nameof(CanonicalName)); }
        return SubAssetId.FromCanonicalBytes(Encoding.UTF8.GetBytes("cge.subasset/1\n"+Token+"\n"+CanonicalName.Normalize(NormalizationForm.FormC)));
    }
}
/// <summary>A source reference and an optional percent-escaped, context-typed subasset selector.</summary>
public sealed class AssetSelector {
    public AssetSelector(string Path,SubAssetKind Kind,string?Name=null){
        if(string.IsNullOrWhiteSpace(Path)){ throw new ArgumentException("A selector requires a source path.",nameof(Path)); }
        _=SubAssetIdentity.KindName(Kind);
        if(Name is not null){ _=SubAssetIdentity.Derive(Kind,Name); }
        this.Path=Path;
        this.Kind=Kind;
        this.Name=Name?.Normalize(NormalizationForm.FormC);
    }
    public string Path{get;}
    public SubAssetKind Kind{get;}
    public string?Name{get;}
    public override string ToString()=>this.Name is null?this.Path:this.Path+":"+Uri.EscapeDataString(this.Name);
    public static AssetSelector Parse(string Value,SubAssetKind Kind){
        if(Value is null){ throw new ArgumentNullException(nameof(Value)); }
        var Separator=Value.IndexOf(':',Value.Length>=2&&Value[1]==':'?2:0);
        if(Separator<0){ return new AssetSelector(Value,Kind); }
        var Encoded=Value.Substring(Separator+1);
        if(Encoded.Length==0||Encoded.IndexOfAny(new[]{':','/','\\'})>=0){ throw new FormatException("A selector name must be percent escaped."); }
        for(var Index=0;Index<Encoded.Length;Index++){
            if(Encoded[Index]!='%'){ continue; }
            if(Index+2>=Encoded.Length||!Uri.IsHexDigit(Encoded[Index+1])||!Uri.IsHexDigit(Encoded[Index+2])){ throw new FormatException("Invalid selector escape."); }
            Index+=2;
        }
        return new AssetSelector(Value.Substring(0,Separator),Kind,Uri.UnescapeDataString(Encoded));
    }
}
