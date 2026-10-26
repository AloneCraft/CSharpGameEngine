using System.Text;
namespace CSharpGameEngine.VSExtension.Tests;
/// <summary>プレビューレベルの保存（Assets\PreviewLevels\*.cgepreview）、保護、選択状態、フォールバック（M0127）。</summary>
public sealed class PreviewLevelStoreTests:IDisposable {
    private readonly string Project=Path.Combine(Path.GetTempPath(),"CgePreviewLevels"+Guid.NewGuid().ToString("N"));
    private PreviewLevelStore Store=>new(this.Project);
    public PreviewLevelStoreTests()=>Directory.CreateDirectory(this.Project);
    public void Dispose(){
        try { Directory.Delete(this.Project,true); } catch(IOException){ } catch(UnauthorizedAccessException){ }
    }
    private sealed class MemorySettings:IPreviewLevelSettings {
        internal readonly Dictionary<string,string>Values=[];
        public string?Read(string Name)=>this.Values.TryGetValue(Name,out var Value)?Value:null;
        public void Write(string Name,string?Value){
            if(Value is null){ this.Values.Remove(Name); } else { this.Values[Name]=Value; }
        }
    }
    [Fact]
    public void WithoutAFolder_OnlyTheFourBuiltInsExistAndNothingIsCreated(){
        var Entries=this.Store.Load();
        Assert.Equal(new[]{ "default","studio","outdoor","neutral" },Entries.Select(static Entry=>Entry.Id));
        Assert.All(Entries,static Entry=>{
            Assert.True(Entry.BuiltIn);
            Assert.True(Entry.IsUsable);
            Assert.Null(Entry.FilePath);
        });
        Assert.False(Directory.Exists(this.Store.Directory));
    }
    [Fact]
    public void Duplicate_WritesAProjectFileWithAStableUniqueId(){
        var Studio=PreviewLevelBuiltIns.Studio();
        var First=this.Store.Duplicate(Studio,"My Studio");
        var Second=this.Store.Duplicate(Studio,"My Studio");
        Assert.Equal("My-Studio",First.Id);
        Assert.Equal("My-Studio-2",Second.Id);
        Assert.False(First.BuiltIn);
        Assert.True(File.Exists(Path.Combine(this.Project,"Assets","PreviewLevels","My-Studio.cgepreview")));
        var Loaded=this.Store.Load();
        Assert.Equal(6,Loaded.Count);
        var Entry=Loaded.Single(static Item=>Item.Id=="My-Studio");
        Assert.Equal("My Studio",Entry.DisplayName);
        Assert.False(Entry.BuiltIn);
        Assert.Equal(Studio.Lights.Count,Entry.Template!.Lights.Count);
        Assert.Equal("My-Studio",Entry.Template.Id);
        Assert.Empty(Directory.GetFiles(this.Store.Directory,"*.tmp"));
    }
    [Fact]
    public void Duplicate_NeverCollidesWithAReservedBuiltInName(){
        var Copy=this.Store.Duplicate(PreviewLevelBuiltIns.Default(),"Default");
        Assert.NotEqual("Default",Copy.Id,StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Default-2",Copy.Id);
        var Studio=this.Store.Duplicate(PreviewLevelBuiltIns.Default(),"studio");
        Assert.Equal("studio-2",Studio.Id);
    }
    [Theory]
    [InlineData("../../evil")]
    [InlineData("a\\b/c")]
    [InlineData("CON:?*|\"<>")]
    [InlineData("   ")]
    [InlineData("....")]
    public void Duplicate_SanitizesNamesSoTheFileStaysInsideTheFolder(string Name){
        if(string.IsNullOrWhiteSpace(Name)){
            Assert.Throws<ArgumentException>(()=>this.Store.Duplicate(PreviewLevelBuiltIns.Studio(),Name));
            return;
        }
        var Entry=this.Store.Duplicate(PreviewLevelBuiltIns.Studio(),Name);
        var Path=System.IO.Path.GetFullPath(this.Store.PathFor(Entry.Id));
        Assert.Equal(System.IO.Path.GetFullPath(this.Store.Directory),System.IO.Path.GetDirectoryName(Path));
        Assert.True(File.Exists(Path));
        Assert.DoesNotContain("..",Entry.Id);
    }
    [Fact]
    public void Duplicate_AllowsJapaneseNames(){
        var Entry=this.Store.Duplicate(PreviewLevelBuiltIns.Studio(),"夜のスタジオ");
        Assert.Equal("夜のスタジオ",Entry.Id);
        Assert.Equal("夜のスタジオ",this.Store.Load().Single(static Item=>!Item.BuiltIn).DisplayName);
    }
    [Fact]
    public void Rename_ChangesTheDisplayNameButKeepsTheIdentity(){
        var Entry=this.Store.Duplicate(PreviewLevelBuiltIns.Studio(),"Alpha");
        this.Store.Rename(Entry.Id,"Beta");
        var Loaded=this.Store.Load().Single(static Item=>!Item.BuiltIn);
        Assert.Equal("Alpha",Loaded.Id);
        Assert.Equal("Beta",Loaded.DisplayName);
        Assert.Throws<ArgumentException>(()=>this.Store.Rename("Alpha"," "));
    }
    [Fact]
    public void BuiltIns_CannotBeRenamedDeletedOrOverwritten(){
        foreach(var Id in PreviewLevelBuiltIns.ReservedIds){
            Assert.Throws<PreviewLevelProtectedException>(()=>this.Store.Rename(Id,"Hacked"));
            Assert.Throws<PreviewLevelProtectedException>(()=>this.Store.Delete(Id));
            Assert.Throws<PreviewLevelProtectedException>(()=>this.Store.Delete(Id.ToUpperInvariant()));
        }
        Assert.Throws<PreviewLevelProtectedException>(()=>this.Store.Save(PreviewLevelBuiltIns.Studio()));
        var Impostor=PreviewLevelBuiltIns.Studio();
        Impostor.BuiltIn=false;
        Assert.Throws<PreviewLevelProtectedException>(()=>this.Store.Save(Impostor));
        Assert.False(Directory.Exists(this.Store.Directory));
    }
    [Fact]
    public void Save_PersistsEditsAndRefusesInvalidTemplates(){
        var Entry=this.Store.Duplicate(PreviewLevelBuiltIns.Studio(),"Edit me");
        var Template=Entry.Template!;
        Template.Lights[0].Intensity=7.5f;
        this.Store.Save(Template);
        Assert.Equal(7.5f,this.Store.Load().Single(static Item=>!Item.BuiltIn).Template!.Lights[0].Intensity);
        Template.Lights[0].Intensity=float.NaN;
        Assert.Throws<ArgumentException>(()=>this.Store.Save(Template));
        Assert.Equal(7.5f,this.Store.Load().Single(static Item=>!Item.BuiltIn).Template!.Lights[0].Intensity);
    }
    [Fact]
    public void Delete_RemovesOnlyTheProjectFile(){
        var Entry=this.Store.Duplicate(PreviewLevelBuiltIns.Studio(),"Temp");
        this.Store.Delete(Entry.Id);
        Assert.False(File.Exists(this.Store.PathFor(Entry.Id)));
        Assert.Equal(4,this.Store.Load().Count);
        this.Store.Delete(Entry.Id);
    }
    [Fact]
    public void BrokenAndReservedFilesBecomeUnusableEntriesWithAReason(){
        var Folder=this.Store.Directory;
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder,"broken.cgepreview"),"{ not json",new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(Folder,"future.cgepreview"),"{ \"SchemaVersion\": 99, \"DisplayName\": \"x\" }");
        File.WriteAllText(Path.Combine(Folder,"Studio.cgepreview"),PreviewLevelBuiltIns.Studio().ToJson());
        File.WriteAllText(Path.Combine(Folder,"huge.cgepreview"),new string(' ',PreviewLevelStore.MaximumFileBytes+1));
        File.WriteAllText(Path.Combine(Folder,"ignored.txt"),"x");
        var Entries=this.Store.Load();
        var Broken=Entries.Where(static Entry=>!Entry.BuiltIn).ToArray();
        Assert.Equal(new[]{ "broken","future","huge","Studio" },Broken.Select(static Entry=>Entry.Id));
        Assert.All(Broken,static Entry=>{
            Assert.False(Entry.IsUsable);
            Assert.False(string.IsNullOrWhiteSpace(Entry.Error));
            Assert.NotNull(Entry.FilePath);
        });
        Assert.Contains("reserved",Broken.Single(static Entry=>Entry.Id=="Studio").Error!);
        // 組み込みの Studio は影響を受けない。
        Assert.True(Entries.Single(static Entry=>Entry.Id=="studio").IsUsable);
    }
    [Fact]
    public void FilesCanBeReadWhileAnEditorHoldsThemOpen(){
        var Entry=this.Store.Duplicate(PreviewLevelBuiltIns.Studio(),"Shared");
        using var Handle=new FileStream(this.Store.PathFor(Entry.Id),FileMode.Open,FileAccess.ReadWrite,FileShare.ReadWrite);
        Assert.True(this.Store.Load().Single(static Item=>!Item.BuiltIn).IsUsable);
    }
    [Fact]
    public void MakeId_IsUniqueAndBounded(){
        var Taken=new HashSet<string>(StringComparer.OrdinalIgnoreCase){ "a","a-2" };
        Assert.Equal("A-3",PreviewLevelStore.MakeId("A",Taken));
        Assert.Equal("preview-level",PreviewLevelStore.MakeId("***",new HashSet<string>()));
        Assert.True(PreviewLevelStore.MakeId(new string('x',500),new HashSet<string>()).Length<=64);
        Assert.Equal("a-b",PreviewLevelStore.MakeId("a  b",new HashSet<string>()));
    }
    [Fact]
    public void Preferences_AreIsolatedPerProjectAndNormalizeThePath(){
        var Settings=new MemorySettings();
        var One=new PreviewLevelPreferences(Settings,@"C:\Games\One");
        var Two=new PreviewLevelPreferences(Settings,@"C:\Games\Two");
        One.LastUsedId="studio";
        Two.LastUsedId="outdoor";
        One.DefaultId="neutral";
        Assert.Equal("studio",One.LastUsedId);
        Assert.Equal("outdoor",Two.LastUsedId);
        Assert.Equal("neutral",One.DefaultId);
        Assert.Null(Two.DefaultId);
        // 大文字小文字と末尾の区切りは同じプロジェクトとして扱う。
        Assert.Equal("studio",new PreviewLevelPreferences(Settings,@"c:\games\one\").LastUsedId);
        Assert.Equal(PreviewLevelPreferences.ProjectHash(@"C:\Games\One"),PreviewLevelPreferences.ProjectHash(@"c:\games\ONE\"));
        Assert.NotEqual(PreviewLevelPreferences.ProjectHash(@"C:\Games\One"),PreviewLevelPreferences.ProjectHash(@"C:\Games\Two"));
        Assert.Equal(16,PreviewLevelPreferences.ProjectHash(@"C:\Games\One").Length);
    }
    [Fact]
    public void Preferences_SupportKindDefaultsForFutureAssetTypesAndClearing(){
        var Settings=new MemorySettings();
        var Preferences=new PreviewLevelPreferences(Settings,@"C:\Games\One");
        Preferences.SetKindDefaultId("StaticMesh","studio");
        Preferences.SetKindDefaultId("Texture","neutral");
        Assert.Equal("studio",Preferences.KindDefaultId("StaticMesh"));
        Assert.Equal("neutral",Preferences.KindDefaultId("Texture"));
        Assert.Null(Preferences.KindDefaultId("SkeletalMesh"));
        Preferences.LastUsedId="outdoor";
        Preferences.LastUsedId=null;
        Assert.Null(Preferences.LastUsedId);
        Assert.DoesNotContain(Settings.Values.Keys,static Key=>Key.Contains("LastUsed"));
        Settings.Values[Settings.Values.Keys.First(static Key=>Key.Contains("KindDefault.StaticMesh"))]="  ";
        Assert.Null(Preferences.KindDefaultId("StaticMesh"));
    }
    [Fact]
    public void Resolver_PrefersLastUsedThenKindDefaultThenProjectDefault(){
        var Entries=this.Store.Load();
        var Result=PreviewLevelResolver.Resolve(Entries,"outdoor","studio","neutral");
        Assert.Equal("outdoor",Result.Template.Id);
        Assert.False(Result.FellBack);
        Assert.Equal("studio",PreviewLevelResolver.Resolve(Entries,null,"studio","neutral").Template.Id);
        Assert.Equal("neutral",PreviewLevelResolver.Resolve(Entries,null,null,"neutral").Template.Id);
        Assert.Equal("STUDIO".ToLowerInvariant(),PreviewLevelResolver.Resolve(Entries,"STUDIO").Template.Id);
    }
    [Fact]
    public void Resolver_WithoutAnyPreferenceUsesDefaultWithoutAnError(){
        var Result=PreviewLevelResolver.Resolve(this.Store.Load(),null,null,null);
        Assert.Equal("default",Result.Template.Id);
        Assert.True(Result.Template.BuiltIn);
        Assert.Null(Result.Error);
        Assert.False(Result.FellBack);
    }
    [Fact]
    public void Resolver_FallsBackSafelyAndReportsWhenTheSelectionIsMissingOrBroken(){
        var Folder=this.Store.Directory;
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder,"broken.cgepreview"),"{ nope");
        var Entries=this.Store.Load();
        var Missing=PreviewLevelResolver.Resolve(Entries,"deleted-long-ago");
        Assert.Equal("default",Missing.Template.Id);
        Assert.True(Missing.FellBack);
        Assert.Contains("deleted-long-ago",Missing.Error!);
        var Broken=PreviewLevelResolver.Resolve(Entries,"broken");
        Assert.Equal("default",Broken.Template.Id);
        Assert.True(Broken.FellBack);
        Assert.Contains("broken",Broken.Error!);
        // 前回のものが使えなくても、使える既定があればそれを使い、理由は残す。
        var Partial=PreviewLevelResolver.Resolve(Entries,"broken",null,"studio");
        Assert.Equal("studio",Partial.Template.Id);
        Assert.NotNull(Partial.Error);
    }
}
