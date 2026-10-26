using System;
using System.IO;
using System.Linq;
using CSharpGameEngine.SceneAnalysis.Tests;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>プロジェクトに結び付いたアイコン選択と、それを分離した Roslyn 書き換えの契約。</summary>
public sealed class ComponentIconAuthoringTests {
    [Theory]
    [InlineData("Assets/Icons/cube.svg","Assets/Icons/cube.svg")]
    [InlineData("Assets\\Icons\\cube.PNG","Assets/Icons/cube.PNG")]
    public void ProjectIconPath_NormalizesAllowedProjectRelativeImagePaths(string Input,string Expected){
        Assert.True(ProjectIconPath.TryNormalize(Input,out string Actual));
        Assert.Equal(Expected,Actual);
    }

    [Theory]
    [InlineData("../secret.svg")]
    [InlineData("Assets/../../secret.png")]
    [InlineData("C:/outside.svg")]
    [InlineData("/outside.png")]
    [InlineData("Assets/icon.jpg")]
    public void ProjectIconPath_RejectsTraversalAbsoluteAndNonImagePaths(string Input){
        Assert.False(ProjectIconPath.TryNormalize(Input,out _));
    }

    [Fact]
    public void AssetCatalog_ListsOnlyProjectSvgAndPngAndExcludesBuildDirectories(){
        using TemporaryDirectory Directory=new();
        Write(Directory.Path,"Assets/Icons/cube.svg");
        Write(Directory.Path,"Assets/Icons/camera.PNG");
        Write(Directory.Path,"bin/generated.svg");
        Write(Directory.Path,"obj/generated.png");
        Write(Directory.Path,"Assets/Icons/readme.txt");

        ProjectIconAsset[]Assets=ProjectIconAssetCatalog.Enumerate(Directory.Path).ToArray();

        Assert.Equal(
            ["Assets/Icons/camera.PNG","Assets/Icons/cube.svg"],
            Assets.Select(static Asset=>Asset.ProjectRelativePath));
        Assert.All(Assets,Asset=>Assert.True(ProjectIconPath.TryResolve(
            Directory.Path,Asset.ProjectRelativePath,out _,out string Resolved) &&File.Exists(Resolved)));
    }

    [Fact]
    public void SourceEditor_AddsFullyQualifiedAttributeToClickedPartialDeclaration(){
        const string Source="""
            namespace Sample;
            public partial struct Component { public int A; }
            // the second declaration owns the selected icon
            public partial struct Component { public int B; }
            """;
        var Root=CSharpSyntaxTree.ParseText(Source).GetRoot();
        StructDeclarationSyntax Declaration=Root.DescendantNodes().OfType<StructDeclarationSyntax>().Last();

        ComponentIconSourceEditResult Result=ComponentIconSourceEditor.Apply(
            Root,Declaration.SpanStart,"Assets/Icons/component.svg");

        Assert.True(Result.Succeeded,Result.ErrorMessage);
        string Changed=Result.Root!.ToFullString();
        Assert.Contains("[global::CSharpGameEngine.Entities.SceneComponentIconAttribute(\"Assets/Icons/component.svg\")]",Changed);
        Assert.Contains("// the second declaration owns the selected icon",Changed);
        Assert.Equal(1,Changed.Split(new[]{"SceneComponentIcon"},StringSplitOptions.None).Length-1);
    }

    [Fact]
    public void SourceEditor_ReplacesExistingAttributeWithoutDuplicatingIt(){
        const string Source="""
            [global::CSharpGameEngine.Entities.SceneComponentIcon("Old/icon.png")]
            public struct Component { }
            """;
        var Root=CSharpSyntaxTree.ParseText(Source).GetRoot();
        int Position=Root.DescendantNodes().OfType<StructDeclarationSyntax>().Single().SpanStart;

        ComponentIconSourceEditResult Result=ComponentIconSourceEditor.Apply(
            Root,Position,"Assets/New/icon.svg");

        Assert.True(Result.Succeeded,Result.ErrorMessage);
        string Changed=Result.Root!.ToFullString();
        Assert.Contains("Assets/New/icon.svg",Changed);
        Assert.DoesNotContain("Old/icon.png",Changed);
        Assert.Equal(1,Changed.Split(new[]{"SceneComponentIcon"},StringSplitOptions.None).Length-1);
    }

    [Fact]
    public void SourceEditor_DefaultRemovesOnlyTheIconAttribute(){
        const string Source="""
            [System.Obsolete, global::CSharpGameEngine.Entities.SceneComponentIcon("Assets/icon.png")]
            public struct Component { }
            """;
        var Root=CSharpSyntaxTree.ParseText(Source).GetRoot();
        int Position=Root.DescendantNodes().OfType<StructDeclarationSyntax>().Single().SpanStart;

        ComponentIconSourceEditResult Result=ComponentIconSourceEditor.Apply(Root,Position,SelectedProjectRelativePath:null);

        Assert.True(Result.Succeeded,Result.ErrorMessage);
        string Changed=Result.Root!.ToFullString();
        Assert.Contains("System.Obsolete",Changed);
        Assert.DoesNotContain("SceneComponentIcon",Changed);
    }

    [Fact]
    public void SourceEditor_InvalidPathLeavesTheRootUnchanged(){
        const string Source="public struct Component { }";
        var Root=CSharpSyntaxTree.ParseText(Source).GetRoot();
        int Position=Root.DescendantNodes().OfType<StructDeclarationSyntax>().Single().SpanStart;

        ComponentIconSourceEditResult Result=ComponentIconSourceEditor.Apply(Root,Position,"../outside.svg");

        Assert.False(Result.Succeeded);
        Assert.Null(Result.Root);
    }

    [Fact]
    public void Resolver_UsesOnlyTheSemanticAttributeAndFallsBackForMissingOrUnsafeAssets(){
        using TemporaryDirectory Directory=new();
        string ProjectDirectory=Path.Combine(Directory.Path,"Project");
        Write(ProjectDirectory,"Assets/Icons/valid.svg");
        Write(Directory.Path,"outside.svg");

        CSharpCompilation Compilation=SceneAnalysisTestHelper.CreateCompilation(
            """
            namespace CSharpGameEngine.Entities {
                [System.AttributeUsage(System.AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
                public sealed class SceneComponentIconAttribute : System.Attribute {
                    public SceneComponentIconAttribute(string projectRelativePath) { }
                }
            }
            """,
            """
            namespace Other {
                public sealed class SceneComponentIconAttribute : System.Attribute {
                    public SceneComponentIconAttribute(string projectRelativePath) { }
                }
            }
            namespace Sample {
                [global::CSharpGameEngine.Entities.SceneComponentIconAttribute("Assets/Icons/valid.svg")]
                public struct ValidComponent { }
                [global::CSharpGameEngine.Entities.SceneComponentIconAttribute("Assets/Icons/missing.png")]
                public struct MissingComponent { }
                [global::CSharpGameEngine.Entities.SceneComponentIconAttribute("../outside.svg")]
                public struct UnsafeComponent { }
                [Other.SceneComponentIconAttribute("Assets/Icons/valid.svg")]
                public struct LookalikeAttributedComponent { }
            }
            """);

        INamedTypeSymbol Valid=GetType(Compilation,"Sample.ValidComponent");
        Assert.Equal("Assets/Icons/valid.svg",SceneComponentIconMetadata.TryGetDeclaredProjectRelativePath(Valid));
        SceneVisualIcon ValidIcon=SceneComponentIconResolver.Resolve(Valid,ProjectDirectory);
        Assert.True(ValidIcon.IsProjectAsset);
        Assert.Equal("Assets/Icons/valid.svg",ValidIcon.ProjectRelativePath);
        Assert.Equal(Path.GetFullPath(Path.Combine(ProjectDirectory,"Assets","Icons","valid.svg")),ValidIcon.AbsolutePath);

        AssertDefaultIcon(GetType(Compilation,"Sample.MissingComponent"),ProjectDirectory);
        AssertDefaultIcon(GetType(Compilation,"Sample.UnsafeComponent"),ProjectDirectory);
        INamedTypeSymbol Lookalike=GetType(Compilation,"Sample.LookalikeAttributedComponent");
        Assert.Null(SceneComponentIconMetadata.TryGetDeclaredProjectRelativePath(Lookalike));
        AssertDefaultIcon(Lookalike,ProjectDirectory);
    }

    [Fact]
    public void Resolver_FallsBackForMetadataComponentsEvenWhenTheirAttributePathExistsInThisProject(){
        using TemporaryDirectory Directory=new();
        string ProjectDirectory=Path.Combine(Directory.Path,"Project");
        Write(ProjectDirectory,"Assets/Icons/valid.svg");

        CSharpCompilation MetadataCompilation=SceneAnalysisTestHelper.CreateCompilation(
            """
            namespace CSharpGameEngine.Entities {
                [System.AttributeUsage(System.AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
                public sealed class SceneComponentIconAttribute : System.Attribute {
                    public SceneComponentIconAttribute(string projectRelativePath) { }
                }
            }
            namespace Referenced {
                [global::CSharpGameEngine.Entities.SceneComponentIconAttribute("Assets/Icons/valid.svg")]
                public struct MetadataComponent { }
            }
            """);
        using MemoryStream AssemblyImage=new();
        var EmitResult=MetadataCompilation.Emit(AssemblyImage);
        Assert.True(EmitResult.Success,string.Join(Environment.NewLine,EmitResult.Diagnostics));

        CSharpCompilation Consumer=SceneAnalysisTestHelper.CreateCompilation()
            .AddReferences(MetadataReference.CreateFromImage(AssemblyImage.ToArray()));
        INamedTypeSymbol MetadataType=GetType(Consumer,"Referenced.MetadataComponent");
        Assert.Equal("Assets/Icons/valid.svg",SceneComponentIconMetadata.TryGetDeclaredProjectRelativePath(MetadataType));
        AssertDefaultIcon(MetadataType,ProjectDirectory);
    }

    [Fact]
    public void ProjectIconPath_RefusesMissingOrOutsideFilesEvenWhenTheyHaveImageExtensions(){
        using TemporaryDirectory Directory=new();
        string ProjectDirectory=Path.Combine(Directory.Path,"Project");
        Write(ProjectDirectory,"Assets/Icons/present.svg");
        string OutsidePath=Path.Combine(Directory.Path,"outside.svg");
        File.WriteAllText(OutsidePath,"asset");
        string NonImagePath=Path.Combine(ProjectDirectory,"Assets","Icons","not-an-icon.txt");
        Write(ProjectDirectory,"Assets/Icons/not-an-icon.txt");

        Assert.False(ProjectIconPath.TryResolve(ProjectDirectory,"Assets/Icons/missing.svg",out _,out _));
        Assert.False(ProjectIconPath.TryResolve(ProjectDirectory,"../outside.svg",out _,out _));
        Assert.False(ProjectIconPath.TryResolve(ProjectDirectory,OutsidePath,out _,out _));
        Assert.False(ProjectIconPath.TryCreateRelative(ProjectDirectory,OutsidePath,out _));
        Assert.False(ProjectIconPath.TryCreateRelative(ProjectDirectory,NonImagePath,out _));
        Assert.True(ProjectIconPath.TryCreateRelative(
            ProjectDirectory,Path.Combine(ProjectDirectory,"Assets","Icons","present.svg"),out string RelativePath));
        Assert.Equal("Assets/Icons/present.svg",RelativePath);
    }

    [Fact]
    public void GlyphEligibility_RequiresAnExistingWritableNonGeneratedCSharpSourceFile(){
        using TemporaryDirectory Directory=new();
        string WritablePath=Path.Combine(Directory.Path,"Component.cs");
        string GeneratedPath=Path.Combine(Directory.Path,"Component.g.cs");
        string ReadOnlyPath=Path.Combine(Directory.Path,"ReadOnlyComponent.cs");
        File.WriteAllText(WritablePath,"public struct Component { }");
        File.WriteAllText(GeneratedPath,"public struct GeneratedComponent { }");
        File.WriteAllText(ReadOnlyPath,"public struct ReadOnlyComponent { }");

        Assert.True(ComponentIconGlyphTagger.IsWritableSourceFile(WritablePath));
        Assert.False(ComponentIconGlyphTagger.IsWritableSourceFile(GeneratedPath));
        Assert.False(ComponentIconGlyphTagger.IsWritableSourceFile(Path.Combine(Directory.Path,"missing.cs")));
        Assert.False(ComponentIconGlyphTagger.IsWritableSourceFile(Path.Combine(Directory.Path,"Component.txt")));

        try {
            File.SetAttributes(ReadOnlyPath,File.GetAttributes(ReadOnlyPath)|FileAttributes.ReadOnly);
            Assert.False(ComponentIconGlyphTagger.IsWritableSourceFile(ReadOnlyPath));
        } finally {
            File.SetAttributes(ReadOnlyPath,FileAttributes.Normal);
        }
    }

    [Fact]
    public void SourceEditor_UpdatesTheExistingAttributeOnItsPartialDeclarationAndNormalizesThePath(){
        const string Source="""
            [System.Obsolete, SceneComponentIconAttribute("Old/icon.png")]
            public partial struct Component { public int First; }
            public partial struct Component { public int Second; }
            """;
        var Root=CSharpSyntaxTree.ParseText(Source).GetRoot();
        StructDeclarationSyntax Owner=Root.DescendantNodes().OfType<StructDeclarationSyntax>().First();

        ComponentIconSourceEditResult Result=ComponentIconSourceEditor.Apply(
            Root,Owner.SpanStart,"Assets\\Icons\\component.PNG");

        Assert.True(Result.Succeeded,Result.ErrorMessage);
        string Changed=Result.Root!.ToFullString();
        Assert.Contains("System.Obsolete",Changed);
        Assert.Contains("Assets/Icons/component.PNG",Changed);
        Assert.DoesNotContain("Old/icon.png",Changed);
        Assert.Equal(1,Changed.Split(new[]{"SceneComponentIcon"},StringSplitOptions.None).Length-1);
    }

    [Fact]
    public void SourceEditor_DefaultIconWithoutAnAttributeIsANoOp(){
        const string Source="// retain trivia\npublic struct Component { }\n";
        var Root=CSharpSyntaxTree.ParseText(Source).GetRoot();
        int Position=Root.DescendantNodes().OfType<StructDeclarationSyntax>().Single().SpanStart;

        ComponentIconSourceEditResult Result=ComponentIconSourceEditor.Apply(Root,Position,SelectedProjectRelativePath:null);

        Assert.True(Result.Succeeded,Result.ErrorMessage);
        Assert.Same(Root,Result.Root);
        Assert.Equal(Source,Result.Root!.ToFullString());
    }

    private static INamedTypeSymbol GetType(CSharpCompilation Compilation,string MetadataName){
        INamedTypeSymbol?Type=Compilation.GetTypeByMetadataName(MetadataName);
        return Assert.IsAssignableFrom<INamedTypeSymbol>(Type);
    }

    private static void AssertDefaultIcon(INamedTypeSymbol Type,string ProjectDirectory){
        SceneVisualIcon Icon=SceneComponentIconResolver.Resolve(Type,ProjectDirectory);
        Assert.False(Icon.IsProjectAsset);
        Assert.Null(Icon.ProjectRelativePath);
        Assert.Null(Icon.AbsolutePath);
    }
    private static void Write(string Root,string RelativePath){
        string Path=System.IO.Path.Combine(Root,RelativePath.Replace('/',System.IO.Path.DirectorySeparatorChar));
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path,"asset");
    }

    private sealed class TemporaryDirectory : IDisposable {
        internal TemporaryDirectory(){
            this.Path=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"CSharpGameEngine.ComponentIconTests",Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(this.Path);
        }

        internal string Path { get; }

        public void Dispose(){
            if(System.IO.Directory.Exists(this.Path)){
                System.IO.Directory.Delete(this.Path,recursive:true);
            }
        }
    }
}
