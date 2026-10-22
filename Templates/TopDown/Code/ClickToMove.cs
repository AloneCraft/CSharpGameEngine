using CSharpGameEngine.Entities;
namespace TopDown;
/// <summary>押されている間、カーソル下の地面を目的地にする。押した瞬間だけ目印を出す。</summary>
internal struct ClickToMove:ISystem {
    public void Update(in PlayerActions Input,ref MoveIntent Intent,Physics Physics,Spawner Spawner){
        if(!Input.SetDestination.Held||!Physics.Raycast(Input.Cursor.Ray,out var Hit)){
            return;
        }
        Intent.MoveTo(Hit.Point);
        if(Input.SetDestination.Pressed){
            Spawner.Spawn(Scenes.CursorMarker,Hit.Point);
        }
    }
}
