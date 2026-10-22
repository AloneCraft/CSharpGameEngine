[assembly: CSharpGameEngine.Authoring.PluginLibrary]
namespace Lightning;
/// <summary>How bright a bolt is. The Weather sample plugin has a component of the same name; the generator names both apart.</summary>
public struct Intensity {
    public float Value;
}
/// <summary>A storm cell that throws bolts.</summary>
public struct Bolt {
    public int Count;
}
/// <summary>The Library module's work: plain code with no ECS types, so it can ship compiled.</summary>
public static class Discharge {
    /// <summary>Energy of one bolt (gigajoules) at an intensity.</summary>
    public static float Energy(float Intensity)=>Intensity*Intensity*5f;
}
