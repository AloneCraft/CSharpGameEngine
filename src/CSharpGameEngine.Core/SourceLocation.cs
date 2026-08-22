namespace CSharpGameEngine.Core;

public readonly struct SourceLocation(string?FilePath,int Line,int Column):IEquatable<SourceLocation> {
    public static SourceLocation Unknown { get;}=new(null,0,0);

    public string?FilePath { get;}=NormalizeFilePath(FilePath);

    private static string?NormalizeFilePath(string?FilePath)=>
        string.IsNullOrWhiteSpace(FilePath)?null:FilePath;

    /// <summary>
    /// 1始まりの行番号。Unknown のときは 0。
    /// </summary>
    public int Line { get;}=ValidateCoordinate(Line,nameof(Line));

    /// <summary>
    /// 1始まりの列番号。Unknown のときは 0。
    /// </summary>
    public int Column { get;}=ValidateCoordinate(Column,nameof(Column));

    private static int ValidateCoordinate(int Value,string ParameterName){
        if(Value<0){
            throw new ArgumentOutOfRangeException(ParameterName);
        }

        return Value;
    }

    public bool IsUnknown=> this.FilePath is null&& this.Line==0&& this.Column==0;

    public override string ToString(){
        if(this.IsUnknown){
            return "<unknown>";
        }

        if(this.FilePath is null){
            return $"(Line: {this.Line}, Column: {this.Column})";
        }

        return $"{this.FilePath}({this.Line},{this.Column})";
    }

    public bool Equals(SourceLocation Other)=>
        StringComparer.Ordinal.Equals(this.FilePath,Other.FilePath)&& this.Line==Other.Line&& this.Column==Other.Column;

    public override bool Equals(object?Obj)=>Obj is SourceLocation Other&& this.Equals(Other);

    public override int GetHashCode()=>HashCode.Combine(StringComparer.Ordinal.GetHashCode(this.FilePath??string.Empty), this.Line, this.Column);

    public static bool operator==(SourceLocation Left,SourceLocation Right)=>Left.Equals(Right);

    public static bool operator!=(SourceLocation Left,SourceLocation Right)=>!Left.Equals(Right);
}
