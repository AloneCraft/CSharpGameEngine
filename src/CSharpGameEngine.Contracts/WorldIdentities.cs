using CSharpGameEngine.Core;
namespace CSharpGameEngine.Contracts;
public readonly struct ReferenceFrameId(StableId Value):IEquatable<ReferenceFrameId> {
    public static ReferenceFrameId Root{get;}=Parse("00000000-0000-5000-8000-000000000001");
    public StableId Value{get;}=Value;
    public bool IsValid=>this.Value.IsValid;
    public static ReferenceFrameId Parse(string Value)=>new(StableId.Parse(Value));
    public static bool TryParse(string?Value,out ReferenceFrameId Frame){
        if(StableId.TryParse(Value,out var Stable)){ Frame=new ReferenceFrameId(Stable);return true; }
        Frame=default;
        return false;
    }
    public override string ToString()=>this.Value.ToString();
    public bool Equals(ReferenceFrameId Other)=>this.Value==Other.Value;
    public override bool Equals(object?Object)=>Object is ReferenceFrameId Other&&this.Equals(Other);
    public override int GetHashCode()=>this.Value.GetHashCode();
    public static bool operator ==(ReferenceFrameId Left,ReferenceFrameId Right)=>Left.Equals(Right);
    public static bool operator !=(ReferenceFrameId Left,ReferenceFrameId Right)=>!Left.Equals(Right);
}
public readonly struct WorldCellId(StableId Value):IEquatable<WorldCellId>,IComparable<WorldCellId> {
    public StableId Value{get;}=Value;
    public bool IsValid=>this.Value.IsValid;
    public static WorldCellId Parse(string Value)=>new(StableId.Parse(Value));
    public static bool TryParse(string?Value,out WorldCellId Cell){
        if(StableId.TryParse(Value,out var Stable)){ Cell=new WorldCellId(Stable);return true; }
        Cell=default;
        return false;
    }
    public override string ToString()=>this.Value.ToString();
    public bool Equals(WorldCellId Other)=>this.Value==Other.Value;
    public override bool Equals(object?Object)=>Object is WorldCellId Other&&this.Equals(Other);
    public override int GetHashCode()=>this.Value.GetHashCode();
    public int CompareTo(WorldCellId Other)=>this.Value.CompareTo(Other.Value);
    public static bool operator ==(WorldCellId Left,WorldCellId Right)=>Left.Equals(Right);
    public static bool operator !=(WorldCellId Left,WorldCellId Right)=>!Left.Equals(Right);
}
public readonly struct PersistentEntityId(StableId Value):IEquatable<PersistentEntityId> {
    public StableId Value{get;}=Value;
    public bool IsValid=>this.Value.IsValid;
    public static PersistentEntityId Parse(string Value)=>new(StableId.Parse(Value));
    public override string ToString()=>this.Value.ToString();
    public bool Equals(PersistentEntityId Other)=>this.Value==Other.Value;
    public override bool Equals(object?Object)=>Object is PersistentEntityId Other&&this.Equals(Other);
    public override int GetHashCode()=>this.Value.GetHashCode();
    public static bool operator ==(PersistentEntityId Left,PersistentEntityId Right)=>Left.Equals(Right);
    public static bool operator !=(PersistentEntityId Left,PersistentEntityId Right)=>!Left.Equals(Right);
}
public readonly struct AssetId(StableId Value):IEquatable<AssetId> {
    public StableId Value{get;}=Value;
    public bool IsValid=>this.Value.IsValid;
    public static AssetId Parse(string Value)=>new(StableId.Parse(Value));
    public static bool TryParse(string?Value,out AssetId AssetId){
        if(StableId.TryParse(Value,out var Stable)){ AssetId=new AssetId(Stable);return true; }
        AssetId=default;
        return false;
    }
    public override string ToString()=>this.Value.ToString();
    public bool Equals(AssetId Other)=>this.Value==Other.Value;
    public override bool Equals(object?Object)=>Object is AssetId Other&&this.Equals(Other);
    public override int GetHashCode()=>this.Value.GetHashCode();
    public static bool operator ==(AssetId Left,AssetId Right)=>Left.Equals(Right);
    public static bool operator !=(AssetId Left,AssetId Right)=>!Left.Equals(Right);
}
public readonly struct SubAssetId(Hash256 Value):IEquatable<SubAssetId>,IComparable<SubAssetId> {
    public Hash256 Value{get;}=Value;
    public bool IsValid=>this.Value.IsValid;
    public static SubAssetId FromCanonicalBytes(byte[] Bytes)=>new(Hash256.Compute(Bytes));
    public static SubAssetId Parse(string Value)=>new(Hash256.Parse(Value));
    public override string ToString()=>this.Value.ToString();
    public bool Equals(SubAssetId Other)=>this.Value==Other.Value;
    public override bool Equals(object?Object)=>Object is SubAssetId Other&&this.Equals(Other);
    public override int GetHashCode()=>this.Value.GetHashCode();
    public int CompareTo(SubAssetId Other)=>this.Value.CompareTo(Other.Value);
    public static bool operator ==(SubAssetId Left,SubAssetId Right)=>Left.Equals(Right);
    public static bool operator !=(SubAssetId Left,SubAssetId Right)=>!Left.Equals(Right);
}
public readonly struct AssetKey(AssetId AssetId,SubAssetId?SubAssetId):IEquatable<AssetKey>,IComparable<AssetKey> {
    public AssetId AssetId{get;}=AssetId;
    public SubAssetId?SubAssetId{get;}=SubAssetId;
    public bool IsValid=>this.AssetId.IsValid&&(!this.SubAssetId.HasValue||this.SubAssetId.Value.IsValid);
    public override string ToString()=>this.SubAssetId is { }Sub
        ?this.AssetId+"/"+Sub
        :this.AssetId.ToString();
    public bool Equals(AssetKey Other)=>this.AssetId==Other.AssetId&&this.SubAssetId==Other.SubAssetId;
    public override bool Equals(object?Object)=>Object is AssetKey Other&&this.Equals(Other);
    public override int GetHashCode(){
        unchecked { return this.AssetId.GetHashCode()*397+(this.SubAssetId?.GetHashCode()??0); }
    }
    public int CompareTo(AssetKey Other){
        int Asset=string.CompareOrdinal(this.AssetId.ToString(),Other.AssetId.ToString());
        if(Asset!=0){ return Asset; }
        if(!this.SubAssetId.HasValue){ return Other.SubAssetId.HasValue?-1:0; }
        return Other.SubAssetId.HasValue?this.SubAssetId.Value.CompareTo(Other.SubAssetId.Value):1;
    }
    public static bool operator ==(AssetKey Left,AssetKey Right)=>Left.Equals(Right);
    public static bool operator !=(AssetKey Left,AssetKey Right)=>!Left.Equals(Right);
}
public readonly struct AssetArtifactReference(AssetKey Key,Hash256 ContentHash):IEquatable<AssetArtifactReference> {
    public AssetKey Key{get;}=Key;
    public Hash256 ContentHash{get;}=ContentHash;
    public bool IsValid=>this.Key.IsValid&&this.ContentHash.IsValid;
    public bool Equals(AssetArtifactReference Other)=>this.Key==Other.Key&&this.ContentHash==Other.ContentHash;
    public override bool Equals(object?Object)=>Object is AssetArtifactReference Other&&this.Equals(Other);
    public override int GetHashCode(){
        unchecked { return this.Key.GetHashCode()*397+this.ContentHash.GetHashCode(); }
    }
    public static bool operator ==(AssetArtifactReference Left,AssetArtifactReference Right)=>Left.Equals(Right);
    public static bool operator !=(AssetArtifactReference Left,AssetArtifactReference Right)=>!Left.Equals(Right);
}
