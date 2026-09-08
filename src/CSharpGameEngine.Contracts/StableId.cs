using System.Globalization;
using System.Security.Cryptography;
using System.Text;
namespace CSharpGameEngine.Core;
public readonly struct StableId:IEquatable<StableId>,IComparable<StableId> {
    private readonly Guid Guid;
    private StableId(Guid Guid){ this.Guid=Guid; }
    public bool IsValid=>this.Guid!=Guid.Empty;
    public static StableId New()=>new(Guid.NewGuid());
    public static StableId FromName(StableId NamespaceId,string Value){
        if(!NamespaceId.IsValid){ throw new ArgumentException("A valid namespace ID is required.",nameof(NamespaceId)); }
        if(string.IsNullOrWhiteSpace(Value)){ throw new ArgumentException("A non-empty name is required.",nameof(Value)); }
        byte[]Hash;
        using(var Sha256=SHA256.Create()){ Hash=Sha256.ComputeHash(Encoding.UTF8.GetBytes(NamespaceId.ToString()+"\n"+Value)); }
        Hash[6]=(byte)((Hash[6]&0x0f)|0x50);
        Hash[8]=(byte)((Hash[8]&0x3f)|0x80);
        var GuidBytes=new byte[16];
        Array.Copy(Hash,GuidBytes,GuidBytes.Length);
        return new StableId(new Guid(GuidBytes));
    }
    public static StableId Parse(string Value){
        if(Value is null){ throw new ArgumentNullException(nameof(Value)); }
        return new StableId(Guid.ParseExact(Value,"D"));
    }
    public static bool TryParse(string?Value,out StableId StableId){
        if(Value is not null&&Guid.TryParseExact(Value,"D",out var Parsed)){ StableId=new StableId(Parsed);return true; }
        StableId=default;
        return false;
    }
    public override string ToString()=>this.Guid.ToString("D",CultureInfo.InvariantCulture);
    public bool Equals(StableId Other)=>this.Guid.Equals(Other.Guid);
    public override bool Equals(object?Object)=>Object is StableId Other&&this.Equals(Other);
    public override int GetHashCode()=>this.Guid.GetHashCode();
    public int CompareTo(StableId Other)=>this.Guid.CompareTo(Other.Guid);
    public static bool operator ==(StableId Left,StableId Right)=>Left.Equals(Right);
    public static bool operator !=(StableId Left,StableId Right)=>!Left.Equals(Right);
}
