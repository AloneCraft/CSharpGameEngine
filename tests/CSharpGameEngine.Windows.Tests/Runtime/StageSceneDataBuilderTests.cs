using CSharpGameEngine.Entities;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Graphics.D3D12.Tests;

namespace CSharpGameEngine.Runtime.Windows.Tests;

public sealed class StageSceneDataBuilderTests {
    [WindowsOnlyFact]
    public void TexturedGltf_IsConvertedToCanonicalMaterialResourcesBeforeL0(){
        StageSceneDataBuilder Scene=StageSceneDataBuilder.Build(
            [(new Entity(0,1),new Transform(),new StaticMesh {
                AssetPath=Path.Combine("Fixtures","BoxTextured.glb"),
                Mobility=MeshMobility.Static,
            })],
            []);

        StageMaterialCreateInfo Material=Assert.Single(Scene.Materials);
        Assert.NotNull(Material.BaseColorTexture);
        Assert.NotNull(Material.NormalTexture);
        Assert.NotNull(Material.MetallicRoughnessTexture);
        Assert.NotNull(Material.EmissiveTexture);
        Assert.True(Material.BaseColorTexture.Width>1);
        Assert.True(Material.BaseColorTexture.Height>1);
        Assert.Equal(
            Material.BaseColorTexture.Width*Material.BaseColorTexture.Height*4,
            Material.BaseColorTexture.Pixels.Length);
    }

    [Fact]
    public void L0MaterialInputExposesNoEncodedTextureByteArrays(){
        var Properties=typeof(StageMaterialCreateInfo).GetProperties();

        Assert.DoesNotContain(Properties,Property=>Property.Name.EndsWith(
            "TextureBytes",StringComparison.Ordinal));
        Assert.DoesNotContain(Properties,Property=>Property.PropertyType==typeof(byte[]));
        Assert.DoesNotContain(
            typeof(RenderServer).GetMethods()
                .Where(Method=>Method.Name==nameof(RenderServer.Create))
                .SelectMany(Method=>Method.GetParameters()),
            Parameter=>Parameter.ParameterType==typeof(byte[]));
    }

    [Fact]
    public void EncodedTextureAdapterRejectsCorruptionInsteadOfFallingBack(){
        Assert.Null(RenderTextureArtifactAdapter.DecodeOptional([]));
        Assert.Throws<InvalidDataException>(()=>
            RenderTextureArtifactAdapter.DecodeOptional([1,2,3,4]));
    }
}
