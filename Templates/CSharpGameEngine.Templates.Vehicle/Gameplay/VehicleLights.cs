using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Lighting;
using CSharpGameEngine.Runtime.Windows;
namespace CgeVehicle;
/// <summary>
/// 車体のブレーキランプ(BP_VehicleAdvSportsCarのBrakeLights)。同じEntityの<c>MeshEmissive</c>へ、ブレーキ中は<see cref="BrakingPower"/>、通常は
/// <see cref="IdlePower"/>のBrakeLightPowerを書く。車体のglbの発光(UEのT_SportsCar_Lights_Mの赤の範囲=テールランプ)は、BrakeLightPower 1に当たる100 nitsで作ってある
/// (UEの発光の値1を100 nitsとして写す。UEの値は露出に依る相対値なので推測で、昼の光の中でもブレーキで明るく光る値を選んだ)。
/// </summary>
public struct BrakeLights() {
    /// <summary>ブレーキ中のBrakeLightPower(UEのSelectFloat A=200)。</summary>
    public float BrakingPower=200f;
    /// <summary>通常のBrakeLightPower(UEのSelectFloat B=4)。</summary>
    public float IdlePower=4f;
    /// <summary>UEのBrakeLights(Is Braking)が最後に受けた値。</summary>
    public bool Braking;
    /// <summary>前のフレームのブレーキの値(IA_BrakeのStartedとCompletedを見分ける)。</summary>
    public float PreviousBrake;
}
/// <summary>
/// UEのBrakeLightsを呼ぶ所に相当する: IA_BrakeのStarted(0からの踏み込み)で点け、Completed(0に戻った)で消す。IA_HandbrakeはTriggered(押している間)で点け、
/// Completed(離した)で消す。最後の呼び出しが勝つので、ブレーキを踏んだままハンドブレーキを離すと消える(UEと同じ)。
/// </summary>
internal struct BrakeLightControl:ISystem {
    public void Update(in PlayerActions Input,ref BrakeLights Lights,ref MeshEmissive Emissive){
        var Brake=(float)Input.Brake;
        if(Brake>0f&&Lights.PreviousBrake<=0f){ Lights.Braking=true; }
        if(Brake<=0f&&Lights.PreviousBrake>0f){ Lights.Braking=false; }
        Lights.PreviousBrake=Brake;
        if(Input.Handbrake.Held){ Lights.Braking=true; }
        if(Input.Handbrake.Released){ Lights.Braking=false; }
        Emissive.Scale=Lights.Braking?Lights.BrakingPower:Lights.IdlePower;
    }
}
/// <summary>IA_Headlightsが切り替えるヘッドライトに付けるマーカー(UEのHeadlight_L。Headlight_Rは車体に直に付いていて、UEでも切り替わらない)。</summary>
public struct HeadlightSwitch {
}
/// <summary>BP_VehicleAdvSportsCarのIA_Headlights: FlipFlopでHeadlight_Lの表示(<c>LightVisibility</c>)を切り替える(最初は非表示なので、1回目で点く)。</summary>
internal struct HeadlightToggle:ISystem {
    public void Update(in PlayerActions Input,ref LightVisibility Light,in HeadlightSwitch Marker){
        if(Input.Headlights.Pressed){
            Light.Visible=!Light.Visible;
        }
    }
}
