using CSharpGameEngine.Entities;
namespace Vehicle;
/// <summary>
/// エンジン音は「イベント」ではなく「状態に追従する音」なので、AudioSourceの音程を回転数に結ぶ。
/// 音源の鳴らし始め・位置・ループはAudioSourceが担当する。
/// </summary>
internal struct EngineSound:ISystem {
    public void Update(ref AudioSource Sound,in Parent<VehicleState> Car){
        Sound.Pitch=0.5f+(Car.Value.Rpm/Car.Value.MaxRpm);
        Sound.Volume=0.3f+(0.7f*Car.Value.Throttle);
    }
}
