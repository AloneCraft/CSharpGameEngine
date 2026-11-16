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
/// Archetypeの番号はSystemの順に振られ、テストは<c>Archetype0</c>={Position}、<c>Archetype1</c>={Position, Velocity}、
/// <c>Archetype2</c>={Position, Parent}を名指しする。順序の無いSystemは名前順になる(ADR-0073)ので、この順を辺で固定する。
/// </summary>
[RunsBefore(typeof(SceneShapeSpawn))]
internal struct TestSystem:ISystem {
    /// <summary>移動元Archetype: <c>{Position}</c>。</summary>
    public bool Update(Entity Entity,ref Position Position,ref State State){
        return true;
    }

    /// <summary>移動先Archetype: <c>{Position, Velocity}</c>。</summary>
    public bool Update(Entity Entity,ref Position Position,ref Velocity Velocity,ref State State){
        return true;
    }
}

/// <summary>Scene Authoringが使う構成(親子リンクと空ノード)を宣言する。</summary>
[RunsBefore(typeof(HealthSpawn))]
internal struct SceneShapeSpawn:ISystem {
    public void Spawn(ref Position Position,ref Parent Parent){
    }

    public void Spawn(ref SceneNodeTag SceneNodeTag){
    }
}

/// <summary>単独の<c>{Health}</c>と、タグを含む構成。</summary>
internal struct HealthSpawn:ISystem {
    public void Spawn(ref Health Health){
    }

    public void Spawn(ref Health Health,ref DisabledTag DisabledTag){
    }
}
