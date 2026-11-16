using CSharpGameEngine.Authoring;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Numerics;
using EcsWorld=CSharpGameEngine.Entities.World;
namespace CSharpGameEngine.Runtime.Windows;
/// <summary>
/// ゲーム実行中に Scene を生成する（ADR-0078/M0105）。System は <c>Spawner</c> を引数に取り、
/// <c>Spawner.Spawn&lt;CursorMarker&gt;(Hit.Point)</c> のように呼び出す。<c>CursorMarker</c> はゲームのシーン（
/// <c>Scenes/CursorMarker.json</c>）であり、名前に指定できるのはルートに <see cref="Transform"/> を持つ Scene だけなので、呼び出しはビルド時
/// に検証する（<see cref="ISpawnableScene"/>）。<para>呼び出しを行ったフレームの末尾にシーンを出現させる。リクエストは
/// <see cref="SpawnerFlush"/> が構造変更コマンドキューを通して処理する。System から Entity を作成する安全な方法はこのキューだけである。Scene の描画は次の
/// フレームから始める。ルートの位置は指定された値に置き換える。回転を指定しない場合は Scene の回転とスケールを維持し、指定した場合は回転も置き換える。子はルートに対する相対位置を保つ。同じ
/// Scene を再度生成すると独立したコピーになる。</para><para>物理ボディを持つ Scene を同時に複数生成してはならない。<c>PhysicsStableId</c> は Scene
/// の定義から取得するため、コピー同士で重複する。</para>
/// </summary>
[EngineService]
public sealed class Spawner {
    private delegate SceneInstance Place(ref EcsWorld World,LogicalVector3 Position,LogicalQuaternion Rotation,bool HasRotation);
    private readonly record struct Request(Place Place,LogicalVector3 Position,LogicalQuaternion Rotation,bool HasRotation);
    private static class Placer<TScene>where TScene:ISpawnableScene {
        internal static readonly Place Run=Instantiate;
        private static SceneInstance Instantiate(ref EcsWorld World,LogicalVector3 Position,LogicalQuaternion Rotation,bool HasRotation)=>
            HasRotation?TScene.InstantiateDeferredAt(ref World,Position,Rotation):TScene.InstantiateDeferredAt(ref World,Position);
    }
    private readonly List<Request>Pending=[];
    /// <summary>フレーム末尾の処理を待つ Scene の数。</summary>
    public int PendingCount=>this.Pending.Count;
    /// <summary>ルートを <paramref name="Position"/> に配置した <typeparamref name="TScene"/> のコピーを要求する。</summary>
    public void Spawn<TScene>(LogicalVector3 Position)where TScene:ISpawnableScene=>
        this.Pending.Add(new Request(Placer<TScene>.Run,Position,default,false));
    /// <summary>同様に、ルートを <paramref name="Rotation"/> に向けたコピーを要求する。</summary>
    public void Spawn<TScene>(LogicalVector3 Position,LogicalQuaternion Rotation)where TScene:ISpawnableScene=>
        this.Pending.Add(new Request(Placer<TScene>.Run,Position,Rotation,true));
    /// <summary>
    /// 要求されたすべての Scene を <paramref name="World"/> のコマンドバッファに追加し、要求を消去する。処理件数を返す。<paramref name="Created"/>
    /// を指定した場合は、各 Scene のハンドルを格納する（フレームの構造変更が再生された後に使用できる）。
    /// </summary>
    public int Flush(ref EcsWorld World,ICollection<SceneInstance>?Created=null){
        var Count=this.Pending.Count;
        try {
            foreach(var Request in this.Pending){
                var Instance=Request.Place(ref World,Request.Position,Request.Rotation,Request.HasRotation);
                Created?.Add(Instance);
            }
        } finally {
            this.Pending.Clear();
        }
        return Count;
    }
}
/// <summary>ゲームが <see cref="Spawner"/> に要求した Scene を、他のすべての処理の後、フレームごとに一度生成する（ADR-0078/M0105）。</summary>
[SystemPhase(SystemPhase.Presentation)]
[RunsAfter(typeof(Present))]
internal struct SpawnerFlush:ISystem {
    public void Update(ref LevelRuntime Runtime,Spawner Spawner,ref State State){
        Spawner.Flush(ref State.World);
    }
}
