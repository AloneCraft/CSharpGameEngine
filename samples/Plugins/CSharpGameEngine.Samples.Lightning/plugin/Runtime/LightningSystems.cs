using CSharpGameEngine.Authoring;
using CSharpGameEngine.Entities;
namespace Lightning;
/// <summary>パッケージプラグインのサービス。ゲーム終了時に破棄される際、観測した内容を出力する（パッケージ検査ではその行を読み取る）。</summary>
[EngineService]
public sealed class LightningLog:IDisposable {
    public int Bolts;
    public float Energy;
    public void Dispose()=>Console.WriteLine(System.FormattableString.Invariant($"CGE-PLUGIN-SAMPLE Lightning bolts={this.Bolts} energy={this.Energy:F1}"));
}
/// <summary>各ティックで、すべての嵐のセルから雷撃を放つ。コンポーネントはLibrary DLLから、処理はDischargeから提供される。</summary>
internal struct Thunder:ISystem {
    public void Update(ref Bolt Bolt,in Intensity Intensity,LightningLog Log){
        Bolt.Count++;
        Log.Bolts++;
        Log.Energy+=Discharge.Energy(Intensity.Value);
    }
}
