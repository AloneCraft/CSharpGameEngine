using System.Linq;
using CSharpGameEngine.SceneAnalysis.Tests;
using Microsoft.CodeAnalysis.CSharp;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class SceneComponentCatalogTests {
    [Fact]
    public void Build_ListsSourceStructComponentsAndExcludesReservedOrUnavailableTypes(){
        CSharpCompilation Compilation=SceneAnalysisTestHelper.CreateCompilation(
            SceneAnalysisTestHelper.CommonComponents,
            """
            namespace Sample;
            public struct UserComponent { public float Value; }
            public struct GenericComponent<T> { public T Value; }
            public ref struct RefComponent { public int Value; }
            public struct RequiredComponent { public required int Value { get; init; } }
            public class ReferenceType { }
            """);

        var Entries=SceneComponentCatalog.Build(Compilation);
        string[]Names=Entries.Select(static Entry=>Entry.FullyQualifiedName).ToArray();

        Assert.Contains("global::Sample.UserComponent",Names);
        Assert.DoesNotContain("global::Sample.GenericComponent<T>",Names);
        Assert.DoesNotContain("global::Sample.RefComponent",Names);
        Assert.DoesNotContain("global::Sample.RequiredComponent",Names);
        Assert.DoesNotContain("global::CSharpGameEngine.Entities.Parent",Names);
        Assert.DoesNotContain("global::CSharpGameEngine.Entities.SceneNodeTag",Names);
        Assert.DoesNotContain("global::Sample.ReferenceType",Names);
    }
}
