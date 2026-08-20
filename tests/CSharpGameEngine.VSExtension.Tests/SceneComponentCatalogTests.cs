using System;
using System.IO;
using System.Linq;
using CSharpGameEngine.SceneAnalysis.Tests;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class SceneComponentCatalogTests {
    [Fact]
    public void Build_ListsSourceStructComponentsAndExcludesReservedOrUnavailableTypes(){
        var Compilation=SceneAnalysisTestHelper.CreateCompilation(
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
        var Names=Entries.Select(static Entry=>Entry.FullyQualifiedName).ToArray();

        Assert.Contains("global::Sample.UserComponent",Names);
        Assert.DoesNotContain("global::Sample.GenericComponent<T>",Names);
        Assert.DoesNotContain("global::Sample.RefComponent",Names);
        Assert.DoesNotContain("global::Sample.RequiredComponent",Names);
        Assert.DoesNotContain("global::CSharpGameEngine.Entities.Parent",Names);
        Assert.DoesNotContain("global::CSharpGameEngine.Entities.SceneNodeTag",Names);
        Assert.DoesNotContain("global::Sample.ReferenceType",Names);
    }

    /// <summary>
    /// M0053: <c>IRuntimeComponent</c>はエンジンが実行時に埋めるComponentの印。作者が宣言するものでは
    /// なく、随伴先を宣言すればSGが自動付与する(SCENE119)。パレットへ出すと「追加しても中身が
    /// 永久に空」になるので除外する。generic版・非generic版の両方が対象。
    /// </summary>
    [Fact]
    public void Build_ExcludesRuntimeComponentsSoTheyCannotBeAddedByHand(){
        var Compilation=SceneAnalysisTestHelper.CreateCompilation(
                                                                  SceneAnalysisTestHelper.CommonComponents,
                                                                  """
                                                                  namespace Sample;
                                                                  public struct Light { public float Intensity; }
                                                                  public struct LightRuntime:CSharpGameEngine.Entities.IRuntimeComponent<Light> { public int Handle; }
                                                                  public struct Standalone:CSharpGameEngine.Entities.IRuntimeComponent { public int Handle; }
                                                                  """);

        var Names=SceneComponentCatalog.Build(Compilation).Select(static Entry=>Entry.FullyQualifiedName).ToArray();

        Assert.Contains("global::Sample.Light",Names);
        Assert.DoesNotContain("global::Sample.LightRuntime",Names);
        Assert.DoesNotContain("global::Sample.Standalone",Names);
    }

    /// <summary>
    /// 実機報告: <c>TransformSync:ISystem</c>のような[GameSystem]構造体が、構造的には
    /// 非nullable structという形だけでComponentパレットに紛れ込んでいた。ECS Systemはper-Entityの
    /// 状態を持たずフィールドも無いため、追加しても展開する中身が永遠に「解析待ち…」のままになる
    /// (Componentとして無意味)。
    /// </summary>
    [Fact]
    public void Build_ExcludesTypesThatImplementISystemEvenThoughTheyAreNonNullableStructs(){
        var Compilation=SceneAnalysisTestHelper.CreateCompilation(
                                                                  SceneAnalysisTestHelper.CommonComponents,
                                                                  """
                                                                  namespace Sample;
                                                                  public struct RealComponent { public float Value; }
                                                                  internal struct FakeSystem:CSharpGameEngine.Entities.ISystem {
                                                                      public bool Update(){ return true; }
                                                                  }
                                                                  """);

        var Names=SceneComponentCatalog.Build(Compilation).Select(static Entry=>Entry.FullyQualifiedName).ToArray();

        Assert.Contains("global::Sample.RealComponent",Names);
        Assert.DoesNotContain("global::Sample.FakeSystem",Names);
    }
    [Fact]
    public void Build_DoesNotListReferencedMetadataStructsAsGlyphCandidates(){
        var MetadataCompilation=SceneAnalysisTestHelper.CreateCompilation(
                                                                          """
                                                                          namespace Referenced {
                                                                              public struct MetadataComponent { public int Value; }
                                                                          }
                                                                          """);
        using MemoryStream AssemblyImage=new();
        var EmitResult=MetadataCompilation.Emit(AssemblyImage);
        Assert.True(EmitResult.Success,string.Join(Environment.NewLine,EmitResult.Diagnostics));

        var Consumer=SceneAnalysisTestHelper.CreateCompilation(
                                                               """
                                                               namespace Local {
                                                                   public struct SourceComponent { public int Value; }
                                                               }
                                                               """)
                                            .AddReferences(MetadataReference.CreateFromImage(AssemblyImage.ToArray()));

        var Names=SceneComponentCatalog.Build(Consumer)
                                      .Select(static Entry=>Entry.FullyQualifiedName)
                                      .ToArray();

        Assert.Contains("global::Local.SourceComponent",Names);
        Assert.DoesNotContain("global::Referenced.MetadataComponent",Names);
    }

    // ---- generic Component型(型引数を制約から機械的に埋められる場合だけ許可する) ----

    /// <summary>
    /// 本命。<c>where T : System.Enum</c>は「全enum型」という具体的な候補集合を持つので、
    /// Compilation内のenumそれぞれについて閉じたinstantiationを1件ずつ生成する。
    /// </summary>
    [Fact]
    public void Build_ExpandsEnumConstrainedGenericIntoOneEntryPerEnumType(){
        var Compilation=SceneAnalysisTestHelper.CreateCompilation(
                                                                  SceneAnalysisTestHelper.CommonComponents,
                                                                  """
                                                                  namespace Sample;
                                                                  public struct StatusEffect<TKind> where TKind : System.Enum {
                                                                      public TKind Kind;
                                                                      public float Duration;
                                                                  }
                                                                  public enum PoisonKind { Weak, Strong }
                                                                  public enum BurnKind { Slow, Fast }
                                                                  """);

        var Entries=SceneComponentCatalog.Build(Compilation);
        var Names=Entries.Select(static Entry=>Entry.FullyQualifiedName).ToArray();
        var DisplayNames=Entries.Select(static Entry=>Entry.DisplayName).ToArray();

        Assert.Contains("global::Sample.StatusEffect<global::Sample.PoisonKind>",Names);
        Assert.Contains("global::Sample.StatusEffect<global::Sample.BurnKind>",Names);
        Assert.Contains("StatusEffect<PoisonKind>",DisplayNames);
        Assert.Contains("StatusEffect<BurnKind>",DisplayNames);
        // CommonComponentsが宣言しているPrimitiveTypeもenumなので、StatusEffect<PrimitiveType>も
        // 候補に出る。件数は数えず「意図した2件が両方出ていること」だけを見る。
    }

    /// <summary>インターフェース制約も同じ仕組みで解決できる(実装している具体型だけを候補にする)。</summary>
    [Fact]
    public void Build_ExpandsInterfaceConstrainedGenericIntoImplementingTypesOnly(){
        var Compilation=SceneAnalysisTestHelper.CreateCompilation(
                                                                  SceneAnalysisTestHelper.CommonComponents,
                                                                  """
                                                                  namespace Sample;
                                                                  public interface IEffectKind { }
                                                                  public struct StatusEffect<TKind> where TKind : IEffectKind {
                                                                      public TKind Kind;
                                                                  }
                                                                  public struct PoisonKind : IEffectKind { }
                                                                  public struct UnrelatedStruct { public int Value; }
                                                                  """);

        var Names=SceneComponentCatalog.Build(Compilation)
                                      .Select(static Entry=>Entry.FullyQualifiedName).ToArray();

        Assert.Contains("global::Sample.StatusEffect<global::Sample.PoisonKind>",Names);
        Assert.DoesNotContain("global::Sample.StatusEffect<global::Sample.UnrelatedStruct>",Names);
    }

    /// <summary>制約が無い(または struct/class/new() のような広すぎる制約)generic型は、以前と同じく除外する。</summary>
    [Fact]
    public void Build_StillExcludesUnconstrainedGenerics(){
        var Compilation=SceneAnalysisTestHelper.CreateCompilation(
                                                                  SceneAnalysisTestHelper.CommonComponents,
                                                                  """
                                                                  namespace Sample;
                                                                  public struct UnconstrainedGeneric<T> { public T Value; }
                                                                  public struct StructConstrainedGeneric<T> where T : struct { public T Value; }
                                                                  public enum SomeEnum { A, B }
                                                                  """);

        var Names=SceneComponentCatalog.Build(Compilation)
                                      .Select(static Entry=>Entry.FullyQualifiedName).ToArray();

        Assert.DoesNotContain(Names,Name=>Name.StartsWith("global::Sample.UnconstrainedGeneric<",StringComparison.Ordinal));
        Assert.DoesNotContain(Names,Name=>Name.StartsWith("global::Sample.StructConstrainedGeneric<",StringComparison.Ordinal));
    }

    /// <summary>
    /// 複数の型引数はcartesian積で展開する。
    /// </summary>
    [Fact]
    public void Build_ExpandsMultipleTypeParametersAsACartesianProduct(){
        var Compilation=SceneAnalysisTestHelper.CreateCompilation(
                                                                  SceneAnalysisTestHelper.CommonComponents,
                                                                  """
                                                                  namespace Sample;
                                                                  public struct Pair<TFirst,TSecond> where TFirst : System.Enum where TSecond : System.Enum {
                                                                      public TFirst First;
                                                                      public TSecond Second;
                                                                  }
                                                                  public enum Kind1 { A }
                                                                  public enum Kind2 { B }
                                                                  """);

        var Names=SceneComponentCatalog.Build(Compilation)
                                      .Select(static Entry=>Entry.FullyQualifiedName).ToArray();

        Assert.Contains("global::Sample.Pair<global::Sample.Kind1, global::Sample.Kind1>",Names);
        Assert.Contains("global::Sample.Pair<global::Sample.Kind1, global::Sample.Kind2>",Names);
        Assert.Contains("global::Sample.Pair<global::Sample.Kind2, global::Sample.Kind1>",Names);
        Assert.Contains("global::Sample.Pair<global::Sample.Kind2, global::Sample.Kind2>",Names);
    }

    /// <summary>requiredメンバーを持つgeneric Componentは、閉じたinstantiationであっても除外する。</summary>
    [Fact]
    public void Build_ExcludesGenericWithRequiredMembersEvenWhenConstraintIsResolvable(){
        var Compilation=SceneAnalysisTestHelper.CreateCompilation(
                                                                  SceneAnalysisTestHelper.CommonComponents,
                                                                  """
                                                                  namespace Sample;
                                                                  public struct StatusEffect<TKind> where TKind : System.Enum {
                                                                      public required TKind Kind { get; init; }
                                                                  }
                                                                  public enum PoisonKind { Weak }
                                                                  """);

        var Names=SceneComponentCatalog.Build(Compilation)
                                      .Select(static Entry=>Entry.FullyQualifiedName).ToArray();

        Assert.DoesNotContain(Names,Name=>Name.StartsWith("global::Sample.StatusEffect<",StringComparison.Ordinal));
    }

    /// <summary>候補が1件も無ければ(制約を満たす型が誰も存在しない)、その型は一切現れない。</summary>
    [Fact]
    public void Build_ProducesNoEntriesWhenNoCandidateSatisfiesTheConstraint(){
        var Compilation=SceneAnalysisTestHelper.CreateCompilation(
                                                                  SceneAnalysisTestHelper.CommonComponents,
                                                                  """
                                                                  namespace Sample;
                                                                  public interface IUnimplementedMarker { }
                                                                  public struct StatusEffect<TKind> where TKind : IUnimplementedMarker {
                                                                      public TKind Kind;
                                                                  }
                                                                  """);

        var Names=SceneComponentCatalog.Build(Compilation)
                                      .Select(static Entry=>Entry.FullyQualifiedName).ToArray();

        Assert.DoesNotContain(Names,Name=>Name.StartsWith("global::Sample.StatusEffect<",StringComparison.Ordinal));
    }
}
