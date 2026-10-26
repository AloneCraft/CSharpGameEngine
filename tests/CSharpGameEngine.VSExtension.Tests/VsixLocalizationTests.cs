using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CSharpGameEngine.VSExtension.Tests;

[Collection(VsixLocalizationTestCollection.Name)]
public sealed class VsixLocalizationTests {
    private static readonly HashSet<string>IntentionalEnglishJapaneseValues=new(StringComparer.Ordinal){
        "Common_ProductTitle",
        "DefinitionInputGesture",
        "PhysicsLabel",
        "PhysicsPanelOpenGlyph",
        "PhysicsPanelCloseGlyph",
        "SceneRenderModeBaseColor",
        "SceneRenderModeLit",
        "SceneRenderModeNormal",
        "SceneRenderModeWireframe",
        // アセットエディターのチャンネル名・倍率は言語に依存しない記号である。
        "AssetEditor_ActualSize",
        "AssetEditor_ChannelAlpha",
        "AssetEditor_ChannelBlue",
        "AssetEditor_ChannelColor",
        "AssetEditor_ChannelGreen",
        "AssetEditor_ChannelRed",
        "AssetEditor_Zoom",
        // 標準のプレビューレベル名は仕様どおりの固有名（Default／Studio／Outdoor／Neutral）。OK は共通の記号。
        "PreviewLevel_Default",
        "PreviewLevel_Neutral",
        "PreviewLevel_Ok",
        "PreviewLevel_Outdoor",
        "PreviewLevel_Studio",
        // Scene Editor の JSON タブ名は形式名に基づく。XAML エディターのタブも同様である。
        "SceneEditorJsonTab",
        "SceneToolTransform",
    };
    [Fact]
    public void Get_UsesEnglishJapaneseAndUnsupportedCultureFallback(){
        Assert.Equal("Scene Designer",VsixText.Get("PaneSceneDesigner",CultureInfo.GetCultureInfo("en-US")));
        Assert.Equal("シーン デザイナー",VsixText.Get("PaneSceneDesigner",CultureInfo.GetCultureInfo("ja-JP")));
        Assert.Equal("Scene Designer",VsixText.Get("PaneSceneDesigner",CultureInfo.GetCultureInfo("fr-FR")));
    }
    [Fact]
    public void Capture_DoesNotFollowLaterThreadCultureChanges(){
        CultureInfo Original=CultureInfo.CurrentUICulture;
        try {
            CultureInfo.CurrentUICulture=CultureInfo.GetCultureInfo("ja-JP");
            VsixLocalization.Capture(CultureInfo.CurrentUICulture);
            CultureInfo.CurrentUICulture=CultureInfo.GetCultureInfo("en-US");
            Assert.Equal("ja-JP",VsixLocalization.UICulture.Name);
            Assert.Equal("シーン デザイナー",VsixText.Get("PaneSceneDesigner"));
        } finally {
            CultureInfo.CurrentUICulture=Original;
            VsixLocalization.Capture(Original);
        }
    }
    [Fact]
    public void MissingNeutralKey_ReturnsVisibleDiagnosticMarker(){
        Assert.Equal("[Resource.DoesNotExist]",VsixText.Get("Resource.DoesNotExist",CultureInfo.GetCultureInfo("ja-JP")));
    }
    [Fact]
    public void EnglishAndJapaneseResources_HaveSameNonEmptyKeysAndPlaceholders(){
        Dictionary<string,string>English=ReadResource(CultureInfo.InvariantCulture);
        Dictionary<string,string>Japanese=ReadResource(CultureInfo.GetCultureInfo("ja-JP"));
        Assert.Equal(English.Keys.OrderBy(Key=>Key),Japanese.Keys.OrderBy(Key=>Key));
        Assert.All(English,Pair=>Assert.False(string.IsNullOrWhiteSpace(Pair.Value),Pair.Key));
        Assert.All(Japanese,Pair=>Assert.False(string.IsNullOrWhiteSpace(Pair.Value),Pair.Key));
        foreach(string Key in English.Keys){
            Assert.Equal(Placeholders(English[Key]),Placeholders(Japanese[Key]));
        }
    }
    [Fact]
    public void JapaneseResources_OnlyKeepExplicitlyIntentionalEnglishValues(){
        Dictionary<string,string>English=ReadResource(CultureInfo.InvariantCulture);
        Dictionary<string,string>Japanese=ReadResource(CultureInfo.GetCultureInfo("ja-JP"));
        string[] EqualValues=English.Keys
            .Where(Key=>string.Equals(English[Key],Japanese[Key],StringComparison.Ordinal))
            .OrderBy(Key=>Key,StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(IntentionalEnglishJapaneseValues.OrderBy(Key=>Key,StringComparer.Ordinal),EqualValues);
        Assert.Equal("重心",Japanese["PhysicsCenterOfMass"]);
        Assert.Equal("表示",Japanese["SceneViewLabel"]);
    }
    [Fact]
    public void XamlLocalizationKeys_AllExistInNeutralResource(){
        Dictionary<string,string>English=ReadResource(CultureInfo.InvariantCulture);
        string DirectoryPath=Path.Combine(VsixBuildIdentity.RepoRoot,"tools","CSharpGameEngine.VSExtension");
        Regex Pattern=new(@"\{local:Loc\s+([A-Za-z0-9_.]+)\}",RegexOptions.CultureInvariant);
        string[] Keys=Directory.EnumerateFiles(DirectoryPath,"*.xaml",SearchOption.TopDirectoryOnly)
            .SelectMany(FilePath=>Pattern.Matches(File.ReadAllText(FilePath)).Cast<Match>())
            .Select(Match=>Match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Assert.NotEmpty(Keys);
        Assert.All(Keys,Key=>Assert.True(English.ContainsKey(Key),$"Missing neutral localization key: {Key}"));
    }
    [Fact]
    public void XamlPresentationAttributes_DoNotContainUnlocalizedText(){
        string DirectoryPath=Path.Combine(VsixBuildIdentity.RepoRoot,"tools","CSharpGameEngine.VSExtension");
        string[] PresentationAttributes=[
            "Text","Content","ToolTip","Header","Title",
            "AutomationProperties.Name","AutomationProperties.HelpText","InputGestureText",
        ];
        var Violations=Directory.EnumerateFiles(DirectoryPath,"*.xaml",SearchOption.TopDirectoryOnly)
            .SelectMany(FilePath=>XDocument.Load(FilePath).Descendants()
                .SelectMany(Element=>Element.Attributes()
                    .Where(Attribute=>PresentationAttributes.Contains(Attribute.Name.LocalName,StringComparer.Ordinal)
                        &&Attribute.Value.Length>0
                        &&!Attribute.Value.StartsWith("{",StringComparison.Ordinal)
                        &&Attribute.Value!="×")
                    .Select(Attribute=>$"{Path.GetFileName(FilePath)}: {Attribute.Name.LocalName}=\"{Attribute.Value}\"")))
            .ToArray();
        Assert.Empty(Violations);
    }
    private static Dictionary<string,string>ReadResource(CultureInfo Culture){
        ResourceManager Manager=new("CSharpGameEngine.VSExtension.VsixStrings",typeof(VsixText).Assembly);
        ResourceSet Set=Manager.GetResourceSet(Culture,true,false)!;
        return Set.Cast<DictionaryEntry>().ToDictionary(
            Entry=>(string)Entry.Key,Entry=>(string)Entry.Value,StringComparer.Ordinal);
    }
    private static string[]Placeholders(string Value)=>Regex.Matches(Value,@"\{(\d+)(?:[^}]*)\}")
        .Cast<Match>()
        .Select(Match=>Match.Groups[1].Value)
        .OrderBy(Value=>Value,StringComparer.Ordinal)
        .ToArray();
}
