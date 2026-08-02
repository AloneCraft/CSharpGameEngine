using System.Globalization;
namespace CSharpGameEngine.Core;
/// <summary>
/// シーンやアセットなどの永続的な Authoring ID を表す 128bit の安定識別子です。
/// </summary>
public readonly struct StableId:IEquatable<StableId>{
    private readonly Guid guid;
    private StableId(Guid guid){this.guid=guid;}
    public bool IsValid=>this.guid!=Guid.Empty;
    public static StableId New()=>new(Guid.NewGuid());
    public static StableId Parse(string value){ArgumentNullException.ThrowIfNull(value);return new StableId(Guid.ParseExact(value,"D"));}
    public static bool TryParse(string? value,out StableId stableId){if(value is not null&&Guid.TryParseExact(value,"D",out Guid parsed)){stableId=new StableId(parsed);return true;}stableId=default;return false;}
    public override string ToString()=>this.guid.ToString("D",CultureInfo.InvariantCulture);
    public bool Equals(StableId other)=>this.guid.Equals(other.guid);
    public override bool Equals(object? obj)=>obj is StableId other&&this.Equals(other);
    public override int GetHashCode()=>this.guid.GetHashCode();
    public static bool operator==(StableId left,StableId right)=>left.Equals(right);
    public static bool operator!=(StableId left,StableId right)=>!left.Equals(right);
}
