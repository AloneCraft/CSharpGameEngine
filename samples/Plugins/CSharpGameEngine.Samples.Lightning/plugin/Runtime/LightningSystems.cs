using CSharpGameEngine.Authoring;
using CSharpGameEngine.Entities;
namespace Lightning;
/// <summary>The package plugin's service; it prints what it saw when the game disposes it (the package check reads the line).</summary>
[EngineService]
public sealed class LightningLog:IDisposable {
    public int Bolts;
    public float Energy;
    public void Dispose()=>Console.WriteLine(System.FormattableString.Invariant($"CGE-PLUGIN-SAMPLE Lightning bolts={this.Bolts} energy={this.Energy:F1}"));
}
/// <summary>Throws a bolt from every storm cell each tick. The components come from the Library DLL, the work from Discharge.</summary>
internal struct Thunder:ISystem {
    public void Update(ref Bolt Bolt,in Intensity Intensity,LightningLog Log){
        Bolt.Count++;
        Log.Bolts++;
        Log.Energy+=Discharge.Energy(Intensity.Value);
    }
}
