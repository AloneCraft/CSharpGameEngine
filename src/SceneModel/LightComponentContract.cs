using System;
namespace CSharpGameEngine.SceneAnalysis;
internal static class LightComponentContract {
    internal const string Prefix="global::CSharpGameEngine.Runtime.Lighting.";
    internal const string ShadowType=Prefix+"LightShadow";
    internal static readonly string[]Types={Prefix+"DirectionalLight",Prefix+"PointLight",Prefix+"SpotLight",Prefix+"RectAreaLight"};
    internal static bool IsLight(string Type)=>Array.IndexOf(Types,Type)>=0;
}
