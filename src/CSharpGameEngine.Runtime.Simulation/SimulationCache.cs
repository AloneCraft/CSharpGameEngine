using System.Buffers.Binary;
using System.Text;

namespace CSharpGameEngine.Runtime.Simulation;

public readonly record struct SimulationCacheIdentity(
    string Domain,string LogicalProfile,int TickRate,string DependencyHash,int SchemaVersion);

public static class SimulationCacheHeader {
    private static ReadOnlySpan<byte> Magic=>"CGESIMC1"u8;

    public static void Write(Stream Output,in SimulationCacheIdentity Identity){
        ArgumentNullException.ThrowIfNull(Output);
        using BinaryWriter Writer=new(Output,Encoding.UTF8,true);
        Writer.Write(Magic);
        Writer.Write(Identity.SchemaVersion);
        Writer.Write(Identity.TickRate);
        Writer.Write(Identity.Domain);
        Writer.Write(Identity.LogicalProfile);
        Writer.Write(Identity.DependencyHash);
    }

    public static bool TryRead(Stream Input,out SimulationCacheIdentity Identity){
        ArgumentNullException.ThrowIfNull(Input);
        Identity=default;
        try {
            Span<byte> MagicBytes=stackalloc byte[Magic.Length];
            if(Input.Read(MagicBytes)!=MagicBytes.Length||!MagicBytes.SequenceEqual(Magic)){
                return false;
            }
            using BinaryReader Reader=new(Input,Encoding.UTF8,true);
            int Schema=Reader.ReadInt32();
            int TickRate=Reader.ReadInt32();
            string Domain=Reader.ReadString();
            string Profile=Reader.ReadString();
            string Hash=Reader.ReadString();
            if(Schema<=0||TickRate<=0||string.IsNullOrWhiteSpace(Domain)||string.IsNullOrWhiteSpace(Profile)||string.IsNullOrWhiteSpace(Hash)){
                return false;
            }
            Identity=new SimulationCacheIdentity(Domain,Profile,TickRate,Hash,Schema);
            return true;
        }
        catch(EndOfStreamException){
            return false;
        }
        catch(IOException){
            return false;
        }
    }
}
