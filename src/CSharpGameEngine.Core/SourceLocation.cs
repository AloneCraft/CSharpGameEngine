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

    public bool IsUnknown=>FilePath is null&&Line==0&&Column==0;

    public override string ToString(){
        if(IsUnknown){
            return "<unknown>";
        }

        if(FilePath is null){
            return $"(Line: {Line}, Column: {Column})";
        }

        return $"{FilePath}({Line},{Column})";
    }

    public bool Equals(SourceLocation Other)=>
        StringComparer.Ordinal.Equals(FilePath,Other.FilePath)&&
        Line==Other.Line&&
        Column==Other.Column;

    public override bool Equals(object?Obj)=>Obj is SourceLocation Other&&Equals(Other);

    public override int GetHashCode()=>HashCode.Combine(StringComparer.Ordinal.GetHashCode(FilePath??string.Empty),Line,Column);

    public static bool operator==(SourceLocation Left,SourceLocation Right)=>Left.Equals(Right);

    public static bool operator!=(SourceLocation Left,SourceLocation Right)=>!Left.Equals(Right);
}
