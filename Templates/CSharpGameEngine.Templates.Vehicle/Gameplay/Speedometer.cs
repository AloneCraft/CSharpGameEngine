using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Character;
using CSharpGameEngine.Runtime.Windows;
namespace CgeVehicle;
/// <summary>
/// UI_Vehicle(速度とギアのHUD)に相当する。画面上の文字の描画はまだ無いので、ウィンドウのタイトルバーに出す。
/// 車の状態(<c>VehicleState</c>)を<c>in</c>で読み、<c>Hud</c>サービスへ文字を渡すだけ。
/// </summary>
internal struct Speedometer:ISystem {
    public void Update(in VehicleState Car,Hud Hud){
        var KilometersPerHour=Car.ForwardSpeed.TryToSingle(out var Speed)?Math.Abs(Speed)*3.6f:0f;
        var Gear=Car.Gear switch {
            -1=>"R",
            0=>"N",
            _=>Car.Gear.ToString(),
        };
        Hud.SetTitle($"Vehicle   {KilometersPerHour:F0} km/h   gear {Gear}");
    }
}
