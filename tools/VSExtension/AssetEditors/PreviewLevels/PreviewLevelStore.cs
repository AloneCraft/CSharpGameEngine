using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
namespace CSharpGameEngine.VSExtension;
/// <summary>カタログの 1 項目。読み込みに失敗したファイルは <see cref="Template"/> が null で、理由が <see cref="Error"/> に入る。</summary>
internal sealed class PreviewLevelEntry {
    internal string Id { get;init;}=string.Empty;
    internal string DisplayName { get;init;}=string.Empty;
    internal bool BuiltIn { get;init;}
    internal PreviewLevelTemplate?Template { get;init;}
    internal string?Error { get;init;}
    internal string?FilePath { get;init;}
    internal bool IsUsable=>this.Template is not null;
}
/// <summary>組み込みのテンプレートに対する削除・名前変更・上書きの拒否。</summary>
internal sealed class PreviewLevelProtectedException(string Id):InvalidOperationException("The built-in preview level '"+Id+"' is protected; duplicate it to customize.") {
    internal string TemplateId { get;}=Id;
}
/// <summary>
/// プレビューレベルのカタログ（組み込み + プロジェクトのファイル）。プロジェクトのテンプレートは <c>Assets\PreviewLevels\*.cgepreview</c>（JSON）に保存し、
/// ファイル名（拡張子なし）が名前変更で変わらない識別子になる。Assets ウィンドウの一覧には出さない編集用データで、ゲームのカタログ生成の対象でもない。
/// 書き込みは一時ファイルから置き換える（途中で壊れたファイルを残さない）。組み込みは書き込み対象外。
/// </summary>
internal sealed class PreviewLevelStore(string ProjectDirectory) {
    internal const string Extension=".cgepreview";
    internal const string FolderName="PreviewLevels";
    /// <summary>テンプレートファイルの上限。これを超えるファイルは読まない。</summary>
    internal const int MaximumFileBytes=256*1024;
    internal string Directory=>Path.Combine(ProjectDirectory,"Assets",FolderName);
    internal string PathFor(string Id)=>Path.Combine(this.Directory,Id+Extension);
    internal IReadOnlyList<PreviewLevelEntry>Load(){
        var Entries=new List<PreviewLevelEntry>();
        foreach(var Template in PreviewLevelBuiltIns.All()){
            Entries.Add(new PreviewLevelEntry { Id=Template.Id,DisplayName=Template.DisplayName,BuiltIn=true,Template=Template });
        }
        if(!System.IO.Directory.Exists(this.Directory)){ return Entries; }
        string[]Files;
        try {
            Files=System.IO.Directory.GetFiles(this.Directory,"*"+Extension);
        } catch(Exception Error) when(Error is IOException or UnauthorizedAccessException){
            return Entries;
        }
        Array.Sort(Files,StringComparer.OrdinalIgnoreCase);
        foreach(var File in Files){ Entries.Add(ReadFile(File)); }
        return Entries;
    }
    private static PreviewLevelEntry ReadFile(string FilePath){
        var Id=Path.GetFileNameWithoutExtension(FilePath);
        if(PreviewLevelBuiltIns.IsReserved(Id)){
            return new PreviewLevelEntry { Id=Id,DisplayName=Id,FilePath=FilePath,Error="'"+Id+"' is a reserved built-in preview level name; rename the file." };
        }
        try {
            var Info=new FileInfo(FilePath);
            if(Info.Length>MaximumFileBytes){ return new PreviewLevelEntry { Id=Id,DisplayName=Id,FilePath=FilePath,Error="The preview level file is larger than "+MaximumFileBytes+" bytes." }; }
            string Json;
            using(var Stream=new FileStream(FilePath,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
            using(var Reader=new StreamReader(Stream,Encoding.UTF8,true)){ Json=Reader.ReadToEnd(); }
            var Template=PreviewLevelTemplate.TryParse(Json,out var Error);
            if(Template is null){ return new PreviewLevelEntry { Id=Id,DisplayName=Id,FilePath=FilePath,Error=Error }; }
            Template.Id=Id;
            return new PreviewLevelEntry { Id=Id,DisplayName=Template.DisplayName,Template=Template,FilePath=FilePath };
        } catch(Exception Failure) when(Failure is IOException or UnauthorizedAccessException){
            return new PreviewLevelEntry { Id=Id,DisplayName=Id,FilePath=FilePath,Error=Failure.Message };
        }
    }
    /// <summary>Default の初期値を持つ新しいユーザーテンプレートを保存する。</summary>
    internal PreviewLevelEntry Create(string DisplayName)=>this.Duplicate(PreviewLevelBuiltIns.Default(),DisplayName);
    /// <summary>テンプレートを複製して新しいファイルとして保存する（組み込みを複製してカスタマイズする入口）。</summary>
    internal PreviewLevelEntry Duplicate(PreviewLevelTemplate Source,string DisplayName){
        if(string.IsNullOrWhiteSpace(DisplayName)){ throw new ArgumentException("A display name is required.",nameof(DisplayName)); }
        var Copy=Source.Clone();
        Copy.BuiltIn=false;
        Copy.DisplayName=DisplayName.Trim();
        Copy.Id=MakeId(Copy.DisplayName,this.ExistingIds());
        Write(this.PathFor(Copy.Id),Copy);
        return new PreviewLevelEntry { Id=Copy.Id,DisplayName=Copy.DisplayName,Template=Copy,FilePath=this.PathFor(Copy.Id) };
    }
    /// <summary>表示名を変える。識別子（ファイル名）は変えないので、選択状態や既定の指定は壊れない。</summary>
    internal void Rename(string Id,string DisplayName){
        if(PreviewLevelBuiltIns.IsReserved(Id)){ throw new PreviewLevelProtectedException(Id); }
        if(string.IsNullOrWhiteSpace(DisplayName)){ throw new ArgumentException("A display name is required.",nameof(DisplayName)); }
        var Entry=ReadFile(this.PathFor(Id));
        var Template=Entry.Template??throw new InvalidOperationException(Entry.Error??"The preview level cannot be read.");
        Template.DisplayName=DisplayName.Trim();
        Write(this.PathFor(Id),Template);
    }
    /// <summary>テンプレートの内容を保存する。組み込みは上書きできない。</summary>
    internal void Save(PreviewLevelTemplate Template){
        if(Template.BuiltIn||PreviewLevelBuiltIns.IsReserved(Template.Id)){ throw new PreviewLevelProtectedException(Template.Id); }
        var Error=Template.Validate();
        if(Error is not null){ throw new ArgumentException(Error,nameof(Template)); }
        Write(this.PathFor(Template.Id),Template);
    }
    internal void Delete(string Id){
        if(PreviewLevelBuiltIns.IsReserved(Id)){ throw new PreviewLevelProtectedException(Id); }
        var Path=this.PathFor(Id);
        if(System.IO.File.Exists(Path)){ System.IO.File.Delete(Path); }
    }
    private HashSet<string>ExistingIds(){
        var Ids=new HashSet<string>(PreviewLevelBuiltIns.ReservedIds,StringComparer.OrdinalIgnoreCase);
        if(System.IO.Directory.Exists(this.Directory)){
            foreach(var File in System.IO.Directory.GetFiles(this.Directory,"*"+Extension)){ Ids.Add(Path.GetFileNameWithoutExtension(File)); }
        }
        return Ids;
    }
    /// <summary>表示名からファイル名に使える一意の識別子を作る。予約済み・既存の名前とは重ならない。</summary>
    internal static string MakeId(string DisplayName,ISet<string>Taken){
        var Invalid=Path.GetInvalidFileNameChars();
        var Builder=new StringBuilder();
        foreach(var Character in DisplayName.Trim()){
            if(Character=='.'||char.IsWhiteSpace(Character)||Array.IndexOf(Invalid,Character)>=0){
                if(Builder.Length>0&&Builder[Builder.Length-1]!='-'){ Builder.Append('-'); }
            } else { Builder.Append(Character); }
        }
        var Stem=Builder.ToString().Trim('-');
        if(Stem.Length==0){ Stem="preview-level"; }
        if(Stem.Length>64){ Stem=Stem.Substring(0,64).TrimEnd('-'); }
        var Candidate=Stem;
        for(var Index=2;Taken.Contains(Candidate);Index++){ Candidate=Stem+"-"+Index; }
        return Candidate;
    }
    private static void Write(string Path,PreviewLevelTemplate Template){
        var Folder=System.IO.Path.GetDirectoryName(Path)!;
        System.IO.Directory.CreateDirectory(Folder);
        var Temporary=System.IO.Path.Combine(Folder,"."+System.IO.Path.GetFileName(Path)+"."+Guid.NewGuid().ToString("N")+".tmp");
        try {
            System.IO.File.WriteAllText(Temporary,Template.ToJson(),new UTF8Encoding(false));
            if(System.IO.File.Exists(Path)){ System.IO.File.Replace(Temporary,Path,null); } else { System.IO.File.Move(Temporary,Path); }
        } finally {
            if(System.IO.File.Exists(Temporary)){ System.IO.File.Delete(Temporary); }
        }
    }
}
/// <summary>前回選んだテンプレートと既定のテンプレートの永続化の入口（VS の設定ストア、テストでは差し替え）。</summary>
internal interface IPreviewLevelSettings {
    string?Read(string Name);
    void Write(string Name,string?Value);
}
/// <summary>
/// プロジェクトごとの選択状態。前回使ったテンプレート、プロジェクトの既定、アセット種類ごとの既定（将来のため。今回は静的メッシュだけが使う）を
/// プロジェクトフォルダーのハッシュを含む名前で保存する。
/// </summary>
internal sealed class PreviewLevelPreferences(IPreviewLevelSettings Settings,string ProjectDirectory) {
    private string Key(string Slot)=>"PreviewLevel."+Slot+"."+ProjectHash(ProjectDirectory);
    internal string?LastUsedId {
        get=>Normalize(Settings.Read(this.Key("LastUsed")));
        set=>Settings.Write(this.Key("LastUsed"),value);
    }
    internal string?DefaultId {
        get=>Normalize(Settings.Read(this.Key("Default")));
        set=>Settings.Write(this.Key("Default"),value);
    }
    internal string?KindDefaultId(string Kind)=>Normalize(Settings.Read(this.Key("KindDefault."+Kind)));
    internal void SetKindDefaultId(string Kind,string?Id)=>Settings.Write(this.Key("KindDefault."+Kind),Id);
    private static string?Normalize(string?Value)=>string.IsNullOrWhiteSpace(Value)?null:Value;
    internal static string ProjectHash(string ProjectDirectory){
        var Normalized=ProjectDirectory.Trim().TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar).ToLowerInvariant();
        using var Sha=SHA256.Create();
        return BitConverter.ToString(Sha.ComputeHash(Encoding.UTF8.GetBytes(Normalized)),0,8).Replace("-",string.Empty).ToLowerInvariant();
    }
}
/// <summary>選択の解決結果。<see cref="Error"/> があるときは、指定されたテンプレートが使えず標準の既定環境へ戻ったことを表す。</summary>
internal sealed class PreviewLevelResolution(PreviewLevelTemplate Template,string?Error) {
    internal PreviewLevelTemplate Template { get;}=Template;
    internal string?Error { get;}=Error;
    internal bool FellBack=>this.Error is not null;
}
internal static class PreviewLevelResolver {
    /// <summary>
    /// 優先順（前回使った → アセット種類の既定 → プロジェクトの既定）に最初に使えるテンプレートを返す。指定があって存在しない・壊れている場合は、
    /// 次の候補を試し、最終的に標準の Default へ戻して理由を返す。指定がなければエラーなしで Default。
    /// </summary>
    internal static PreviewLevelResolution Resolve(IReadOnlyList<PreviewLevelEntry>Entries,params string?[]PreferredIds){
        string?FirstProblem=null;
        foreach(var Id in PreferredIds){
            if(string.IsNullOrEmpty(Id)){ continue; }
            var Entry=Entries.FirstOrDefault(Candidate=>string.Equals(Candidate.Id,Id,StringComparison.OrdinalIgnoreCase));
            if(Entry?.Template is{ }Template){ return new PreviewLevelResolution(Template,FirstProblem); }
            FirstProblem??=VsixText.Format("PreviewLevel_Unavailable",Id,Entry?.Error??VsixText.Get("PreviewLevel_Missing"));
        }
        var Fallback=Entries.First(static Candidate=>string.Equals(Candidate.Id,PreviewLevelBuiltIns.DefaultId,StringComparison.Ordinal)).Template!;
        return new PreviewLevelResolution(Fallback,FirstProblem);
    }
}
