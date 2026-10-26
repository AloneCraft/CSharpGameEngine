using CSharpGameEngine.Entities;
using CSharpGameEngine.Numerics;
namespace CSharpGameEngine.Runtime.Windows;
/// <summary>
/// 短時間で自身を終了するEntity（ADR-0078/M0105）。残り時間を固定ティックごとに1ティック分減らし、0になるとEntityを破棄する。
/// Sceneのルートに置いた場合は子も含めてScene全体を終了させるため、生成したマーカーやバーストの後片付けに使える。
/// それ以外のEntityに置いた場合は、そのEntityだけを終了する。固定ティック数で管理するため、シミュレーションのほかの処理と同様に決定的。
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Gameplay,20)]
public struct Lifetime {
    /// <summary>残り時間（秒）。0以下なら次の固定ティックでEntityを破棄する。</summary>
    public LogicalScalar Seconds;
}
/// <summary>ADR-0078/M0105：すべての<see cref="Lifetime"/>を減算し、時間切れになったEntityを破棄する。</summary>
[SystemPhase(SystemPhase.FixedSimulation)]
internal struct LifetimeExpire:ISystem {
    public void Update(Entity Entity,ref Lifetime Lifetime,ref State State){
        var Step=LogicalScalar.One/LogicalScalar.FromInt64(State.FixedTickRate);
        if(LogicalScalar.TrySubtract(Lifetime.Seconds,Step,out var Left)&&Left>LogicalScalar.Zero){
            Lifetime.Seconds=Left;
            return;
        }
        Lifetime.Seconds=LogicalScalar.Zero;
        var Scene=State.World.GetOwningScene(Entity);
        if(!Scene.IsNone&&State.World.GetSceneRoot(Scene)==Entity){
            State.World.UnloadSceneDeferred(Scene,ref State);
        } else {
            State.World.DestroyDeferred(Entity,ref State);
        }
    }
}
