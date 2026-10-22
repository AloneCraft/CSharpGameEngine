using CSharpGameEngine.Entities;
namespace FirstPerson;
/// <summary>腕のメッシュ(子)を、親の視点の上下角に合わせて傾ける。左右の向きは親(体)が既に向いている。</summary>
internal struct ArmsFollowView:ISystem {
    public void Update(ref Transform Arms,in Parent<ViewAngles> Body){
        Arms.Rotation=Rotation.Pitch(Body.Value.Pitch);
    }
}
