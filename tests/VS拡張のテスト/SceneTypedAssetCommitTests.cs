using System;
using System.Linq;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Json;
using CSharpGameEngine.SceneAnalysis.Tests;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
namespace CSharpGameEngine.VSExtension.Tests;
/// <summary>
/// JSON のシーンで、TextureAsset/ImageAsset の欄へ生成カタログの値を入れると、JSON の Path の値として保存する。
/// カタログの名前(静的な読み取り専用の値)は JSON の形が無いので、名前のまま書こうとすると保存できない。
/// </summary>
public sealed class SceneTypedAssetCommitTests {
    private const string JsonPath=@"C:\Game\Scenes\MainScene.json";
    private const string NodeId="17848c4d-7c3e-47a9-9c82-64eb04bb29b8";
    private static readonly SceneKey Key=new("project-a","Game.MainScene");
    private const string AssetTypes="""
        namespace CSharpGameEngine.Authoring {
            public readonly struct TextureAsset(string Path) { public readonly string Path=Path; }
            public readonly struct ImageAsset(string Path) { public readonly string Path=Path; }
        }
        """;
    private const string Catalog="""
        internal static class Assets {
            internal static class Photo {
                public static readonly global::CSharpGameEngine.Authoring.TextureAsset Color=new("Assets/Photo.png:color");
                public static readonly global::CSharpGameEngine.Authoring.ImageAsset Original=new("Assets/Photo.png:original");
                public static global::CSharpGameEngine.Authoring.TextureAsset Default=>Color;
                public static global::CSharpGameEngine.Authoring.TextureAsset Mutable=new("Assets/Photo.png:mutable");
                private static readonly global::CSharpGameEngine.Authoring.TextureAsset Hidden=new("Assets/Photo.png:hidden");
                public static global::CSharpGameEngine.Authoring.TextureAsset Computed=>Get();
                private static global::CSharpGameEngine.Authoring.TextureAsset Get()=>Color;
                public static global::CSharpGameEngine.Authoring.TextureAsset CycleA=>CycleB;
                public static global::CSharpGameEngine.Authoring.TextureAsset CycleB=>CycleA;
            }
        }
        """;
    [Theory]
    [InlineData("TextureAsset",false,"Color")]
    [InlineData("TextureAsset",true,"Color")]
    [InlineData("ImageAsset",false,"Original")]
    [InlineData("ImageAsset",true,"Original")]
    [InlineData("TextureAsset",false,"Default")]
    public void TypedCatalogChoice_WritesExistingJsonValueAndRoundTrips(string Type,bool Property,string Choice){
        var Seed=CreateSeed(Type,Property,Catalog);
        var Member=EditableMember(Seed);
        var Expression="global::Assets.Photo."+Choice;
        var Request=SceneInspectorCommitPlanner.CreateCommit(Key,1,Member,Expression);
        var Plan=SceneSourceCommitPlanner.Plan(Seed.Context,Seed.Snapshot,Request,Seed.Reconciler,CancellationToken.None);
        Assert.True(Plan.Succeeded,Plan.ErrorMessage);
        var After=Plan.WriteStep!.Before.WithChanges(Plan.WriteStep.Changes).ToString();
        var ExpectedPath="Assets/Photo.png:"+(Type=="TextureAsset"?"color":"original");
        Assert.Equal(Seed.Context.Text.ToString().Replace("\"Path\": \"\"","\"Path\": \""+ExpectedPath+"\""),After);
        Assert.Contains(NodeId,After);
        Assert.Equal(Seed.Snapshot.Projection!.Nodes.Single().Id,Plan.PredictedProjection!.Nodes.Single().Id);
        var Value=Assert.Single(Plan.PredictedModel!.Root!.Components).Members.Single(M=>M.MemberName=="Value").Value;
        Assert.Equal(SceneValueKind.ObjectCreation,Value.Kind);
        Assert.Equal("CSharpGameEngine.Authoring."+Type,Value.Type!.ToDisplayString());
        Assert.Equal(ExpectedPath,Assert.Single(Value.Arguments).ConstantValue);
        var Reparsed=CreateSeed(Type,Property,Catalog,After);
        Assert.False(Reparsed.Context.Model.HasErrors);
        Assert.Equal(ExpectedPath,Assert.Single(Assert.Single(Reparsed.Context.Model.Root!.Components).Members.Single(M=>M.MemberName=="Value").Value.Arguments).ConstantValue);
    }
    [Fact]
    public void JsonObjectValue_OffersTypedCatalogChoices(){
        var Seed=CreateSeed("TextureAsset",false,Catalog);
        var Member=EditableMember(Seed);
        Assert.Contains(Member.Candidates,Candidate=>Candidate.EmitExpression=="global::Assets.Photo.Color");
        Assert.Contains(Member.Candidates,Candidate=>Candidate.EmitExpression=="global::Assets.Photo.Default");
        Assert.DoesNotContain(Member.Candidates,Candidate=>Candidate.EmitExpression=="global::Assets.Photo.Mutable");
        Assert.DoesNotContain(Member.Candidates,Candidate=>Candidate.EmitExpression=="global::Assets.Photo.Original");
    }
    [Fact]
    public void StoredTypedValue_ShowsItsCatalogChoiceAndEquivalentChoicesAreNoOps(){
        var Initial=CreateSeed("TextureAsset",false,Catalog);
        var Json=Initial.Context.Text.ToString().Replace("\"Path\": \"\"","\"Path\": \"Assets/Photo.png:color\"");
        var Seed=CreateSeed("TextureAsset",false,Catalog,Json);
        var Member=EditableMember(Seed);
        var Color=Assert.Single(Member.Candidates,Candidate=>Candidate.EmitExpression=="global::Assets.Photo.Color");
        var Alias=Assert.Single(Member.Candidates,Candidate=>Candidate.EmitExpression=="global::Assets.Photo.Default");
        Assert.Equal(Color.Name,new InspectorMemberViewModel(Member).SelectedChoiceName);
        Assert.False(SceneInspectorCommitPlanner.TryCreateChoiceCommit(Key,1,Member,Color.Name,out var Same));
        Assert.Null(Same);
        Assert.False(SceneInspectorCommitPlanner.TryCreateChoiceCommit(Key,1,Member,Alias.Name,out var Equivalent));
        Assert.Null(Equivalent);
        Assert.Equal(Json,Seed.Context.Text.ToString());
    }
    [Theory]
    [InlineData("Locked")]
    [InlineData("ReadOnlyProperty")]
    [InlineData("Static")]
    [InlineData("Private")]
    public void NonWritableDestination_RejectsDirectTypedOperation(string Destination){
        var Seed=CreateSeed("TextureAsset",false,Catalog);
        var Node=Assert.Single(Seed.Snapshot.Projection!.Nodes).Id;
        var Operation=new SetComponentMemberOperation(Key,Node,1,"global::Game.ImageProbe",Destination,"global::Assets.Photo.Color");
        Assert.False(SceneModelEditApplier.TryApply(Seed.Context.Model,Seed.Snapshot.Projection,Seed.Reconciler,Key,[Operation],out _,out _,Seed.Context.Compilation));
    }
    [Fact]
    public void DefaultTypedValue_CanBeAssigned(){
        var Initial=CreateSeed("TextureAsset",false,Catalog);
        var Json=Initial.Context.Text.ToString().Replace("{ \"Path\": \"\" }","{}");
        var Seed=CreateSeed("TextureAsset",false,Catalog,Json);
        var Request=SceneInspectorCommitPlanner.CreateCommit(Key,1,EditableMember(Seed),"global::Assets.Photo.Color");
        var Plan=SceneSourceCommitPlanner.Plan(Seed.Context,Seed.Snapshot,Request,Seed.Reconciler,CancellationToken.None);
        Assert.True(Plan.Succeeded,Plan.ErrorMessage);
        Assert.Equal("Assets/Photo.png:color",Assert.Single(Assert.Single(Plan.PredictedModel!.Root!.Components).Members.Single(M=>M.MemberName=="Value").Value.Arguments).ConstantValue);
    }
    /// <summary>カタログが証明しない値(書き換えられる・見えない・計算する・循環する・無い・任意の式)は、Path へ平らにせず書かない。</summary>
    [Theory]
    [InlineData("global::Assets.Photo.Original")]
    [InlineData("global::Assets.Photo.Mutable")]
    [InlineData("global::Assets.Photo.Hidden")]
    [InlineData("global::Assets.Photo.Computed")]
    [InlineData("global::Assets.Photo.CycleA")]
    [InlineData("global::Assets.Photo.Missing")]
    [InlineData("global::Assets.Photo.Get()")]
    [InlineData("new global::CSharpGameEngine.Authoring.TextureAsset(\"Assets/arbitrary.png:color\")")]
    public void UnprovenOrIncompatibleChoice_DoesNotWrite(string Expression){
        var Seed=CreateSeed("TextureAsset",false,Catalog);
        var Request=SceneInspectorCommitPlanner.CreateCommit(Key,1,EditableMember(Seed),Expression);
        var Plan=SceneSourceCommitPlanner.Plan(Seed.Context,Seed.Snapshot,Request,Seed.Reconciler,CancellationToken.None);
        Assert.False(Plan.Succeeded);
        Assert.Null(Plan.WriteStep);
        Assert.Equal("",Assert.Single(Assert.Single(Seed.Context.Model.Root!.Components).Members.Single(M=>M.MemberName=="Value").Value.Arguments).ConstantValue);
    }
    [Fact]
    public void CatalogWithStaticConstructor_DoesNotFlattenAnUnprovenRuntimeValue(){
        var Changed=Catalog.Replace("internal static class Photo {","internal static class Photo { static Photo(){ Color=new(\"Assets/other.png:color\"); }");
        var Seed=CreateSeed("TextureAsset",false,Changed);
        var Request=SceneInspectorCommitPlanner.CreateCommit(Key,1,EditableMember(Seed),"global::Assets.Photo.Color");
        var Plan=SceneSourceCommitPlanner.Plan(Seed.Context,Seed.Snapshot,Request,Seed.Reconciler,CancellationToken.None);
        Assert.False(Plan.Succeeded);
        Assert.Null(Plan.WriteStep);
    }
    private static SceneInspectorMember EditableMember(Seed Seed){
        var Node=Assert.Single(Seed.Snapshot.Projection!.Nodes).Id;
        var Selection=new SceneSelection(Key,[Node],Node,null,null,SceneSelectionOrigin.Inspector,1,Guid.NewGuid());
        var Member=Assert.Single(SceneInspectorModelBuilder.Build(Seed.Snapshot,Selection),M=>M.MemberName=="Value");
        Assert.Equal(InspectorEditorKind.AssetReference,Member.EditorKind);
        Assert.True(Member.IsEditable);
        return Member;
    }
    private static Seed CreateSeed(string Type,bool Property,string CatalogSource,string?Json=null){
        var Component="namespace Game { public struct ImageProbe { public global::CSharpGameEngine.Authoring."+Type+" Value"+(Property?" { get; set; }":";")+" public float Gain; public readonly global::CSharpGameEngine.Authoring.TextureAsset Locked; public global::CSharpGameEngine.Authoring.TextureAsset ReadOnlyProperty=>default; public static global::CSharpGameEngine.Authoring.TextureAsset Static; private global::CSharpGameEngine.Authoring.TextureAsset Private; } }";
        var Base=SceneAnalysisTestHelper.CreateCompilation(AssetTypes,Component,CatalogSource);
        var Text=Json??"{\r\n  \"schemaVersion\": 3,\r\n  \"namespace\": \"Game\",\r\n  \"className\": \"MainScene\",\r\n  \"root\": {\r\n    \"id\": \""+NodeId+"\",\r\n    \"name\": \"Probe\",\r\n    \"components\": [\r\n      { \"type\": \"Game.ImageProbe\", \"members\": { \"Value\": { \"Path\": \"\" }, \"Gain\": 3 } }\r\n    ],\r\n    \"children\": []\r\n  }\r\n}\r\n";
        Assert.True(SceneJsonCommitPlanner.TryAnalyze(Text,JsonPath,Base,"Game.MainScene",CancellationToken.None,out var Model,out var Root,out var Compilation,out var Error),Error);
        var Reconciler=new SceneProjectionReconciler();
        var Snapshot=SceneDocumentSnapshot.Create(Key,"document-a",1,1,Model!,false,null,Reconciler);
        return new Seed(new SceneSourceCommitContext(Model!,Root!,SourceText.From(Text),Compilation!),Snapshot,Reconciler);
    }
    private sealed record Seed(SceneSourceCommitContext Context,SceneDocumentSnapshot Snapshot,SceneProjectionReconciler Reconciler);
}
