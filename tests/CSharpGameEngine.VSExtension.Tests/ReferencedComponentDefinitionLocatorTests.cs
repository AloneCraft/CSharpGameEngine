using System;
using System.IO;
using System.Threading;
using CSharpGameEngine.SceneAnalysis.Tests;
using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class ReferencedComponentDefinitionLocatorTests {
    [Fact]
    public void TryFind_ResolvesReferencedProjectSourceAndSelectsTheRequestedDeclaration(){
        string ProjectDirectory=Path.Combine(Path.GetTempPath(),"CSharpGameEngine.DefinitionNavigation",Guid.NewGuid().ToString("N"));
        string SourceDirectory=Path.Combine(ProjectDirectory,"Runtime");
        string AssemblyDirectory=Path.Combine(ProjectDirectory,"bin","Debug","net10.0");
        Directory.CreateDirectory(SourceDirectory);
        Directory.CreateDirectory(AssemblyDirectory);
        try {
            string ProjectFile=Path.Combine(ProjectDirectory,"SceneModelTestAssembly.csproj");
            string SourceFile=Path.Combine(SourceDirectory,"Transform.cs");
            string AssemblyFile=Path.Combine(AssemblyDirectory,"SceneModelTestAssembly.dll");
            const string Source="""
                namespace CSharpGameEngine.Runtime.Windows;
                public struct Transform {
                    public float PositionX;
                    public float PositionZ;
                }
                """;
            File.WriteAllText(ProjectFile,"<Project />");
            File.WriteAllText(SourceFile,Source);
            var ReferencedCompilation=SceneAnalysisTestHelper.CreateCompilation(Source);
            using(FileStream Output=File.Create(AssemblyFile)){
                var Emit=ReferencedCompilation.Emit(Output);
                Assert.True(Emit.Success,string.Join(Environment.NewLine,Emit.Diagnostics));
            }

            var Consumer=SceneAnalysisTestHelper.CreateCompilation("namespace Consumer { public struct Marker { } }")
                .WithAssemblyName("Consumer")
                .AddReferences(MetadataReference.CreateFromFile(AssemblyFile));
            INamedTypeSymbol?Transform=ComponentTypeResolver.Resolve(
                Consumer,"global::CSharpGameEngine.Runtime.Windows.Transform");

            Assert.NotNull(Transform);
            Assert.True(ReferencedComponentDefinitionLocator.TryFind(
                Consumer,Transform!,MemberName:null,CancellationToken.None,out ReferencedComponentDefinitionLocation TypeLocation));
            Assert.Contains("public struct Transform",TypeLocation.Text.ToString(TypeLocation.Span));

            Assert.True(ReferencedComponentDefinitionLocator.TryFind(
                Consumer,Transform!,"PositionZ",CancellationToken.None,out ReferencedComponentDefinitionLocation FieldLocation));
            Assert.Equal("public float PositionZ;",FieldLocation.Text.ToString(FieldLocation.Span).Trim());
        } finally {
            if(Directory.Exists(ProjectDirectory)){
                Directory.Delete(ProjectDirectory,recursive:true);
            }
        }
    }
}