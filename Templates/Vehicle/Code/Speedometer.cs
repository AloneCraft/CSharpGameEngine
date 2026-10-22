using CSharpGameEngine.Entities;
namespace Vehicle;
/// <summary>HUD(子のScreenText)に速度とギアを出す。表示は、車(親)の状態を読むだけ。</summary>
internal struct Speedometer:ISystem {
    public void Update(ref ScreenText Hud,in Parent<VehicleState> Car){
        Hud.Text=$"{Car.Value.Gear}  {Car.Value.Speed.KilometersPerHour:0} km/h";
    }
}
