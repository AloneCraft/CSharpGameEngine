#nullable enable

// 検討用プロトタイプ。
// 必要条件:
//   - .NET 9 以上
//   - C# 13 以上（partial propertyを使用）
//   - unsafeを許可すること
//
// 例:
//   csc /unsafe /langversion:13 ArchetypeNativeMemoryPrototype.cs
//
// 設計上の重要事項:
//   - World / Archetype / Chunk / NativeVectorはすべてstruct。
//   - 実データはNativeMemoryで確保する。
//   - 所有権を持つstructをコピーしてはいけない。
//   - WorldとArchetypeには必ずref経由でアクセスする。
//   - Dispose後にアクセスしてはいけない。

using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace UserGameProject{

    // ================================================================
    // 1. ライブラリ側: 基本型と属性
    // ================================================================

    public readonly record struct Entity(long Id){
        public static Entity Null   => default;
        public        bool   IsNull => Id == 0;

        public override string ToString() =>
            IsNull ? "Entity.Null" : $"Entity({Id})";
    }

    [AttributeUsage(AttributeTargets.Struct)]
    public sealed class ComponentAttribute(int canonicalOrder) : Attribute{
        public int CanonicalOrder{ get; } = canonicalOrder;
    }

    [AttributeUsage(AttributeTargets.Struct)]
    public sealed class GameObjectAttribute : Attribute{ }

    namespace Components{
        // SGはCanonicalOrderで型引数順を正規化する想定。

        [Component(0)]
        public readonly record struct Scene(long Id);

        [Component(1)]
        public readonly record struct Parent(Entity Entity);

        [Component(2)]
        public struct Transform{
            public Vector3    Scale;
            public Quaternion Rotation;
            public Vector3    Position;

            public static Transform Identity => new(){
                                                         Scale    = Vector3.One,
                                                         Rotation = Quaternion.Identity,
                                                         Position = Vector3.Zero
                                                     };
        }

        [Component(10)]
        public struct Light{
            public float Intensity;
        }

        [Component(11)]
        public struct Camera{
            public float FieldOfView;
        }

        [Component(12)]
        public struct Rendering{
            public int MeshId;
            public int MaterialId;
        }
    }

    // ================================================================
    // 2. ライブラリ側: NativeMemory補助型
    // ================================================================

    namespace Native{
        internal static class NativeLayout{
            // 検討用として、各SoA列を64バイト境界に配置する。
            public static readonly nuint ColumnAlignment = 64;

            public static nuint AlignUp(nuint value, nuint alignment){
                if (alignment == 0 || (alignment & (alignment - 1)) != 0)
                    throw new ArgumentOutOfRangeException(nameof(alignment));

                return checked((value + alignment - 1) & ~(alignment - 1));
            }

            public static unsafe nuint ByteCount<T>(int count)
            where T : unmanaged{
                if (count < 0)
                    throw new ArgumentOutOfRangeException(nameof(count));

                return checked((nuint)count * (nuint)sizeof(T));
            }
        }


    }

    // ================================================================
    // 3. ライブラリ側: NativeMemoryを使うChunk
    //
    // 1 Chunk = 1個のNativeMemoryブロック。
    // ブロック内にEntity列と各Component列をSoA配置する。
    // ================================================================

    namespace Archetypes{
        using Native;

        public unsafe struct NativeChunk<T0, T1, T2> : IDisposable
        where T0 : unmanaged
        where T1 : unmanaged
        where T2 : unmanaged{
            private byte* _memory;
            private nuint _entityOffset;
            private nuint _value0Offset;
            private nuint _value1Offset;
            private nuint _value2Offset;
            private int   _count;
            private int   _capacity;

            public NativeChunk(int capacity){
                if (capacity <= 0)
                    throw new ArgumentOutOfRangeException(nameof(capacity));

                _memory       = null;
                _entityOffset = 0;
                _value0Offset = 0;
                _value1Offset = 0;
                _value2Offset = 0;
                _count        = 0;
                _capacity     = capacity;

                nuint cursor = 0;

                _entityOffset = NativeLayout.AlignUp(cursor, NativeLayout.ColumnAlignment);
                cursor        = checked(_entityOffset + NativeLayout.ByteCount<Entity>(capacity));

                _value0Offset = NativeLayout.AlignUp(cursor, NativeLayout.ColumnAlignment);
                cursor        = checked(_value0Offset + NativeLayout.ByteCount<T0>(capacity));

                _value1Offset = NativeLayout.AlignUp(cursor, NativeLayout.ColumnAlignment);
                cursor        = checked(_value1Offset + NativeLayout.ByteCount<T1>(capacity));

                _value2Offset = NativeLayout.AlignUp(cursor, NativeLayout.ColumnAlignment);
                cursor        = checked(_value2Offset + NativeLayout.ByteCount<T2>(capacity));

                var totalBytes = NativeLayout.AlignUp(cursor, NativeLayout.ColumnAlignment);
                _memory = (byte*)NativeMemory.AlignedAlloc(
                                                           totalBytes,
                                                           NativeLayout.ColumnAlignment);

                if (_memory == null)
                    throw new OutOfMemoryException();
            }

            public readonly int  Count     => _count;
            public readonly int  Capacity  => _capacity;
            public readonly bool IsCreated => _memory != null;
            public readonly bool IsFull    => _count  >= _capacity;

            private readonly Entity* EntityPointer =>
                (Entity*)(_memory + _entityOffset);

            private readonly T0* Value0Pointer =>
                (T0*)(_memory + _value0Offset);

            private readonly T1* Value1Pointer =>
                (T1*)(_memory + _value1Offset);

            private readonly T2* Value2Pointer =>
                (T2*)(_memory + _value2Offset);

            public readonly ReadOnlySpan<Entity> Entities =>
                _count == 0
                    ? ReadOnlySpan<Entity>.Empty
                    : new ReadOnlySpan<Entity>(EntityPointer, _count);

            public readonly Span<T0> Values0 =>
                _count == 0 ? Span<T0>.Empty : new Span<T0>(Value0Pointer, _count);

            public readonly Span<T1> Values1 =>
                _count == 0 ? Span<T1>.Empty : new Span<T1>(Value1Pointer, _count);

            public readonly Span<T2> Values2 =>
                _count == 0 ? Span<T2>.Empty : new Span<T2>(Value2Pointer, _count);

            public void Add(
                Entity entity,
                in T0  value0,
                in T1  value1,
                in T2  value2){
                if (_memory == null)
                    throw new ObjectDisposedException(nameof(NativeChunk<T0, T1, T2>));

                if (IsFull)
                    throw new InvalidOperationException("Chunk is full.");

                var index = _count++;
                EntityPointer[index] = entity;
                Value0Pointer[index] = value0;
                Value1Pointer[index] = value1;
                Value2Pointer[index] = value2;
            }

            public void Dispose(){
                if (_memory != null)
                    NativeMemory.AlignedFree(_memory);

                this = default;
            }
        }

        public unsafe struct NativeChunk<T0, T1, T2, T3> : IDisposable
        where T0 : unmanaged
        where T1 : unmanaged
        where T2 : unmanaged
        where T3 : unmanaged{
            private byte* _memory;
            private nuint _entityOffset;
            private nuint _value0Offset;
            private nuint _value1Offset;
            private nuint _value2Offset;
            private nuint _value3Offset;
            private int   _count;
            private int   _capacity;

            public NativeChunk(int capacity){
                if (capacity <= 0)
                    throw new ArgumentOutOfRangeException(nameof(capacity));

                _memory       = null;
                _entityOffset = 0;
                _value0Offset = 0;
                _value1Offset = 0;
                _value2Offset = 0;
                _value3Offset = 0;
                _count        = 0;
                _capacity     = capacity;

                nuint cursor = 0;

                _entityOffset = NativeLayout.AlignUp(cursor, NativeLayout.ColumnAlignment);
                cursor        = checked(_entityOffset + NativeLayout.ByteCount<Entity>(capacity));

                _value0Offset = NativeLayout.AlignUp(cursor, NativeLayout.ColumnAlignment);
                cursor        = checked(_value0Offset + NativeLayout.ByteCount<T0>(capacity));

                _value1Offset = NativeLayout.AlignUp(cursor, NativeLayout.ColumnAlignment);
                cursor        = checked(_value1Offset + NativeLayout.ByteCount<T1>(capacity));

                _value2Offset = NativeLayout.AlignUp(cursor, NativeLayout.ColumnAlignment);
                cursor        = checked(_value2Offset + NativeLayout.ByteCount<T2>(capacity));

                _value3Offset = NativeLayout.AlignUp(cursor, NativeLayout.ColumnAlignment);
                cursor        = checked(_value3Offset + NativeLayout.ByteCount<T3>(capacity));

                var totalBytes = NativeLayout.AlignUp(cursor, NativeLayout.ColumnAlignment);
                _memory = (byte*)NativeMemory.AlignedAlloc(
                                                           totalBytes,
                                                           NativeLayout.ColumnAlignment);

                if (_memory == null)
                    throw new OutOfMemoryException();
            }

            public readonly int  Count     => _count;
            public readonly int  Capacity  => _capacity;
            public readonly bool IsCreated => _memory != null;
            public readonly bool IsFull    => _count  >= _capacity;

            private readonly Entity* EntityPointer =>
                (Entity*)(_memory + _entityOffset);

            private readonly T0* Value0Pointer =>
                (T0*)(_memory + _value0Offset);

            private readonly T1* Value1Pointer =>
                (T1*)(_memory + _value1Offset);

            private readonly T2* Value2Pointer =>
                (T2*)(_memory + _value2Offset);

            private readonly T3* Value3Pointer =>
                (T3*)(_memory + _value3Offset);

            public readonly ReadOnlySpan<Entity> Entities =>
                _count == 0
                    ? ReadOnlySpan<Entity>.Empty
                    : new ReadOnlySpan<Entity>(EntityPointer, _count);

            public readonly Span<T0> Values0 =>
                _count == 0 ? Span<T0>.Empty : new Span<T0>(Value0Pointer, _count);

            public readonly Span<T1> Values1 =>
                _count == 0 ? Span<T1>.Empty : new Span<T1>(Value1Pointer, _count);

            public readonly Span<T2> Values2 =>
                _count == 0 ? Span<T2>.Empty : new Span<T2>(Value2Pointer, _count);

            public readonly Span<T3> Values3 =>
                _count == 0 ? Span<T3>.Empty : new Span<T3>(Value3Pointer, _count);

            public void Add(
                Entity entity,
                in T0  value0,
                in T1  value1,
                in T2  value2,
                in T3  value3){
                if (_memory == null)
                    throw new ObjectDisposedException(nameof(NativeChunk<T0, T1, T2, T3>));

                if (IsFull)
                    throw new InvalidOperationException("Chunk is full.");

                var index = _count++;
                EntityPointer[index] = entity;
                Value0Pointer[index] = value0;
                Value1Pointer[index] = value1;
                Value2Pointer[index] = value2;
                Value3Pointer[index] = value3;
            }

            public void Dispose(){
                if (_memory != null)
                    NativeMemory.AlignedFree(_memory);

                this = default;
            }
        }

        // ============================================================
        // 4. ライブラリ側: struct Archetype
        //
        // Chunkメタデータ配列もNativeMemoryで保持する。
        // NativeMemory.ReallocはChunk構造体をビット単位で移動するが、
        // 各Chunkが指す本体ブロックの所有権はそのまま維持される。
        // ============================================================

        public unsafe struct Archetype<T0, T1, T2> : IDisposable
        where T0 : unmanaged
        where T1 : unmanaged
        where T2 : unmanaged{
            public const int DefaultChunkCapacity = 64;

            private NativeChunk<T0, T1, T2>* _chunks;
            private int                      _chunkCount;
            private int                      _chunkMetadataCapacity;
            private int                      _entityCount;
            private int                      _entityCapacityPerChunk;

            public readonly int  Count      => _entityCount;
            public readonly int  ChunkCount => _chunkCount;
            public readonly bool IsCreated  => _entityCapacityPerChunk > 0;

            public void EnsureCreated(int chunkCapacity = DefaultChunkCapacity){
                if (IsCreated)
                    return;

                if (chunkCapacity <= 0)
                    throw new ArgumentOutOfRangeException(nameof(chunkCapacity));

                _chunks                 = null;
                _chunkCount             = 0;
                _chunkMetadataCapacity  = 0;
                _entityCount            = 0;
                _entityCapacityPerChunk = chunkCapacity;
            }

            public void Add(
                Entity entity,
                in T0  value0,
                in T1  value1,
                in T2  value2){
                EnsureCreated();

                ref var chunk = ref GetWritableChunk();
                chunk.Add(entity, in value0, in value1, in value2);
                _entityCount++;
            }

            public ref NativeChunk<T0, T1, T2> GetChunk(int index){
                if ((uint)index >= (uint)_chunkCount)
                    throw new ArgumentOutOfRangeException(nameof(index));

                return ref _chunks[index];
            }

            private ref NativeChunk<T0, T1, T2> GetWritableChunk(){
                if (_chunkCount != 0){
                    ref var last = ref _chunks[_chunkCount - 1];
                    if (!last.IsFull)
                        return ref last;
                }

                EnsureChunkMetadataCapacity(checked(_chunkCount + 1));

                ref var created = ref _chunks[_chunkCount++];
                created = new NativeChunk<T0, T1, T2>(_entityCapacityPerChunk);
                return ref created;
            }

            private void EnsureChunkMetadataCapacity(int requiredCapacity){
                if (requiredCapacity <= _chunkMetadataCapacity)
                    return;

                var newCapacity = _chunkMetadataCapacity == 0
                                      ? 4
                                      : _chunkMetadataCapacity;

                while (newCapacity < requiredCapacity)
                    newCapacity = checked(newCapacity * 2);

                var bytes   = NativeLayout.ByteCount<NativeChunk<T0, T1, T2>>(newCapacity);
                var resized = NativeMemory.Realloc(_chunks, bytes);

                if (resized == null)
                    throw new OutOfMemoryException();

                _chunks                = (NativeChunk<T0, T1, T2>*)resized;
                _chunkMetadataCapacity = newCapacity;
            }

            public void Dispose(){
                if (_chunks != null){
                    for (var i = 0; i < _chunkCount; i++)
                        _chunks[i].Dispose();

                    NativeMemory.Free(_chunks);
                }

                this = default;
            }
        }

        public unsafe struct Archetype<T0, T1, T2, T3> : IDisposable
        where T0 : unmanaged
        where T1 : unmanaged
        where T2 : unmanaged
        where T3 : unmanaged{
            public const int DefaultChunkCapacity = 64;

            private NativeChunk<T0, T1, T2, T3>* _chunks;
            private int                          _chunkCount;
            private int                          _chunkMetadataCapacity;
            private int                          _entityCount;
            private int                          _entityCapacityPerChunk;

            public readonly int  Count      => _entityCount;
            public readonly int  ChunkCount => _chunkCount;
            public readonly bool IsCreated  => _entityCapacityPerChunk > 0;

            public void EnsureCreated(int chunkCapacity = DefaultChunkCapacity){
                if (IsCreated)
                    return;

                if (chunkCapacity <= 0)
                    throw new ArgumentOutOfRangeException(nameof(chunkCapacity));

                _chunks                 = null;
                _chunkCount             = 0;
                _chunkMetadataCapacity  = 0;
                _entityCount            = 0;
                _entityCapacityPerChunk = chunkCapacity;
            }

            public void Add(
                Entity entity,
                in T0  value0,
                in T1  value1,
                in T2  value2,
                in T3  value3){
                EnsureCreated();

                ref var chunk = ref GetWritableChunk();
                chunk.Add(entity, in value0, in value1, in value2, in value3);
                _entityCount++;
            }

            public ref NativeChunk<T0, T1, T2, T3> GetChunk(int index){
                if ((uint)index >= (uint)_chunkCount)
                    throw new ArgumentOutOfRangeException(nameof(index));

                return ref _chunks[index];
            }

            private ref NativeChunk<T0, T1, T2, T3> GetWritableChunk(){
                if (_chunkCount != 0){
                    ref var last = ref _chunks[_chunkCount - 1];
                    if (!last.IsFull)
                        return ref last;
                }

                EnsureChunkMetadataCapacity(checked(_chunkCount + 1));

                ref var created = ref _chunks[_chunkCount++];
                created = new NativeChunk<T0, T1, T2, T3>(_entityCapacityPerChunk);
                return ref created;
            }

            private void EnsureChunkMetadataCapacity(int requiredCapacity){
                if (requiredCapacity <= _chunkMetadataCapacity)
                    return;

                var newCapacity = _chunkMetadataCapacity == 0
                                      ? 4
                                      : _chunkMetadataCapacity;

                while (newCapacity < requiredCapacity)
                    newCapacity = checked(newCapacity * 2);

                var bytes   = NativeLayout.ByteCount<NativeChunk<T0, T1, T2, T3>>(newCapacity);
                var resized = NativeMemory.Realloc(_chunks, bytes);

                if (resized == null)
                    throw new OutOfMemoryException();

                _chunks                = (NativeChunk<T0, T1, T2, T3>*)resized;
                _chunkMetadataCapacity = newCapacity;
            }

            public void Dispose(){
                if (_chunks != null){
                    for (var i = 0; i < _chunkCount; i++)
                        _chunks[i].Dispose();

                    NativeMemory.Free(_chunks);
                }

                this = default;
            }
        }

    }

    // ================================================================
    // 5. ライブラリ側: SG生成オブジェクトが実装する契約
    // ================================================================

    namespace Objects{
        public interface IGeneratedGameObject<TSelf>
        where TSelf : struct, IGeneratedGameObject<TSelf>{
            static abstract void RegisterArchetype(ref World world);

            static abstract Entity Spawn(
                ref World world,
                in  TSelf value,
                Entity    parent);
        }
    }

    // ================================================================
    // 6. ユーザーが記述するコード
    //
    // SGはこの部分のpartial propertyを調べ、必要なArchetypeを決定する。
    // ================================================================

    namespace Objects{
        [GameObject]
        public partial struct NodeObject{
            public partial Components.Scene     Scene    { get; init; }
            public partial Components.Transform Transform{ get; init; }
        }

        [GameObject]
        public partial struct LightObject{
            public partial Components.Scene     Scene    { get; init; }
            public partial Components.Transform Transform{ get; init; }
            public partial Components.Light     Light    { get; init; }
        }

        [GameObject]
        public partial struct CameraObject{
            public partial Components.Scene     Scene    { get; init; }
            public partial Components.Transform Transform{ get; init; }
            public partial Components.Camera    Camera   { get; init; }
        }

        [GameObject]
        public partial struct CubeObject{
            public partial Components.Scene     Scene    { get; init; }
            public partial Components.Transform Transform{ get; init; }
            public partial Components.Rendering Rendering{ get; init; }
        }
    }
    public struct Scene1{
        public Entity Root;
        public Entity Light;
        public Entity Camera;
        public Entity Floor;
        public Entity Cube0;
        public Entity Cube1;
    }

    public partial struct World{
        public Scene1 Scene1;
        partial void BuildInitialScene(){
            var scene = new Components.Scene(1);

            var root = Add(new Objects.NodeObject{
                                                     Scene     = scene,
                                                     Transform = Components.Transform.Identity
                                                 });

            var light = Add(new Objects.LightObject{
                                                       Scene = scene,
                                                       Transform = new Components.Transform{
                                                                       Scale    = Vector3.One,
                                                                       Rotation = Quaternion.Identity,
                                                                       Position = new Vector3(0, 4, 0)
                                                                   },
                                                       Light = new Components.Light{
                                                                   Intensity = 10.0f
                                                               }
                                                   }, parent: root);

            var camera = Add(new Objects.CameraObject{
                                                         Scene = scene,
                                                         Transform = new Components.Transform{
                                                                         Scale    = Vector3.One,
                                                                         Rotation = Quaternion.Identity,
                                                                         Position = new Vector3(0, 2, -5)
                                                                     },
                                                         Camera = new Components.Camera{
                                                                      FieldOfView = 60.0f
                                                                  }
                                                     }, parent: root);

            var floor = Add(new Objects.CubeObject{
                                                      Scene = scene,
                                                      Transform = new Components.Transform{
                                                                      Scale    = new Vector3(10, 1, 10),
                                                                      Rotation = Quaternion.Identity,
                                                                      Position = Vector3.Zero
                                                                  },
                                                      Rendering = new Components.Rendering{
                                                                      MeshId     = 1,
                                                                      MaterialId = 10
                                                                  }
                                                  }, parent: root);

            var cube0 = Add(new Objects.CubeObject{
                                                      Scene = scene,
                                                      Transform = new Components.Transform{
                                                                      Scale    = Vector3.One,
                                                                      Rotation = Quaternion.Identity,
                                                                      Position = new Vector3(-2, 1, 0)
                                                                  },
                                                      Rendering = new Components.Rendering{
                                                                      MeshId     = 2,
                                                                      MaterialId = 20
                                                                  }
                                                  }, parent: floor);

            var cube1 = Add(new Objects.CubeObject{
                                                      Scene = scene,
                                                      Transform = new Components.Transform{
                                                                      Scale    = Vector3.One,
                                                                      Rotation = Quaternion.Identity,
                                                                      Position = new Vector3(2, 1, 0)
                                                                  },
                                                      Rendering = new Components.Rendering{
                                                                      MeshId     = 2,
                                                                      MaterialId = 21
                                                                  }
                                                  }, parent: floor);

            Scene1 = new Scene1{
                                   Root   = root,
                                   Light  = light,
                                   Camera = camera,
                                   Floor  = floor,
                                   Cube0  = cube0,
                                   Cube1  = cube1
                               };
        }
    }
    // ================================================================
    // 7. SGが生成する想定: WorldのArchetypeフィールド
    //
    // SGは全GameObjectを走査し、同一コンポーネント集合を重複排除する。
    // ParentはSGが全オブジェクトへ自動追加する想定。
    // ================================================================

    [StructLayout(LayoutKind.Auto)]
    public partial struct World : IDisposable{
        public World()
        {
            this = default;

            try
            {
                BuildInitialScene();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        partial void BuildInitialScene();
        private long _nextEntityId;

        private Archetypes.Archetype<
            Components.Scene,
            Components.Parent,
            Components.Transform> _sceneParentTransform;

        private Archetypes.Archetype<
            Components.Scene,
            Components.Parent,
            Components.Transform,
            Components.Light> _sceneParentTransformLight;

        private Archetypes.Archetype<
            Components.Scene,
            Components.Parent,
            Components.Transform,
            Components.Camera> _sceneParentTransformCamera;

        private Archetypes.Archetype<
            Components.Scene,
            Components.Parent,
            Components.Transform,
            Components.Rendering> _sceneParentTransformRendering;

        public Entity Add<T>(in T value, Entity parent = default)
        where T : struct, Objects.IGeneratedGameObject<T>{
            T.RegisterArchetype(ref this);
            return T.Spawn(ref this, in value, parent);
        }

        public void Register<T>()
        where T : struct, Objects.IGeneratedGameObject<T>{
            T.RegisterArchetype(ref this);
        }

        internal Entity AllocateEntity() => new(++_nextEntityId);

        // ref Worldを受けるstatic accessorなら、structのフィールドをrefで返せる。
        internal static ref Archetypes.Archetype<
            Components.Scene,
            Components.Parent,
            Components.Transform> GetSceneParentTransform(ref World world) =>
            ref world._sceneParentTransform;

        internal static ref Archetypes.Archetype<
            Components.Scene,
            Components.Parent,
            Components.Transform,
            Components.Light> GetSceneParentTransformLight(ref World world) =>
            ref world._sceneParentTransformLight;

        internal static ref Archetypes.Archetype<
            Components.Scene,
            Components.Parent,
            Components.Transform,
            Components.Camera> GetSceneParentTransformCamera(ref World world) =>
            ref world._sceneParentTransformCamera;

        internal static ref Archetypes.Archetype<
            Components.Scene,
            Components.Parent,
            Components.Transform,
            Components.Rendering> GetSceneParentTransformRendering(ref World world) =>
            ref world._sceneParentTransformRendering;

        public void Dispose(){
            _sceneParentTransform.Dispose();
            _sceneParentTransformLight.Dispose();
            _sceneParentTransformCamera.Dispose();
            _sceneParentTransformRendering.Dispose();
            this = default;
        }
    }

    // ================================================================
    // 8. SGが生成する想定: 各Objectのpartial実装
    // ================================================================

    namespace Objects{
        public partial struct NodeObject : IGeneratedGameObject<NodeObject>{
            private Components.Scene     _scene;
            private Components.Transform _transform;

            public partial Components.Scene Scene{
                get => _scene;
                init => _scene = value;
            }

            public partial Components.Transform Transform{
                get => _transform;
                init => _transform = value;
            }

            public static void RegisterArchetype(ref World world){
                ref var archetype =
                    ref World.GetSceneParentTransform(ref world);

                archetype.EnsureCreated();
            }

            public static Entity Spawn(
                ref World      world,
                in  NodeObject value,
                Entity         parent){
                var entity          = world.AllocateEntity();
                var    parentComponent = new Components.Parent(parent);

                ref var archetype =
                    ref World.GetSceneParentTransform(ref world);

                archetype.Add(
                              entity,
                              in value._scene,
                              in parentComponent,
                              in value._transform);

                return entity;
            }
        }

        public partial struct LightObject : IGeneratedGameObject<LightObject>{
            private Components.Scene     _scene;
            private Components.Transform _transform;
            private Components.Light     _light;

            public partial Components.Scene Scene{
                get => _scene;
                init => _scene = value;
            }

            public partial Components.Transform Transform{
                get => _transform;
                init => _transform = value;
            }

            public partial Components.Light Light{
                get => _light;
                init => _light = value;
            }

            public static void RegisterArchetype(ref World world){
                ref var archetype =
                    ref World.GetSceneParentTransformLight(ref world);

                archetype.EnsureCreated();
            }

            public static Entity Spawn(
                ref World       world,
                in  LightObject value,
                Entity          parent){
                var entity          = world.AllocateEntity();
                var    parentComponent = new Components.Parent(parent);

                ref var archetype =
                    ref World.GetSceneParentTransformLight(ref world);

                archetype.Add(
                              entity,
                              in value._scene,
                              in parentComponent,
                              in value._transform,
                              in value._light);

                return entity;
            }
        }

        public partial struct CameraObject : IGeneratedGameObject<CameraObject>{
            private Components.Scene     _scene;
            private Components.Transform _transform;
            private Components.Camera    _camera;

            public partial Components.Scene Scene{
                get => _scene;
                init => _scene = value;
            }

            public partial Components.Transform Transform{
                get => _transform;
                init => _transform = value;
            }

            public partial Components.Camera Camera{
                get => _camera;
                init => _camera = value;
            }

            public static void RegisterArchetype(ref World world){
                ref var archetype =
                    ref World.GetSceneParentTransformCamera(ref world);

                archetype.EnsureCreated();
            }

            public static Entity Spawn(
                ref World        world,
                in  CameraObject value,
                Entity           parent){
                var entity          = world.AllocateEntity();
                var    parentComponent = new Components.Parent(parent);

                ref var archetype =
                    ref World.GetSceneParentTransformCamera(ref world);

                archetype.Add(
                              entity,
                              in value._scene,
                              in parentComponent,
                              in value._transform,
                              in value._camera);

                return entity;
            }
        }

        public partial struct CubeObject : IGeneratedGameObject<CubeObject>{
            private Components.Scene     _scene;
            private Components.Transform _transform;
            private Components.Rendering _rendering;

            public partial Components.Scene Scene{
                get => _scene;
                init => _scene = value;
            }

            public partial Components.Transform Transform{
                get => _transform;
                init => _transform = value;
            }

            public partial Components.Rendering Rendering{
                get => _rendering;
                init => _rendering = value;
            }

            public static void RegisterArchetype(ref World world){
                ref var archetype =
                    ref World.GetSceneParentTransformRendering(ref world);

                archetype.EnsureCreated();
            }

            public static Entity Spawn(
                ref World      world,
                in  CubeObject value,
                Entity         parent){
                var entity          = world.AllocateEntity();
                var    parentComponent = new Components.Parent(parent);

                ref var archetype =
                    ref World.GetSceneParentTransformRendering(ref world);

                archetype.Add(
                              entity,
                              in value._scene,
                              in parentComponent,
                              in value._transform,
                              in value._rendering);

                return entity;
            }
        }
    }

    // ================================================================
    // 9. 利用例
    // ================================================================

    internal class Program{
        private void Main(){
            World world = default;

            try{
                var scene = new Components.Scene(1);

                // 明示登録も可能。Add内でも遅延登録される。
                world.Register<Objects.NodeObject>();
                world.Register<Objects.LightObject>();
                world.Register<Objects.CameraObject>();
                world.Register<Objects.CubeObject>();

                var root = world.Add(new Objects.NodeObject{
                                                               Scene     = scene,
                                                               Transform = Components.Transform.Identity
                                                           });

                var light = world.Add(new Objects.LightObject{
                                                                 Scene = scene,
                                                                 Transform = new Components.Transform{
                                                                                 Scale    = Vector3.One,
                                                                                 Rotation = Quaternion.Identity,
                                                                                 Position = new Vector3(0, 4, 0)
                                                                             },
                                                                 Light = new Components.Light{
                                                                             Intensity = 10.0f
                                                                         }
                                                             }, parent: root);

                var camera = world.Add(new Objects.CameraObject{
                                                                   Scene = scene,
                                                                   Transform = new Components.Transform{
                                                                                   Scale    = Vector3.One,
                                                                                   Rotation = Quaternion.Identity,
                                                                                   Position = new Vector3(0, 2, -5)
                                                                               },
                                                                   Camera = new Components.Camera{
                                                                                FieldOfView = 60.0f
                                                                            }
                                                               }, parent: root);

                var cube = world.Add(new Objects.CubeObject{
                                                               Scene     = scene,
                                                               Transform = Components.Transform.Identity,
                                                               Rendering = new Components.Rendering{
                                                                               MeshId     = 1,
                                                                               MaterialId = 10
                                                                           }
                                                           }, parent: root);

                Console.WriteLine($"Created: {root}, {light}, {camera}, {cube}");

                // Queryのイメージ。
                // Worldを値コピーせず、Archetypeをrefで取得する。
                ref var lightArchetype =
                    ref World.GetSceneParentTransformLight(ref world);

                Console.WriteLine(
                                  $"Light archetype: {lightArchetype.Count} entities, " +
                                  $"{lightArchetype.ChunkCount} chunks");

                for (var chunkIndex = 0;
                     chunkIndex < lightArchetype.ChunkCount;
                     chunkIndex++){
                    ref var chunk =
                        ref lightArchetype.GetChunk(chunkIndex);

                    var       entities   = chunk.Entities;
                    var transforms = chunk.Values2;
                    var     lights     = chunk.Values3;

                    for (var i = 0; i < chunk.Count; i++){
                        Console.WriteLine(
                                          $"{entities[i]}: Position={transforms[i].Position}, " +
                                          $"Intensity={lights[i].Intensity}");
                    }
                }
            }
            finally{
                // NativeMemoryはGCに解放されないため必須。
                world.Dispose();
            }
        }
    }
}














//// Temporarily commented out at the user's request (2026-08-03): this file had two conflicting
//// partial `Scene1` declarations (CS8983/CS0282) that blocked the whole UserGameProject build,
//// unrelated to the M0004 UE-FirstPerson-stage-port work in progress this session. Restore by
//// removing this comment wrapper once the conflicting declarations below are resolved.

//using System.Collections;
//using System.Numerics;
//using System.Runtime.CompilerServices;
//using System.Runtime.InteropServices;

//using TerraFX.Interop.Windows;
//using UserGameProject.Archtypes;
//using UserGameProject.Components;
//using UserGameProject.Objects;


//namespace UserGameProject{
//    public record struct Entity(long Id);
//    //ライブラリに定義
//    namespace Components{
//        public record struct Scene(long Id);
//        public struct Parent{
//            public Entity     Entity;
//        }
//        public struct Transform{
//            public Vector3    Scale;
//            public Quaternion Rotation;
//            public Vector3    Position;
//        }
//        public partial struct Light{
//        }
//        public partial struct Camera{
//        }
//        public partial struct Rendering{
//        }
//    }
//    //ライブラリに定義
//    namespace Archtypes{
//        public record struct Archtype<T0    >(T0 Value0           ) where T0 : unmanaged                     ;
//        public record struct Archtype<T0, T1>(T0 Value0, T1 Value1)where T0 : unmanaged where T1 : unmanaged;
//        public record struct Archtype<T0, T1,T2>(T0 Value0, T1 Value1, T2 Value2)where T0 : unmanaged where T1 : unmanaged where T2 : unmanaged;
//    }

//    namespace Objects{
//        /// <summary>
//        /// ライブラリとして定義するもの
//        /// </summary>
//        public interface IGameObject:IEnumerable<IGameObject>{
//            List<Entity> Children{ get; }
//        }
//        public interface IGeneratedGameObject<TSelf>
//        where TSelf : struct, IGeneratedGameObject<TSelf>
//        {
//            static abstract void RegisterArchetype(World world);

//            static abstract Entity Spawn(
//                World    world,
//                in TSelf value,
//                Entity   parent);
//        }
//        public partial struct NodeObject{
//            public partial Components.Scene     Scene    { get; }
//            public partial Components.Transform Transform{ get; }
//        }
//        public partial struct LightObject{
//            public partial Components.Scene     Scene    { get; }
//            public partial Components.Transform Transform{ get; }
//            public partial Components.Light     Light    { get; }
//        }
//        public partial struct CameraObject{
//            public partial Components.Scene     Scene    { get; }
//            public partial Components.Transform Transform{ get; }
//            public partial Components.Camera    Camera   { get; }
//        }
//        public partial struct CubeObject{
//            public partial Components.Scene     Scene    { get; }
//            public partial Components.Transform Transform{ get; }
//            public partial Components.Rendering    Rendering   { get; }
//        }
//        //public interface ILightObject{
//        //    Components.Transform                                           Transform{ get; }
//        //    Components.Light                                               Light    { get; }
//        //}
//        /// <summary>
//        /// SGで生成したコード例
//        /// </summary>
//        [StructLayout(LayoutKind.Auto)]
//        public partial struct NodeObject() : IGameObject,ICreateArchtype<Components.Scene,Components.Transform>{
//            public         Archtype<Components.Scene,Components.Transform> Create() =>new(this._Scene,this._Transform);
//            public         Components.Scene                                _Scene;
//            public partial Components.Scene                                Scene=>this._Scene;
//            public         Components.Transform                            _Transform;
//            public partial Components.Transform                            Transform      => this._Transform;
//            public         List<Entity>                                    Children       => throw new NotImplementedException();
//            public         IEnumerator<IGameObject>                        GetEnumerator(){
//                throw new NotImplementedException();
//            }

//            IEnumerator IEnumerable.                                       GetEnumerator(){
//                return GetEnumerator();
//            }
//        }
//        [StructLayout(LayoutKind.Auto)]
//        public partial struct LightObject() : IGameObject,ICreateArchtype<Components.Scene,Components.Transform, Components.Light>{
//            public         Archtype<Components.Scene,Components.Transform, Components.Light> Create() =>new(this._Scene,this._Transform,this._Light);
//            public         Components.Scene                                 _Scene;
//            public partial Components.Scene                                 Scene=>this._Scene;
//            public         Components.Transform                             _Transform;
//            public partial Components.Transform                             Transform => this._Transform;
//            public         Components.Light                                 _Light = new();
//            public partial Components.Light                                 Light   => this._Light;
//            public List<Entity> Children => throw new NotImplementedException();
//            //public         List<Entity>    Children{ get; }//
//            public IEnumerator<IGameObject> GetEnumerator(){
//                throw new NotImplementedException();
//            }

//            IEnumerator IEnumerable.        GetEnumerator(){
//                return GetEnumerator();
//            }
//        }
//        [StructLayout(LayoutKind.Auto)]
//        public partial struct CameraObject() : IGameObject,ICreateArchtype<Components.Scene,Components.Transform, Components.Camera>{
//            public Archtype<Components.Scene,Components.Transform, Components.Camera> Create()=>new(this._Scene,this._Transform,this._Camera);
//            public         Components.Scene                                 _Scene;
//            public partial Components.Scene                                 Scene=>this._Scene;
//            public         Components.Transform _Transform;
//            public partial Components.Transform Transform => this._Transform;
//            public         Components.Camera    _Camera = new();
//            public partial Components.Camera    Camera  => this._Camera;

//            public List<Entity> Children => throw new NotImplementedException();
//            // public         List<Entity>         Children{ get; }
//            public IEnumerator<IGameObject> GetEnumerator(){
//                throw new NotImplementedException();
//            }

//            IEnumerator IEnumerable.        GetEnumerator(){
//                return GetEnumerator();
//            }
//        }
//        [StructLayout(LayoutKind.Auto)]
//        public partial struct CubeObject() : IGameObject,ICreateArchtype<Components.Scene,Components.Transform, Components.Rendering>{
//            public void         Add(IGameObject);
//            public Archtype<Components.Scene,Components.Transform, Components.Rendering> Create()=>new(this._Scene,this._Transform,this._Rendering);
//            public         Components.Scene                                 _Scene;
//            public partial Components.Scene                                 Scene=>this._Scene;
//            public         Components.Transform _Transform;
//            public partial Components.Transform Transform => this._Transform;
//            public         Components.Rendering    _Rendering = new();
//            public partial Components.Rendering    Rendering  => this._Rendering;

//            public List<Entity> Children => throw new NotImplementedException();
//            // public         List<Entity>         Children{ get; }
//            public IEnumerator<IGameObject> GetEnumerator(){
//                throw new NotImplementedException();
//            }

//            IEnumerator IEnumerable.        GetEnumerator(){
//                return GetEnumerator();
//            }
//        }
//    }


//    public interface IScene{
//        //List<Objects.IGameObject> Entities{ get; }

//        //public Entity Add<T>(T Value)where T:IGameObject{
//        //    var Entity = new Entity(this.Entities.Count);
//        //    this.Entities.Add(Value);
//        //    return Entity;
//        //}
//    }

//    public interface IWorld{
//    //    IGameObject[] Create{ get; }
//    //    void Add(LightObject Value);
//    //    void Add(CameraObject Value);
//    //    //void Add<T0,T1>(Archtype<T0,T1> Value)where T:ICreateArchtype<T0,T1>where T0:unmanaged where T1:unmanaged;
//    }
//    /// <summary>
//    /// Scene1
//    /// ├Light Components.Transform,Components.Light
//    /// ├Camera Components.Transform,Components.Camera
//    /// └Floor Components.Transform,Components.Mesh,Components.Renderer,Components.Collider
//    ///   ├Cube0 Components.Transform,Components.Mesh,Components.Renderer,Components.Collider,Components.RigidBody
//    ///   └Cube1 Components.Transform,Components.Mesh,Components.Renderer,Components.Collider,Components.RigidBody
//    /// これを表現するとする
//    /// </summary>
//    public partial struct Scene1:IScene{
//        //private List<Entity> _Children = null;
//        //public List<Entity> Children{ get; } = [
//        //                                        ];
//        //    //public Scene1(){
//        //    //    this.Children = [new Objects.LightObject()];
//        //    //}

//        //public List<Objects.IGameObject> Entities                                                { get; } = null;
//    }
//    [StructLayout(LayoutKind.Auto)]
//    public partial struct Scene1{
//        public Scene1(){ }
//        //public void Add2<T,T0>(T value) where T:ICreateArchtype<T0> where T0:unmanaged{
//        //    throw new InvalidOperationException($"{typeof(T).Name} must implement {nameof(IGameObject)}.");
//        //}
//        //public void Add<T,T0>(T Value) where T : ICreateArchtype<T0> where T0:unmanaged{
//        //    throw new NotImplementedException();
//        //}
//        //public void Add<T, T0, T1>(T Value) where T : ICreateArchtype<T0,T1> where T0 : unmanaged where T1 : unmanaged{
//        //    throw new NotImplementedException();
//        //}

//        //public void Add(LightObject  Value){
//        //    throw new NotImplementedException();
//        //}
//        //public void Add(CameraObject Value){
//        //    throw new NotImplementedException();
//        //}

//        ////public void Add<T0>(Archtype<T0> Value) where T0 : unmanaged {
//        ////    throw new NotImplementedException();
//        ////}
//        ////public void Add<T0, T1>(Archtype<T0,T1> Value) where T0 : unmanaged where T1 : unmanaged {
//        ////    throw new NotImplementedException();
//        ////}

//        //public IEnumerator<IGameObject> GetEnumerator(){
//        //    throw new NotImplementedException();
//        //}

//        //IEnumerator IEnumerable.        GetEnumerator(){
//        //    return this.GetEnumerator();
//        //}
//    }

//    public partial struct World{
//        public Scene1   Scene1;
//        public IGameObject[]Create=>[
//            new LightObject(),
//            new CameraObject(),
//            new CubeObject{
//                              new CubeObject(),
//                              new CubeObject()
//                          }
//        ];
//    }
//    /// <summary>
//    /// SGにより[Component]structのジェネリック引数の組み合わせ
//    /// </summary>
//    [StructLayout(LayoutKind.Auto)]
//    public partial struct World:IWorld{
//        private int _nextEntityId;

//        private readonly Dictionary<Type, object> _archetypes = new();

//        public void Register<T>()
//        where T : struct, IGeneratedGameObject<T>
//        {
//            T.RegisterArchetype(this);
//        }

//        public Entity Add<T>(
//            in T   value,
//            Entity parent = default)
//        where T : struct, IGeneratedGameObject<T>
//        {
//            // 未登録なら作成する。
//            T.RegisterArchetype(this);

//            return T.Spawn(this, in value, parent);
//        }

//        internal Entity AllocateEntity()
//        {
//            return new Entity(++_nextEntityId);
//        }
//        public readonly List<Archtype<Components.Transform>>                   Archtypes0 =new();
//        public readonly List<Archtype<Components.Transform,Components.Parent>> Archtypes1 =new();
//        public readonly List<Archtype<Components.Transform,Components.Camera>> Archtypes2 =new();
//        public readonly List<Archtype<Components.Transform,Components.Light >> Archtypes3 =new();
//        public          List<IGameObject[]>GameObjects=new();
//        public World(){
//            this.Scene1 = new Scene1();
//            new LightObject().Create()
//            this.Add(new LightObject());
//            this.Add(new CameraObject());
//            this.Add(new CubeObject());            //    this.Scene1=new Scene1{
//            //    new LightObject(),
//            //    new CameraObject()
//            //};
//        }

//        public void Add(LightObject  Value){
//            throw new NotImplementedException();
//        }
//        public void Add(CameraObject Value){
//            throw new NotImplementedException();
//        }

//        //public void Add<T0>(Archtype<T0> Value) where T0 : unmanaged {
//        //    throw new NotImplementedException();
//        //}
//        //public void Add<T0, T1>(Archtype<T0,T1> Value) where T0 : unmanaged where T1 : unmanaged {
//        //    throw new NotImplementedException();
//        //}

//        public IEnumerator<IGameObject> GetEnumerator(){
//            throw new NotImplementedException();
//        }

//        IEnumerator IEnumerable.        GetEnumerator(){
//            return this.GetEnumerator();
//        }

//    }
//}

