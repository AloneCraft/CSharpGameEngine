using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Windows;
using CSharpGameEngine.Vehicles;
namespace CgeVehicle;
/// <summary>前と後ろのカメラに付けるマーカー。<see cref="CameraToggle"/>が切り替える。</summary>
public struct VehicleCameraSwitch {
}
/// <summary>
/// BP_VehicleAdvPawnBaseのIA_ToggleCamera: 後ろのカメラを止め、FlipFlopで前と後ろのカメラを交互に有効にする。マーカーの付いたカメラの有効を、押すたびに
/// 反転する(最初は後ろが有効、前は無効なので、前と後ろが入れ替わる)。
/// </summary>
internal struct CameraToggle:ISystem {
    public void Update(in PlayerActions Input,ref Camera Camera,in VehicleCameraSwitch Marker){
        if(Input.ToggleCamera.Pressed){
            Camera.Active=!Camera.Active;
        }
    }
}
/// <summary>
/// BP_VehicleAdvPawnBaseのIA_LookAround: 後ろの腕をYawに入力の値だけ回す。入力の値は視点の入力と同じく回転数(turns)なので、度にして足す。
/// 元の角度へ戻すのはエンジンの<c>VehicleCameraLook</c>(UEのInterpsToOriginalRotation)。
/// </summary>
internal struct LookAround:ISystem {
    public void Update(in PlayerActions Input,ref VehicleCameraLook Look){
        Look.YawDegrees+=Input.LookAround.X*360f;
    }
}
