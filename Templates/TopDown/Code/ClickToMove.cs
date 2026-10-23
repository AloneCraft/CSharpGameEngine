using CSharpGameEngine.Entities;
namespace TopDown;
/// <summary>
/// UEのクリック移動。押している間はカーソルの下の地面へ直接歩き、押していた時間が0.5秒未満で離したら(短押し)、その地面へナビメッシュの経路で歩いて目印を出す。
/// 長押しで離したら止まるだけ。
/// </summary>
internal struct ClickToMove:ISystem {
    public void Update(in PlayerActions Input,ref MoveIntent Intent,ref ClickDestination Cached,in Transform Body,Picking Picking,Spawner Spawner){
        var Click=Input.SetDestination;
        var Found=Picking.TryPick(Input.Cursor,out var Hit);
        if(Click.Pressed){
            Intent.Stop();
            if(Found){ Cached.Point=Hit.Point; Cached.Found=true; }
        }
        if(Click.Held&&Found){
            Cached.Point=Hit.Point;
            if((Cached.Point-Body.Position).TryNormalize(out var Direction)){ Intent.Direction=Direction; }
        }
        if(Click.Released){
            if(Click.ReleasedBefore(0.5f)&&Cached.Found){
                Intent.MoveTo(Cached.Point);
                Spawner.Spawn<CursorMarker>(Cached.Point);
            } else {
                Intent.Stop();
            }
        }
    }
}
