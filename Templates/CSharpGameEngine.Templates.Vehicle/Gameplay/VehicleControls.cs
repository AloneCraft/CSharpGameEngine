using CSharpGameEngine.Entities;
using CSharpGameEngine.Vehicles;
namespace CgeVehicle;
/// <summary>
/// BP_VehicleAdvPlayerControllerの操作に相当する。操作を車の望み(<c>VehicleIntent</c>)へ写すだけで、エンジン・ブレーキ・ハンドル・
/// 接地の計算は、エンジンの車両モデルが<c>Vehicle</c>のシーンの値で行う。アクセルとブレーキは別々のアナログ値で、車の望みの
/// <c>Drive</c>(正は前進、負は後退・ブレーキ)にはその差を渡す。
/// </summary>
internal struct VehicleControls:ISystem {
    public void Update(in PlayerActions Input,ref VehicleIntent Intent){
        Intent.Drive=Input.Throttle-Input.Brake;
        Intent.Steering=Input.Steering;
        Intent.Handbrake=Input.Handbrake.Held;
    }
}
