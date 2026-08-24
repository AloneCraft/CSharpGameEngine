using System.Security.Cryptography;
using System.Text;

namespace CSharpGameEngine.Runtime.Physics;

public readonly record struct PhysicsCookingInput(
    ReadOnlyMemory<byte> SourceAssetBytes,string AssetGuid,string ImportVersion,
    string Settings,string Scale,string Seed,string CookerVersion,string SchemaVersion,string ScalarVersion);

public static class PhysicsCookingKey {
    public static string Compute(in PhysicsCookingInput Input){
        using IncrementalHash Hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Hash.AppendData(Input.SourceAssetBytes.Span);
        Append(Hash,Input.AssetGuid);
        Append(Hash,Input.ImportVersion);
        Append(Hash,Input.Settings);
        Append(Hash,Input.Scale);
        Append(Hash,Input.Seed);
        Append(Hash,Input.CookerVersion);
        Append(Hash,Input.SchemaVersion);
        Append(Hash,Input.ScalarVersion);
        return Convert.ToHexString(Hash.GetHashAndReset()).ToLowerInvariant();
    }

    public static string GetArtifactDirectory(string RepositoryRoot,string AssetGuid,string Key){
        ArgumentException.ThrowIfNullOrWhiteSpace(RepositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(AssetGuid);
        ArgumentException.ThrowIfNullOrWhiteSpace(Key);
        return Path.Combine(RepositoryRoot,".artifacts","physics",AssetGuid,Key);
    }

    private static void Append(IncrementalHash Hash,string Value){
        byte[] Bytes=Encoding.UTF8.GetBytes(Value??string.Empty);
        Span<byte> Length=stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(Length,Bytes.Length);
        Hash.AppendData(Length);
        Hash.AppendData(Bytes);
    }
}

public readonly record struct FractureFragment(int Index,string MeshArtifact,PhysicsVector3 LocalCenter,PhysicsScalar Mass);
public readonly record struct FractureBond(int FirstFragment,int SecondFragment,PhysicsScalar Strength);

public sealed class PrefractureAsset {
    public required string SourceHash { get;init; }
    public required int Seed { get;init; }
    public required IReadOnlyList<FractureFragment> Fragments { get;init; }
    public required IReadOnlyList<FractureBond> Bonds { get;init; }

    public bool IsDeterministicallyOrdered(){
        for(int Index=1;Index<this.Fragments.Count;Index++){
            if(this.Fragments[Index-1].Index>=this.Fragments[Index].Index){ return false; }
        }
        for(int Index=1;Index<this.Bonds.Count;Index++){
            FractureBond Previous=this.Bonds[Index-1];
            FractureBond Current=this.Bonds[Index];
            if(Previous.FirstFragment>Current.FirstFragment
                ||Previous.FirstFragment==Current.FirstFragment&&Previous.SecondFragment>=Current.SecondFragment){ return false; }
        }
        return true;
    }
}

public readonly record struct FractureBreak(ulong ParentStableId,int BondIndex,int FirstFragment,int SecondFragment);

public sealed class PrefractureRuntime {
    private readonly SortedDictionary<(ulong ParentStableId,int BondIndex),PhysicsScalar> Accumulated=[];

    public void Accumulate(ulong ParentStableId,int BondIndex,PhysicsScalar Impulse){
        (ulong ParentStableId,int BondIndex) Key=(ParentStableId,BondIndex);
        this.Accumulated.TryGetValue(Key,out PhysicsScalar Current);
        this.Accumulated[Key]=Current+Impulse;
    }

    public int Resolve(ulong ParentStableId,PrefractureAsset Asset,List<FractureBreak> Results){
        ArgumentNullException.ThrowIfNull(Asset);
        ArgumentNullException.ThrowIfNull(Results);
        int Start=Results.Count;
        for(int Index=0;Index<Asset.Bonds.Count;Index++){
            FractureBond Bond=Asset.Bonds[Index];
            if(this.Accumulated.TryGetValue((ParentStableId,Index),out PhysicsScalar Total)&&Total>=Bond.Strength){
                Results.Add(new FractureBreak(ParentStableId,Index,Bond.FirstFragment,Bond.SecondFragment));
            }
        }
        return Results.Count-Start;
    }
}
