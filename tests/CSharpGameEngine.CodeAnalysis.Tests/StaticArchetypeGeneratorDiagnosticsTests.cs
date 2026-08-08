using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis.Tests;

public sealed class StaticArchetypeGeneratorDiagnosticsTests
{
    private const string Component0 = "internal struct Component0{}";

    [Fact]
    public void NonStructSystem_ReportsSarch001()
    {
        string source = Component0 + """


            internal class NonStructSystem:ISystem{
                public bool Update(ref Component0 Component0, ref State State){ return true; }
            }
            """;

        AssertHasDiagnostic(source, "SARCH001");
    }

    [Fact]
    public void GenericSystem_ReportsSarch002()
    {
        string source = Component0 + """


            internal struct GenericSystem<T>:ISystem{
                public bool Update(ref Component0 Component0, ref State State){ return true; }
            }
            """;

        AssertHasDiagnostic(source, "SARCH002");
    }

    [Fact]
    public void NestedSystem_ReportsSarch003()
    {
        string source = Component0 + """


            internal static class Container{
                internal struct NestedSystem:ISystem{
                    public bool Update(ref Component0 Component0, ref State State){ return true; }
                }
            }
            """;

        AssertHasDiagnostic(source, "SARCH003");
    }

    [Fact]
    public void StaticUpdateMethod_ReportsSarch004()
    {
        string source = Component0 + """


            internal struct StaticUpdateSystem:ISystem{
                public static bool Update(ref Component0 Component0, ref State State){ return true; }
            }
            """;

        AssertHasDiagnostic(source, "SARCH004");
    }

    [Fact]
    public void PrivateUpdateMethod_ReportsSarch005()
    {
        string source = Component0 + """


            internal struct PrivateUpdateSystem:ISystem{
                private bool Update(ref Component0 Component0, ref State State){ return true; }
            }
            """;

        AssertHasDiagnostic(source, "SARCH005");
    }

    [Fact]
    public void NonBoolUpdateMethod_ReportsSarch006()
    {
        string source = Component0 + """


            internal struct VoidUpdateSystem:ISystem{
                public void Update(ref Component0 Component0, ref State State){ }
            }
            """;

        AssertHasDiagnostic(source, "SARCH006");
    }

    [Fact]
    public void GenericUpdateMethod_ReportsSarch007()
    {
        string source = Component0 + """


            internal struct GenericUpdateSystem:ISystem{
                public bool Update<T>(ref Component0 Component0, ref State State){ return true; }
            }
            """;

        AssertHasDiagnostic(source, "SARCH007");
    }

    [Fact]
    public void UpdateMethodWithoutComponentParameter_ReportsSarch008()
    {
        string source = Component0 + """


            internal struct NoComponentSystem:ISystem{
                public bool Update(ref State State){ return true; }
            }
            """;

        AssertHasDiagnostic(source, "SARCH008");
    }

    [Fact]
    public void ComponentParameterNotRef_ReportsSarch009()
    {
        string source = Component0 + """


            internal struct ByValueComponentSystem:ISystem{
                public bool Update(Component0 Component0, ref State State){ return true; }
            }
            """;

        AssertHasDiagnostic(source, "SARCH009");
    }

    [Fact]
    public void ComponentTypeNotStruct_ReportsSarch010()
    {
        string source = Component0 + """


            internal struct ReferenceTypeComponentSystem:ISystem{
                public bool Update(ref string Text, ref State State){ return true; }
            }
            """;

        AssertHasDiagnostic(source, "SARCH010");
    }

    [Fact]
    public void DuplicateComponentType_ReportsSarch011()
    {
        string source = Component0 + """


            internal struct DuplicateComponentSystem:ISystem{
                public bool Update(ref Component0 A, ref Component0 B, ref State State){ return true; }
            }
            """;

        AssertHasDiagnostic(source, "SARCH011");
    }

    [Fact]
    public void LeadingEntityParameterPassedByRef_ReportsSarch012()
    {
        string source = Component0 + """


            internal struct RefEntitySystem:ISystem{
                public bool Update(ref Entity Entity, ref Component0 Component0, ref State State){ return true; }
            }
            """;

        AssertHasDiagnostic(source, "SARCH012");
    }

    [Fact]
    public void UpdateMethodWithoutTrailingState_ReportsSarch013()
    {
        string source = Component0 + """


            internal struct NoStateSystem:ISystem{
                public bool Update(ref Component0 Component0){ return true; }
            }
            """;

        AssertHasDiagnostic(source, "SARCH013");
    }

    [Fact]
    public void StateUsedAsComponentParameter_ReportsSarch014()
    {
        string source = Component0 + """


            internal struct StateAsComponentSystem:ISystem{
                public bool Update(ref Component0 Component0, ref State ExtraState, ref State State){ return true; }
            }
            """;

        AssertHasDiagnostic(source, "SARCH014");
    }

    [Fact]
    public void SystemWithoutUpdateMethod_ReportsSarch015()
    {
        string source = """
            internal struct EmptySystem:ISystem{
            }
            """;

        AssertHasDiagnostic(source, "SARCH015");
    }

    private static void AssertHasDiagnostic(string source, string diagnosticId)
    {
        GeneratorDriverRunResult result = StaticArchetypeGeneratorTestHelper.RunGenerator(source);

        Assert.Contains(result.Diagnostics, d => d.Id == diagnosticId);
    }
}
