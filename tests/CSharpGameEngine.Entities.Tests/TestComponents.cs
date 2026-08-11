namespace CSharpGameEngine.Entities.Tests;

public struct Position {
    public float X;
    public float Y;
    public float Z;
}

public struct Velocity {
    public float X;
    public float Y;
    public float Z;
}

public struct Health {
    public float Value;
}

public struct DisabledTag {
}

/// <summary>
/// Archetypeは<c>Update</c>/<c>Spawn</c>の引数からのみ導出されるため(ADR-0033)、テストが使う構成は
/// ここで宣言しておく必要がある。<c>Update</c>を持つものはメインループのディスパッチ対象にもなるが、
/// このテストプロジェクトは<c>GeneratedGameLoop</c>を呼ばないので副作用は無い。
/// </summary>
internal struct TestSystem:ISystem {
    /// <summary>移動元Archetype: <c>{Position}</c>。</summary>
    public bool Update(Entity entity,ref Position position,ref State state){
        return true;
    }

    /// <summary>移動先Archetype: <c>{Position, Velocity}</c>。</summary>
    public bool Update(Entity entity,ref Position position,ref Velocity velocity,ref State state){
        return true;
    }
}

/// <summary>Scene Authoringが使う構成(親子リンクと空ノード)を宣言する。</summary>
internal struct SceneShapeSpawn:ISystem {
    public void Spawn(ref Position position,ref Parent parent){
    }

    public void Spawn(ref SceneNodeTag sceneNodeTag){
    }
}

/// <summary>単独の<c>{Health}</c>と、タグを含む構成。</summary>
internal struct HealthSpawn:ISystem {
    public void Spawn(ref Health health){
    }

    public void Spawn(ref Health health,ref DisabledTag disabledTag){
    }
}
