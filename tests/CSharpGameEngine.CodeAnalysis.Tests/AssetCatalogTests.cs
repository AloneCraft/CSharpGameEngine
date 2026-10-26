using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis.Tests;

/// <summary>ADR-0077 stage 7：<c>Assets/</c>は型付き参照になり、存在しないアセットを参照するシーンはエラーとなる。</summary>
public sealed class AssetCatalogTests {
    private const string Root=SceneProjectOptions.ProjectDirectory;

    private static AdditionalText File(string RelativePath,string Text="")=>new InMemoryAdditionalText(Root+RelativePath.Replace('/','\\'),Text);

    private static GeneratorDriverRunResult Run(params AdditionalText[] Files){
        var Compilation=GameSystemGeneratorTestHelper.CreateCompilation([GameSystemGeneratorTestHelper.CommonComponents,"""
            using CSharpGameEngine.Entities;
            using Sample;

            namespace Game;

            internal struct Any : ISystem
            {
                public void Update(ref Position position) { }
            }
            """]);
        return GameSystemGeneratorTestHelper.RunGenerator(Compilation,Files);
    }

    [Fact]
    public void RegisteredImagesGenerateSeparateTypedOutputsAndDefault(){
        var Options=new Dictionary<string,string>{["image.defaultOutput"]="image",
            ["image.outputs.0.key"]="color",["image.outputs.0.kind"]="texture",["image.outputs.0.usage"]="color",
            ["image.outputs.1.key"]="normal",["image.outputs.1.kind"]="texture",["image.outputs.1.usage"]="normal",
            ["image.outputs.2.key"]="image",["image.outputs.2.kind"]="image",["image.outputs.2.usage"]="color"};
        var Metadata=System.Text.Json.JsonSerializer.Serialize(new { schemaVersion=1,assetId="b52d509c-08b1-4cf0-9f3c-264f1a4ee9ce",sourceUri="Assets/Textures/Wall.png",importer="cge.image",options=Options });
        var Result=Run(File("Assets/Textures/Wall.png"),File("Assets/Textures/Wall.png.meta",Metadata));
        Assert.Empty(Result.Diagnostics);var Text=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.Assets.g.cs");
        Assert.Contains("internal static class Wall {",Text);Assert.Contains("TextureAsset Color = new(\"Assets/Textures/Wall.png:color\")",Text);
        Assert.Contains("TextureAsset Normal = new(\"Assets/Textures/Wall.png:normal\")",Text);Assert.Contains("ImageAsset Image = new(\"Assets/Textures/Wall.png:image\")",Text);
        Assert.Contains("ImageAsset Default=>Image",Text);
    }
    [Fact]
    public void AssetFiles_BecomeTypedMembersNestedByFolder(){
        var Result=Run(
            File("Assets/Sounds/Jump.wav"),
            File("Assets/Characters/Mannequins/SKM_Quinn_Simple.glb"),
            File("Assets/Stage/Floor.png"),
            File("Assets/Sounds/readme.txt"),
            File("Assets/Editor/ComponentIcons/Light.svg"),
            File("Other/Sounds/Elsewhere.wav"));

        Assert.Empty(Result.Diagnostics);
        var Catalog=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.Assets.g.cs");

        Assert.Contains("internal static class Assets {",Catalog);
        Assert.Contains("internal static class Sounds {",Catalog);
        Assert.Contains("public static readonly global::CSharpGameEngine.Authoring.SoundAsset Jump = new(\"Assets/Sounds/Jump.wav\");",Catalog);
        Assert.Contains("internal static class Mannequins {",Catalog);
        Assert.Contains("global::CSharpGameEngine.Authoring.ModelAsset SKM_Quinn_Simple = new(\"Assets/Characters/Mannequins/SKM_Quinn_Simple.glb\");",Catalog);
        Assert.Contains("global::CSharpGameEngine.Authoring.TextureAsset Floor = new(\"Assets/Stage/Floor.png\");",Catalog);
        // Assets配下にある既知の種別のファイルだけを対象にする。テキストファイル、エディターアイコン、Assets外のファイルは含めない。
        Assert.DoesNotContain("readme",Catalog);
        Assert.DoesNotContain("Light",Catalog);
        Assert.DoesNotContain("Elsewhere",Catalog);
    }

    [Fact]
    public void ABakedFont_BecomesAFontAsset(){
        var Result=Run(File("Assets/Fonts/Display.cgefont"),File("Assets/Fonts/Display.ttf"));

        Assert.Empty(Result.Diagnostics);
        var Catalog=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.Assets.g.cs");

        Assert.Contains("global::CSharpGameEngine.Authoring.FontAsset Display = new(\"Assets/Fonts/Display.cgefont\");",Catalog);
        // ソースフォントはアセット種別ではない。ゲームが読み込むのはベイク済みファイルだけ。
        Assert.DoesNotContain("Display_ttf",Catalog);
    }

    [Fact]
    public void NamesThatAreNotIdentifiers_AreMadeIntoOnes_AndSameStemsDoNotCollide(){
        var Result=Run(File("Assets/Models/3d box.glb"),File("Assets/Models/Box.glb"),File("Assets/Models/Box.fbx"));
        var Catalog=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.Assets.g.cs");

        Assert.Contains("ModelAsset _3d_box = new(\"Assets/Models/3d box.glb\");",Catalog);
        Assert.Contains("ModelAsset Box = new(\"Assets/Models/Box.fbx\");",Catalog);
        Assert.Contains("ModelAsset Box_glb = new(\"Assets/Models/Box.glb\");",Catalog);
    }

    [Fact]
    public void WithoutAnyAssetFile_NothingIsGenerated(){
        var Result=Run();

        Assert.DoesNotContain(Result.GeneratedTrees,static Tree=>Tree.FilePath.EndsWith("GameSystem.Assets.g.cs",StringComparison.Ordinal));
    }

    [Fact]
    public void ASceneThatNamesAMissingAsset_ReportsScene208AtThePath(){
        const string Scene="""
            {
              "schemaVersion": 3,
              "className": "Main",
              "startupOrder": 0,
              "root": { "id": "11111111-1111-5111-8111-111111111111", "name": "Main", "components": [
                { "type": "CSharpGameEngine.Entities.Transform", "members": {} },
                { "type": "Sample.Mesh", "members": { "AssetPath": "Assets/Missing/Cube.glb" } }
              ], "children": [] }
            }
            """;
        var Result=Run(File("Assets/Present/Cube.glb"),new InMemoryAdditionalText(Root+@"Scenes\Main.json",Scene));
        var Diagnostic=Assert.Single(Result.Diagnostics,static D=>D.Id=="SCENE208");

        Assert.Contains("Assets/Missing/Cube.glb",Diagnostic.GetMessage());
        // 0始まり。Meshコンポーネントは文書の7行目。
        Assert.Equal(6,Diagnostic.Location.GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    public void PluginAssets_AreUnderAssetsPlugins_WithTheirOutputPaths(){
        // ADR-0079：プロジェクトプラグインのアセット（Plugins/<Name>/Assets）はプロジェクト内のパスを維持する。エンジンプラグインのアセットは同じ形式のパスへコピーする。
        var Result=Run(
            File("Plugins/Weather/Assets/Sounds/Rain.wav"),
            new InMemoryAdditionalText(SceneProjectOptions.EnginePluginDirectory+@"Assets\Cars\Sedan.glb",""),
            File("Plugins/Weather/Readme/Notes.wav"));
        var Catalog=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.Assets.g.cs");

        Assert.Contains("internal static class Plugins {",Catalog);
        Assert.Contains("SoundAsset Rain = new(\"Plugins/Weather/Assets/Sounds/Rain.wav\");",Catalog);
        Assert.Contains("ModelAsset Sedan = new(\"Plugins/Vehicles/Assets/Cars/Sedan.glb\");",Catalog);
        Assert.Contains("internal static class Weather {",Catalog);
        Assert.Contains("internal static class Vehicles {",Catalog);
        // Assetsフォルダー外にあるプラグインファイルはアセットではない。
        Assert.DoesNotContain("Notes",Catalog);
    }

    [Fact]
    public void ASceneThatNamesAPluginAsset_IsCheckedLikeAGameAsset(){
        const string Scene="""
            { "schemaVersion": 3, "className": "Main", "root": { "id": "11111111-1111-5111-8111-111111111111", "name": "Main",
              "components": [
                { "type": "Sample.Mesh", "members": { "AssetPath": "Plugins/Vehicles/Assets/Cars/Sedan.glb" } },
                { "type": "Sample.Mesh", "members": { "AssetPath": "Plugins/Vehicles/Assets/Cars/Missing.glb" } }
              ], "children": [] } }
            """;
        var Result=Run(new InMemoryAdditionalText(SceneProjectOptions.EnginePluginDirectory+@"Assets\Cars\Sedan.glb",""),new InMemoryAdditionalText(Root+@"Scenes\Main.json",Scene));
        var Diagnostic=Assert.Single(Result.Diagnostics,static D=>D.Id=="SCENE208");

        Assert.Contains("Plugins/Vehicles/Assets/Cars/Missing.glb",Diagnostic.GetMessage());
    }

    [Fact]
    public void ASceneThatNamesAnExistingAsset_IsAccepted(){
        const string Scene="""
            { "schemaVersion": 3, "className": "Main", "root": { "id": "11111111-1111-5111-8111-111111111111", "name": "Main",
              "components": [ { "type": "Sample.Mesh", "members": { "AssetPath": "Assets/Present/Cube.glb" } } ], "children": [] } }
            """;
        var Result=Run(File("Assets/Present/Cube.glb"),new InMemoryAdditionalText(Root+@"Scenes\Main.json",Scene));

        Assert.DoesNotContain(Result.Diagnostics,static D=>D.Id=="SCENE208");
    }
}
