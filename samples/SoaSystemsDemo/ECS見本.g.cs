using System.Runtime.InteropServices;
using System.Security.AccessControl;

using CSharpGameEngine.Entities;

// ============================================================
// System = 条件に一致するEntityのデータを一括処理する
// ============================================================
public partial struct MovementSystem : ISystem{
    public void OnUpdate(ref SystemState state){
        float deltaTime = SystemAPI.Time.DeltaTime;

        // LocalTransform + Velocity + MovingObjectを持つ
        // すべてのEntityが処理対象になる。
        new MovementJob
        {
            DeltaTime = deltaTime
        }.ScheduleParallel();
    }
}

// ============================================================
// Job = Entityごとの処理
// ============================================================
public partial struct MovementJob : IJobEntity{
    public float DeltaTime;
    private void Execute(
        ref LocalTransform transform,
        in Velocity velocity,
        in MovingObject tag)
    {
        transform.Position += velocity.Value * DeltaTime;
    }
}

// ============================================================
// 寿命が切れたEntityを削除するSystem
// ============================================================
public partial struct LifetimeSystem : ISystem
{
    public void OnUpdate(ref SystemState state){
        float deltaTime = SystemAPI.Time.DeltaTime;
        // Entityの生成・削除・Component追加削除は構造変更になるため、
        // ECBに命令を記録し、あとでまとめて実行する。
        var ecb =
            new EntityCommandBuffer(Allocator.Temp);
        foreach (var (lifetime, entity) in SystemAPI.Query<RefRW<Lifetime>>()
                                                    .WithEntityAccess()){
            lifetime.ValueRW.Value -= deltaTime;
            if (lifetime.ValueRO.Value <= 0f){
                ecb.DestroyEntity(entity);
            }
        }
        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }
}

// ============================================================
// Entityを一度だけ生成するSystem
// ============================================================

public partial struct SpawnSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        EntityManager entityManager = state.EntityManager;
        // Componentの組み合わせがArchetype。
        EntityArchetype archetype =
            entityManager.CreateArchetype(
                typeof(LocalTransform),
                typeof(Velocity),
                typeof(Lifetime),
                typeof(MovingObject));

        NativeArray<Entity> entities =
            entityManager.CreateEntity(
                archetype,
                100,
                Allocator.Temp);

        for (var i = 0; i < entities.Length; i++){
            Entity entity = entities[i];
            entityManager.SetComponentData(
                entity,
                LocalTransform.FromPosition(new float3(i, 0f, 0f)));
            entityManager.SetComponentData(
                entity,
                new Velocity{
                                Value = new float3(0f, 0f, 1f)
                            });
            entityManager.SetComponentData(
                entity,
                new Lifetime{
                                Value = 10f
                            });
        }

        entities.Dispose();

        // 一度生成したら、このSystemは無効化する。
        state.Enabled = false;
    }
    public void OnUpdate(ref SystemState state){
    }
}
