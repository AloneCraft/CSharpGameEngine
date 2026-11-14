namespace CSharpGameEngine.Entities;

public enum SimulationPlaybackState:byte {
    Running,
    Paused,
}

/// <summary>安定したフレームフェーズ。値は生成された実行メタデータに保存される。</summary>
public enum SystemPhase:byte {
    FrameInput,
    FixedSimulation,
    PhysicsSimulation,
    PostPhysics,
    Presentation,
}

/// <summary>ECSシステムを、決定的なフレームフェーズへ割り当てる。</summary>
[AttributeUsage(AttributeTargets.Struct,AllowMultiple=false,Inherited=false)]
public sealed class SystemPhaseAttribute(SystemPhase Phase):Attribute {
    public SystemPhase Phase { get; }=Phase;
}

/// <summary>廃止（ADR-0077、ADR-0098 §5 の B）。構造体の <c>in T</c> がそのまま読み取り専用のコンポーネントになる。使うとビルドエラーで書き換え方を示す。</summary>
[Obsolete("[ReadOnlyComponent] is retired: an 'in T' struct parameter already reads the component. Remove the attribute.",error:true)]
[AttributeUsage(AttributeTargets.Parameter,AllowMultiple=false,Inherited=false)]
public sealed class ReadOnlyComponentAttribute:Attribute {
}

/// <summary>
/// システムを生成されたアクセススケジュールへ参加させる。同一フェーズで同じコンポーネントにアクセスし、少なくとも一方が書き込むシステム同士は、
/// <see cref="RunsAfterAttribute"/>または<see cref="RunsBeforeAttribute"/>で順序を指定する必要がある（SYSGEN032）。実行順自体は変更しない。
/// </summary>
[AttributeUsage(AttributeTargets.Struct,AllowMultiple=false,Inherited=false)]
public sealed class ScheduledSystemAttribute:Attribute {
}

/// <summary>
/// ADR-0073：同一フェーズ内で、このシステムを<paramref name="SystemType"/>の後に実行する。
/// フェーズ内のシステムは、宣言されたすべての順序辺を維持するように並ぶ。相互に経路のないシステムは、完全名の序数順で実行する。
/// 先行フェーズのシステムは常に前に実行されるため、その型を指定しても効果はない。後続フェーズの型を指定するとSYSGEN035となる。
/// </summary>
[AttributeUsage(AttributeTargets.Struct,AllowMultiple=true,Inherited=false)]
public sealed class RunsAfterAttribute(Type SystemType):Attribute {
    public Type SystemType{get;}=SystemType??throw new ArgumentNullException(nameof(SystemType));
}

/// <summary>
/// ADR-0073：同一フェーズ内で、このシステムを<paramref name="SystemType"/>より前に実行する。
/// <see cref="RunsAfterAttribute"/>の逆方向の指定。エンジン側コードからゲーム側の型を参照できないため、
/// エンジンシステムより前に実行させるにはゲームコードでこれを指定する。
/// </summary>
[AttributeUsage(AttributeTargets.Struct,AllowMultiple=true,Inherited=false)]
public sealed class RunsBeforeAttribute(Type SystemType):Attribute {
    public Type SystemType{get;}=SystemType??throw new ArgumentNullException(nameof(SystemType));
}

/// <summary>ワールドレベルのシステムは、該当する各Entityではなく、フェーズごとに1回呼び出される。</summary>
public interface IWorldSystem:ISystem {
}

/// <summary>システムに公開する整数のシミュレーション時刻。固定更新に壁時計の値は持ち込まない。</summary>
public readonly record struct SimulationTiming(ulong Tick,int TickRate,SystemPhase Phase) {
    public static SimulationTiming Default=>new(0,60,SystemPhase.FixedSimulation);
}
