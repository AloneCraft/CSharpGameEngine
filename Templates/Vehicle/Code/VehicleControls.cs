using CSharpGameEngine.Entities;
namespace Vehicle;
/// <summary>操作を車へのお願い(VehicleIntent)へ写す。エンジン・変速・サスペンションは車の側(Vehicle)が解く。</summary>
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
/// <summary>カメラの切り替え。CameraRigは子のカメラを列挙しており、Nextで次の1台だけを有効にする。</summary>
internal struct CameraSwitch:ISystem {
    public void Update(in PlayerActions Input,ref CameraRig Rig){
        if(Input.ToggleCamera.Pressed){
            Rig.Next();
        }
    }
}
