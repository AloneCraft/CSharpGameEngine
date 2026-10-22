using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Character;
namespace CgeVehicle;
/// <summary>
/// BP_VehicleAdvPlayerControllerの操作に相当する。操作を車の望み(<c>VehicleIntent</c>)へ写すだけで、エンジン・ブレーキ・ハンドル・
/// 接地の計算は、エンジンの車両モデルが<c>Vehicle</c>のシーンの値で行う。
/// </summary>
internal struct VehicleControls:ISystem {
    public void Update(in PlayerActions Input,ref VehicleIntent Intent){
        Intent.Drive=Input.Drive.Y;
        Intent.Steering=Input.Drive.X;
        Intent.Handbrake=Input.Handbrake.Held;
    }
}
