using System.Text;
namespace CSharpGameEngine.Unreal.SceneImporter;
/// <summary>
/// Unrealのテキスト書き出し(T3D)を読む(ADR-0078/M0102)。T3Dは、まず「Begin Object Class=… Name=…」でオブジェクトの木を宣言し、
/// 続けて「Begin Object Name=…」の段で属性(Key=Value)とピン(CustomProperties Pin (...))を書く。両方の段のExportPathから、
/// オブジェクトのクラスと完全なパスを取り、1つのオブジェクトにまとめる。
/// </summary>
public sealed class T3dDocument {
    private readonly Dictionary<string,T3dObject>ByPath=new(StringComparer.Ordinal);
    private readonly List<T3dObject>RootList=[];
    public IReadOnlyList<T3dObject>Roots=>this.RootList;
    public IEnumerable<T3dObject>All=>this.ByPath.Values;
    public static T3dDocument Parse(string Text){
        var Document=new T3dDocument();
        var Stack=new Stack<T3dObject>();
        foreach(var Raw in Text.Split('\n')){
            var Line=Raw.Trim().TrimEnd('\r');
            if(Line.StartsWith("Begin Object ",StringComparison.Ordinal)){
                var Header=ParseHeader(Line);
                if(!Document.ByPath.TryGetValue(Header.Path,out var Object)){
                    Object=new T3dObject(Header.Name,Header.Path,Stack.Count>0?Stack.Peek():null);
                    Document.ByPath.Add(Header.Path,Object);
                    if(Object.Parent is null){
                        Document.RootList.Add(Object);
                    }
                    else {
                        Object.Parent.ChildList.Add(Object);
                    }
                }
                if(Header.Class.Length>0){
                    Object.Class=Header.Class;
                }
                Stack.Push(Object);
                continue;
            }
            if(Line=="End Object"){
                if(Stack.Count>0){
                    Stack.Pop();
                }
                continue;
            }
            if(Stack.Count==0||Line.Length==0){
                continue;
            }
            var Current=Stack.Peek();
            if(Line.StartsWith("CustomProperties Pin (",StringComparison.Ordinal)){
                Current.PinList.Add(T3dPin.Parse(Current,Line["CustomProperties Pin ".Length..]));
                continue;
            }
            var Equals=Line.IndexOf('=');
            if(Equals>0){
                Current.PropertyMap[Line[..Equals]]=Line[(Equals+1)..];
            }
        }
        return Document;
    }
    public T3dObject?Find(string Path)=>this.ByPath.TryGetValue(Path,out var Object)?Object:null;
    private readonly record struct Header(string Class,string Name,string Path);
    /// <summary>「Begin Object [Class=C] Name="N" ExportPath="/Script/M.C'/Game/…:Outer.N'"」の、クラスの短い名前・名前・パス。</summary>
    private static Header ParseHeader(string Line){
        var Name=QuotedAfter(Line,"Name=\"");
        var Export=QuotedAfter(Line,"ExportPath=\"");
        var Class=string.Empty;
        var Path=Name;
        var Quote=Export.IndexOf('\'');
        if(Quote>0){
            Class=ShortName(Export[..Quote]);
            Path=Export[(Quote+1)..].TrimEnd('\'');
        }
        var Declared=Line.IndexOf("Class=",StringComparison.Ordinal);
        if(Declared>=0&&Line.IndexOf("Begin Object Class=",StringComparison.Ordinal)==0){
            var End=Line.IndexOf(' ',Declared);
            Class=ShortName(Line[(Declared+"Class=".Length)..(End<0?Line.Length:End)]);
        }
        return new Header(Class,Name,Path);
    }
    private static string QuotedAfter(string Line,string Key){
        var Start=Line.IndexOf(Key,StringComparison.Ordinal);
        if(Start<0){
            return string.Empty;
        }
        Start+=Key.Length;
        var End=Line.IndexOf('"',Start);
        return End<0?Line[Start..]:Line[Start..End];
    }
    /// <summary>「/Script/Engine.Blueprint」のような完全なクラス名から「Blueprint」を取る。</summary>
    public static string ShortName(string Value){
        var Text=Value.Trim().Trim('"');
        var Quote=Text.IndexOf('\'');
        if(Quote>=0){
            // "/Script/CoreUObject.Class'/Script/Engine.Character'" は、引用符の中の方が本体。
            Text=Text[(Quote+1)..].TrimEnd('\'');
        }
        // オブジェクトの名前には「Walk / Run」のように「/」が入りうるので、まず「.」と「:」で切る。
        var Cut=Math.Max(Text.LastIndexOf('.'),Text.LastIndexOf(':'));
        if(Cut<0){
            Cut=Text.LastIndexOf('/');
        }
        return Cut>=0?Text[(Cut+1)..]:Text;
    }
    /// <summary>
    /// 「(A=1,B=(C="x,y"),D=2)」の最上位のKeyとValueを読む。括弧と引用符の中のコンマでは区切らない。
    /// 括弧で囲まれていない値は、そのまま1つの要素として返さない(空の辞書)。
    /// </summary>
    public static Dictionary<string,string>ParseStruct(string Value){
        var Result=new Dictionary<string,string>(StringComparer.Ordinal);
        var Text=Value.Trim();
        if(Text.Length<2||Text[0]!='('||Text[^1]!=')'){
            return Result;
        }
        foreach(var Part in SplitTopLevel(Text[1..^1])){
            var Equals=Part.IndexOf('=');
            if(Equals>0){
                Result[Part[..Equals].Trim()]=Part[(Equals+1)..].Trim();
            }
        }
        return Result;
    }
    /// <summary>括弧と引用符の外のコンマで区切る。末尾の空の要素は落とす。</summary>
    public static List<string>SplitTopLevel(string Text){
        var Parts=new List<string>();
        var Depth=0;
        var Quoted=false;
        var Builder=new StringBuilder();
        for(var Index=0;Index<Text.Length;Index++){
            var Character=Text[Index];
            if(Character=='"'&&(Index==0||Text[Index-1]!='\\')){
                Quoted=!Quoted;
            }
            else if(!Quoted&&Character=='('){
                Depth++;
            }
            else if(!Quoted&&Character==')'){
                Depth--;
            }
            else if(!Quoted&&Depth==0&&Character==','){
                Parts.Add(Builder.ToString());
                Builder.Clear();
                continue;
            }
            Builder.Append(Character);
        }
        if(Builder.Length>0){
            Parts.Add(Builder.ToString());
        }
        return Parts.Where(static Part=>Part.Trim().Length>0).ToList();
    }
}
/// <summary>T3Dの1つのオブジェクト。属性は書かれた文字列のまま持つ。</summary>
public sealed class T3dObject(string Name,string Path,T3dObject?Parent) {
    internal readonly List<T3dObject>ChildList=[];
    internal readonly List<T3dPin>PinList=[];
    internal readonly Dictionary<string,string>PropertyMap=new(StringComparer.Ordinal);
    public string Name { get; }=Name;
    public string Path { get; }=Path;
    public T3dObject?Parent { get; }=Parent;
    public string Class { get; internal set;}=string.Empty;
    public IReadOnlyList<T3dObject>Children=>this.ChildList;
    public IReadOnlyList<T3dPin>Pins=>this.PinList;
    public IReadOnlyDictionary<string,string>Properties=>this.PropertyMap;
    public string Property(string Key)=>this.PropertyMap.TryGetValue(Key,out var Value)?Value:string.Empty;
    public T3dPin?FindPin(string Id)=>this.PinList.FirstOrDefault(Pin=>Pin.Id==Id);
    public override string ToString()=>$"{this.Class} {this.Name}";
}
/// <summary>グラフのノードのピン。<see cref="Links"/>は(ノード名, ピンID)の組。</summary>
public sealed class T3dPin {
    public required T3dObject Owner { get; init;}
    public required string Id { get; init;}
    public required string Name { get; init;}
    public required bool IsOutput { get; init;}
    public required string Category { get; init;}
    public required string SubCategoryObject { get; init;}
    public required string DefaultValue { get; init;}
    public required bool Hidden { get; init;}
    public required IReadOnlyList<(string Node,string Pin)>Links { get; init;}
    public bool IsExec=>this.Category=="exec";
    internal static T3dPin Parse(T3dObject Owner,string Text){
        var Fields=T3dDocument.ParseStruct(Text);
        string Get(string Key)=>Fields.TryGetValue(Key,out var Value)?Value.Trim('"'):string.Empty;
        var Links=new List<(string,string)>();
        if(Fields.TryGetValue("LinkedTo",out var Linked)&&Linked.Length>=2){
            foreach(var Part in T3dDocument.SplitTopLevel(Linked.Trim()[1..^1])){
                var Pieces=Part.Trim().Split(' ',StringSplitOptions.RemoveEmptyEntries);
                if(Pieces.Length==2){
                    Links.Add((Pieces[0],Pieces[1]));
                }
            }
        }
        var Default=Get("DefaultValue");
        if(Default.Length==0){
            Default=Get("DefaultObject");
        }
        if(Default.Length==0){
            Default=Get("DefaultTextValue");
        }
        return new T3dPin {
            Owner=Owner,
            Id=Get("PinId"),
            Name=Get("PinName"),
            IsOutput=Get("Direction")=="EGPD_Output",
            Category=Get("PinType.PinCategory"),
            SubCategoryObject=Get("PinType.PinSubCategoryObject"),
            DefaultValue=Default,
            Hidden=Get("bHidden")=="True",
            Links=Links,
        };
    }
}
