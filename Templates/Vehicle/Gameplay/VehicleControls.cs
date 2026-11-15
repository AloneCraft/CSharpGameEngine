using CSharpGameEngine.Entities;
using CSharpGameEngine.Vehicles;
namespace CgeVehicle;
/// <summary>
/// BP_VehicleAdvPawnBaseの操作に相当する(IA_Throttle、IA_Brake、IA_Steering、IA_Handbrake、IA_Reset)。操作を車の望み(<c>VehicleIntent</c>)へ写すだけで、
/// エンジン・変速・ブレーキ・サスペンション・タイヤの計算は、エンジンのVehiclesプラグインが車の設定(<see cref="UseSportsCarSetup"/>)の値で行う。
/// </summary>
internal struct VehicleControls:ISystem {
    public void Update(in PlayerActions Input,ref VehicleIntent Intent){
        Intent.Throttle=Input.Throttle;
        Intent.Brake=Input.Brake;
        Intent.Steering=Input.Steering;
        Intent.Handbrake=Input.Handbrake.Held;
        if(Input.Reset.Pressed){
            Intent.Reset();
        }
    }
}
/// <summary>
/// BP_VehicleAdvSportsCarのVehicleMovementCompの値(質量、エンジン、変速、操舵、前輪と後輪)を、データアセット
/// <c>Assets/Vehicles/SportsCar/SportsCarSetup.cgeasset</c>から車へ渡す。シーンからデータアセットを参照する仕組みはまだ無いので、コードで入れる。
/// </summary>
internal struct UseSportsCarSetup:ISystem {
    public void Update(ref Vehicle Car){
        Car.Setup??=Assets.Vehicles.SportsCar.SportsCarSetup;
    }
}
