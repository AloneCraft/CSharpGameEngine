using System.Security.Cryptography;

namespace CSharpGameEngine.Contracts;

/// <summary>A canonical 256-bit content identity rendered as 64 lower-case hexadecimal digits.</summary>
public readonly struct Hash256:IEquatable<Hash256>,IComparable<Hash256> {
    private readonly ulong A;
    private readonly ulong B;
    private readonly ulong C;
    private readonly ulong D;

    private Hash256(ulong A,ulong B,ulong C,ulong D){
        this.A=A;
        this.B=B;
        this.C=C;
        this.D=D;
    }

    public bool IsValid=>(this.A|this.B|this.C|this.D)!=0;

    public static Hash256 Compute(byte[] Bytes){
        if(Bytes is null){ throw new ArgumentNullException(nameof(Bytes)); }
        using var Sha256=SHA256.Create();
        return FromBytes(Sha256.ComputeHash(Bytes));
    }

    public static Hash256 FromBytes(byte[] Bytes){
        if(Bytes is null){ throw new ArgumentNullException(nameof(Bytes)); }
        if(Bytes.Length!=32){ throw new ArgumentException("A Hash256 requires exactly 32 bytes.",nameof(Bytes)); }
        return new Hash256(
            ReadUInt64BigEndian(Bytes,0),
            ReadUInt64BigEndian(Bytes,8),
            ReadUInt64BigEndian(Bytes,16),
            ReadUInt64BigEndian(Bytes,24));
    }

    public byte[] ToByteArray(){
        var Result=new byte[32];
        WriteUInt64BigEndian(Result,0,this.A);
        WriteUInt64BigEndian(Result,8,this.B);
        WriteUInt64BigEndian(Result,16,this.C);
        WriteUInt64BigEndian(Result,24,this.D);
        return Result;
    }

    public static Hash256 Parse(string Value){
        if(!TryParse(Value,out var Result)){ throw new FormatException("A Hash256 must contain exactly 64 hexadecimal digits."); }
        return Result;
    }

    public static bool TryParse(string?Value,out Hash256 Result){
        Result=default;
        if(Value is null||Value.Length!=64){ return false; }
        var Bytes=new byte[32];
        for(var Index=0;Index<Bytes.Length;Index++){
            int High=ParseNibble(Value[Index*2]);
            int Low=ParseNibble(Value[Index*2+1]);
            if(High<0||Low<0){ return false; }
            Bytes[Index]=(byte)((High<<4)|Low);
        }
        Result=FromBytes(Bytes);
        return true;
    }

    public override string ToString(){
        const string Hex="0123456789abcdef";
        byte[]Bytes=this.ToByteArray();
        var Characters=new char[64];
        for(var Index=0;Index<Bytes.Length;Index++){
            Characters[Index*2]=Hex[Bytes[Index]>>4];
            Characters[Index*2+1]=Hex[Bytes[Index]&0xf];
        }
        return new string(Characters);
    }

    public bool Equals(Hash256 Other)=>
        this.A==Other.A&&this.B==Other.B&&this.C==Other.C&&this.D==Other.D;
    public override bool Equals(object?Object)=>Object is Hash256 Other&&this.Equals(Other);
    public override int GetHashCode(){
        unchecked {
            int Hash=17;
            Hash=Hash*31+this.A.GetHashCode();
            Hash=Hash*31+this.B.GetHashCode();
            Hash=Hash*31+this.C.GetHashCode();
            Hash=Hash*31+this.D.GetHashCode();
            return Hash;
        }
    }
    public int CompareTo(Hash256 Other){
        int Result=this.A.CompareTo(Other.A);
        if(Result!=0){ return Result; }
        Result=this.B.CompareTo(Other.B);
        if(Result!=0){ return Result; }
        Result=this.C.CompareTo(Other.C);
        return Result!=0?Result:this.D.CompareTo(Other.D);
    }
    public static bool operator ==(Hash256 Left,Hash256 Right)=>Left.Equals(Right);
    public static bool operator !=(Hash256 Left,Hash256 Right)=>!Left.Equals(Right);

    private static int ParseNibble(char Value){
        if(Value>='0'&&Value<='9'){ return Value-'0'; }
        if(Value>='a'&&Value<='f'){ return Value-'a'+10; }
        if(Value>='A'&&Value<='F'){ return Value-'A'+10; }
        return -1;
    }

    private static ulong ReadUInt64BigEndian(byte[] Bytes,int Offset){
        ulong Result=0;
        for(var Index=0;Index<8;Index++){ Result=(Result<<8)|Bytes[Offset+Index]; }
        return Result;
    }

    private static void WriteUInt64BigEndian(byte[] Bytes,int Offset,ulong Value){
        for(var Index=7;Index>=0;Index--){
            Bytes[Offset+Index]=(byte)Value;
            Value>>=8;
        }
    }
}
