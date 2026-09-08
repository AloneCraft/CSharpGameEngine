using CSharpGameEngine.Entities;
using CSharpGameEngine.Lighting;
namespace CSharpGameEngine.Runtime.Lighting;
[SceneComponentOrder(SceneComponentCategory.LightingEnvironment,10)]
[SceneComponentIcon("Assets/Editor/ComponentIcons/DirectionalLight.svg")]
public struct DirectionalLight() {
    public LinearLightColor Color=new(1f,1f,1f);
    public Illuminance Illuminance=new(100_000f);
}
[SceneComponentOrder(SceneComponentCategory.LightingEnvironment,20)]
[SceneComponentIcon("Assets/Editor/ComponentIcons/PointLight.svg")]
public struct PointLight() {
    public LinearLightColor Color=new(1f,1f,1f);
    public LuminousFlux Flux=new(800f);
    public LightRange Range=new(10f);
}
[SceneComponentOrder(SceneComponentCategory.LightingEnvironment,30)]
[SceneComponentIcon("Assets/Editor/ComponentIcons/SpotLight.svg")]
public struct SpotLight() {
    public LinearLightColor Color=new(1f,1f,1f);
    public LuminousFlux Flux=new(800f);
    public LightRange Range=new(10f);
    public SpotConeAngles Cone=new(30f,45f);
}
[SceneComponentOrder(SceneComponentCategory.LightingEnvironment,40)]
[SceneComponentIcon("Assets/Editor/ComponentIcons/RectAreaLight.svg")]
public struct RectAreaLight() {
    public LinearLightColor Color=new(1f,1f,1f);
    public Luminance Luminance=new(100f);
    public RectangleSize Size=new(1f,1f);
    public LightRange Range=new(10f);
}
[SceneComponentOrder(SceneComponentCategory.LightingEnvironment,50)]
[SceneComponentIcon("Assets/Editor/ComponentIcons/LightShadow.svg")]
public struct LightShadow() {
    public int Priority=0;
    public ShadowBias Bias=new(0.005f);
}
