namespace CSharpGameEngine.Core;

public readonly struct SourceLocation:IEquatable<SourceLocation>{
    public static SourceLocation Unknown { get;}=new(null,0,0);

    public SourceLocation(string?FilePath,int Line,int Column){
        if(Line<0){
            throw new ArgumentOutOfRangeException(nameof(Line));
        }

        if(Column<0){
            throw new ArgumentOutOfRangeException(nameof(Column));
        }

        this.FilePath=string.IsNullOrWhiteSpace(FilePath) ?null: FilePath;
        this.Line=Line;
        this.Column=Column;
    }

    public string?FilePath { get;}

    /// <summary>
    /// 1始まりの行番号。Unknown のときは 0。
    /// </summary>
    public int Line { get;}

    /// <summary>
    /// 1始まりの列番号。Unknown のときは 0。
    /// </summary>
    public int Column { get;}

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
