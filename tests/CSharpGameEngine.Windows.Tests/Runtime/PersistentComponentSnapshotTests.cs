using System.Reflection;
using CSharpGameEngine.Entities;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>
/// Audit AUD-001 left the snapshot round trip of every persistent Component unverified; the typed lights were the ones
/// it found failing. This test takes every persistent Component of this compilation's generated World from its snapshot
/// archetype descriptors and round-trips its default value through the codec.
/// </summary>
public sealed class PersistentComponentSnapshotTests {
    [Fact]
    public void EveryPersistentComponent_RoundTripsItsDefaultValue(){
        var Descriptors=typeof(global::CSharpGameEngine.Entities.World)
            .GetFields(BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)
            .Where(static Field=>Field.FieldType==typeof(EcsSnapshotArchetypeDescriptor))
            .Select(static Field=>(EcsSnapshotArchetypeDescriptor)Field.GetValue(null)!)
            .ToArray();
        Assert.NotEmpty(Descriptors);
        var TypeIds=Descriptors.SelectMany(static Archetype=>Archetype.Components)
            .Where(static Component=>!Component.Transient)
            .Select(static Component=>Component.TypeId)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var Types=AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(static Assembly=>{ try { return Assembly.GetTypes(); } catch(ReflectionTypeLoadException Error){ return Error.Types.OfType<Type>(); } })
            .Where(static Type=>Type.IsValueType&&Type.FullName is not null)
            .GroupBy(static Type=>Type.FullName!,StringComparer.Ordinal)
            .ToDictionary(static Group=>Group.Key,static Group=>Group.First(),StringComparer.Ordinal);
        var RoundTrip=typeof(PersistentComponentSnapshotTests).GetMethod(nameof(RoundTripFailure),BindingFlags.Static|BindingFlags.NonPublic)!;

        var Failures=new List<string>();
        foreach(var TypeId in TypeIds){
            if(!Types.TryGetValue(TypeId,out var Type)){
                Failures.Add($"{TypeId}: type not found");
                continue;
            }
            if(RoundTrip.MakeGenericMethod(Type).Invoke(null,[Activator.CreateInstance(Type)!]) is string Failure){
                Failures.Add($"{TypeId}: {Failure}");
            }
        }

        Assert.True(TypeIds.Length>10,$"only {TypeIds.Length} persistent Components were found");
        Assert.True(Failures.Count==0,string.Join(Environment.NewLine,Failures));
    }

    private static string?RoundTripFailure<T>(T Value)where T:struct{
        byte[]Payload;
        try { Payload=EcsSnapshotComponentCodec<T>.Serialize(Value); }
        catch(Exception Error){ return "serialize: "+Error.Message; }
        if(!EcsSnapshotComponentCodec<T>.TryDeserialize(Payload,out var Restored,out var Message)){ return Message; }
        return EqualityComparer<T>.Default.Equals(Value,Restored)?null:"restored value differs";
    }
}
