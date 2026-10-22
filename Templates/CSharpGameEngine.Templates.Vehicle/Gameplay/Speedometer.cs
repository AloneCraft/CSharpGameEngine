using CSharpGameEngine.Entities;
using CSharpGameEngine.Vehicles;
using CSharpGameEngine.Runtime.Windows;
namespace CgeVehicle;
/// <summary>UI_Vehicleの速度の数字(Km/h)に付けるマーカー。同じEntityのScreenTextへ、親の車の速さを書く。</summary>
public struct SpeedReadout {
}

/// <summary>UI_Vehicleのギアの数字に付けるマーカー。同じEntityのScreenTextへ、親の車のギアを書く。</summary>
public struct GearReadout {
}

/// <summary>
/// UI_Vehicle(右下の速度とギアのHUD)の速度の数字に相当する。車(親)の<c>VehicleState</c>を<c>in</c>で読み、
/// 同じEntityの<c>ScreenText</c>へ、3桁ゼロ詰め(000〜999)の km/h を書く。
/// </summary>
[SystemPhase(SystemPhase.Presentation)]
[RunsBefore(typeof(ScreenTextCollect))]
internal struct SpeedometerSpeed:ISystem {
    private static readonly string[] Digits=BuildDigits();

    public void Update(in SpeedReadout Marker,ref ScreenText Text,in Parent<VehicleState> Car){
        var KilometersPerHour=Car.Value.ForwardSpeed.TryToSingle(out var Speed)?MathF.Abs(Speed)*3.6f:0f;
        var Rounded=(int)MathF.Round(KilometersPerHour,MidpointRounding.ToEven);
        Text.Text=Digits[Rounded%Digits.Length];
    }

    private static string[] BuildDigits(){
        var Table=new string[1000];
        for(var Index=0;Index<Table.Length;Index++){ Table[Index]=Index.ToString("D3"); }
        return Table;
    }
}

/// <summary>UI_Vehicleのギアの数字に相当する。後退はR、ニュートラルはN、前進は段の数字を書く。</summary>
[SystemPhase(SystemPhase.Presentation)]
[RunsBefore(typeof(ScreenTextCollect))]
internal struct SpeedometerGear:ISystem {
    private static readonly string[] Forward=["1","2","3","4","5","6","7","8","9"];

    public void Update(in GearReadout Marker,ref ScreenText Text,in Parent<VehicleState> Car){
        var Gear=Car.Value.Gear;
        Text.Text=Gear switch {
            < 0=>"R",
            0=>"N",
            _=>Gear<=Forward.Length?Forward[Gear-1]:Gear.ToString(),
        };
    }
}
