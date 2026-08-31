using System.Globalization;
using System.Security.Cryptography;
using System.Text;
namespace CSharpGameEngine.Core;
/// <summary>
/// シーンやアセットなどの永続的な Authoring ID を表す 128bit の安定識別子です。
/// </summary>
public readonly struct StableId:IEquatable<StableId>{
    private readonly Guid Guid;
    private StableId(Guid Guid){ this.Guid=Guid;}
    public bool IsValid=>this.Guid!=Guid.Empty;
    public static StableId New()=>new(Guid.NewGuid());
    public static StableId FromName(StableId NamespaceId,string Value){
        if(!NamespaceId.IsValid){ throw new ArgumentException("A valid namespace ID is required.",nameof(NamespaceId)); }
        ArgumentException.ThrowIfNullOrWhiteSpace(Value);
        var Bytes=SHA256.HashData(Encoding.UTF8.GetBytes(NamespaceId.ToString()+"\n"+Value));
        Bytes[6]=(byte)((Bytes[6]&0x0F)|0x50);
        Bytes[8]=(byte)((Bytes[8]&0x3F)|0x80);
        return new StableId(new Guid(Bytes.AsSpan(0,16)));
    }
    public static StableId Parse(string Value){ ArgumentNullException.ThrowIfNull(Value);return new StableId(Guid.ParseExact(Value,"D"));}
    public static bool TryParse(string?Value,out StableId StableId){ if(Value is not null&&Guid.TryParseExact(Value,"D",out var Parsed)){ StableId=new StableId(Parsed);return true;} StableId=default;return false;}
    public override string ToString()=>this.Guid.ToString("D",CultureInfo.InvariantCulture);
    public bool Equals(StableId Other)=>this.Guid.Equals(Other.Guid);
    public override bool Equals(object?Obj)=>Obj is StableId Other&&this.Equals(Other);
    public override int GetHashCode()=>this.Guid.GetHashCode();
    public static bool operator==(StableId Left,StableId Right)=>Left.Equals(Right);
    public static bool operator!=(StableId Left,StableId Right)=>!Left.Equals(Right);
}
