using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CSharpGameEngine.ShaderArtifacts;
namespace CSharpGameEngine.ShaderArtifacts.Tests;
public sealed class ShaderArtifactSerializerTests {
    [Fact]
    public void Roundtrip_PreservesAllFields() {
        ShaderArtifactDocument Document = CreateSample();
        using MemoryStream OutputStream = new();
        ShaderArtifactSerializer.Write(OutputStream, in Document, out string WriteError);
        Assert.True(string.IsNullOrEmpty(WriteError), WriteError);
        OutputStream.Position = 0;
        bool ReadResult = ShaderArtifactSerializer.TryRead(OutputStream, Document.Target, Document.AbiHash, out ShaderArtifactDocument? ReadDocument, out string ReadError);
        Assert.True(ReadResult, ReadError);
        Assert.NotNull(ReadDocument);
        Assert.Equal(Document.Target, ReadDocument!.Target);
        Assert.Equal(Document.AbiHash, ReadDocument.AbiHash);
        Assert.Equal(Document.SemanticHash, ReadDocument.SemanticHash);
        Assert.Equal(Document.DependencyHash, ReadDocument.DependencyHash);
        Assert.Equal(Document.CompilerProvenance.CompilerName, ReadDocument.CompilerProvenance.CompilerName);
        Assert.Equal(Document.CompilerProvenance.CompilerVersion, ReadDocument.CompilerProvenance.CompilerVersion);
        Assert.Equal(Document.CompilerProvenance.DxcVersion, ReadDocument.CompilerProvenance.DxcVersion);
        List<ShaderArtifactEntry> SortedOriginal = [.. Document.Entries.OrderBy(static Entry => Entry.Stage).ThenBy(static Entry => Entry.LogicalName, System.StringComparer.Ordinal).ThenBy(static Entry => Entry.Profile, System.StringComparer.Ordinal)];
        List<ShaderArtifactEntry> SortedLoaded = [.. ReadDocument.Entries.OrderBy(static Entry => Entry.Stage).ThenBy(static Entry => Entry.LogicalName, System.StringComparer.Ordinal).ThenBy(static Entry => Entry.Profile, System.StringComparer.Ordinal)];
        Assert.Equal(SortedOriginal.Count, SortedLoaded.Count);
        for (int Index = 0; Index < SortedOriginal.Count; Index++) {
            ShaderArtifactEntry Original = SortedOriginal[Index];
            ShaderArtifactEntry Loaded = SortedLoaded[Index];
            Assert.Equal(Original.LogicalName, Loaded.LogicalName);
            Assert.Equal(Original.Profile, Loaded.Profile);
            Assert.Equal(Original.Stage, Loaded.Stage);
            Assert.Equal(Original.PsoKey, Loaded.PsoKey);
            Assert.Equal(Original.Dxil, Loaded.Dxil);
            Assert.Equal(Original.Reflection, Loaded.Reflection);
        }
    }
    [Fact]
    public void Determinism_SortsByStageLogicalNameAndProfile() {
        ShaderArtifactDocument Unordered = CreateSample();
        ShaderArtifactDocument Reversed = new() {
            Target = Unordered.Target,
            AbiHash = Unordered.AbiHash,
            SemanticHash = Unordered.SemanticHash,
            DependencyHash = Unordered.DependencyHash,
            CompilerProvenance = Unordered.CompilerProvenance,
            Entries = [Unordered.Entries[1], Unordered.Entries[0], Unordered.Entries[2]],
        };
        using MemoryStream First = new();
        using MemoryStream Second = new();
        ShaderArtifactSerializer.Write(First, in Unordered, out string FirstWriteError);
        ShaderArtifactSerializer.Write(Second, in Reversed, out string SecondWriteError);
        Assert.True(string.IsNullOrEmpty(FirstWriteError));
        Assert.True(string.IsNullOrEmpty(SecondWriteError));
        Assert.Equal(First.ToArray(), Second.ToArray());
    }
    [Fact]
    public void Corruption_RejectsBadChecksum() {
        ShaderArtifactDocument Document = CreateSample();
        using MemoryStream OutputStream = new();
        ShaderArtifactSerializer.Write(OutputStream, in Document, out string WriteError);
        byte[] Bytes = OutputStream.ToArray();
        Bytes[^1] ^= 0xFF;
        bool ReadResult = ShaderArtifactSerializer.TryRead(new MemoryStream(Bytes), Document.Target, Document.AbiHash, out _, out string ReadError);
        Assert.False(ReadResult);
        Assert.Equal("Checksum mismatch.", ReadError);
    }
    [Fact]
    public void Version_RejectsUnknownVersion() {
        ShaderArtifactDocument Document = CreateSample();
        using MemoryStream OutputStream = new();
        ShaderArtifactSerializer.Write(OutputStream, in Document, out string WriteError);
        byte[] Bytes = OutputStream.ToArray();
        int VersionOffset = "CGESHDR1".Length;
        Bytes[VersionOffset] = 3;
        using SHA256 Hash = SHA256.Create();
        byte[] Payload = new byte[Bytes.Length - 32];
        byte[] Checksum = Hash.ComputeHash(Payload);
        Array.Copy(Checksum, 0, Bytes, Payload.Length, Checksum.Length);
        bool ReadResult = ShaderArtifactSerializer.TryRead(new MemoryStream(Bytes), Document.Target, Document.AbiHash, out _, out string ReadError);
        Assert.False(ReadResult);
        Assert.Equal("Unsupported version.", ReadError);
    }
    [Fact]
    public void TargetAndAbi_RejectsMismatch() {
        ShaderArtifactDocument Document = CreateSample();
        using MemoryStream OutputStream = new();
        ShaderArtifactSerializer.Write(OutputStream, in Document, out string WriteError);
        byte[] Bytes = OutputStream.ToArray();
        bool TargetMismatch = ShaderArtifactSerializer.TryRead(
            new MemoryStream(Bytes),
            ShaderArtifactTargetKind.Model,
            Document.AbiHash,
            out _,
            out string TargetError);
        Assert.False(TargetMismatch);
        Assert.Equal("Target does not match requested target.", TargetError);
        bool AbiMismatch = ShaderArtifactSerializer.TryRead(
            new MemoryStream(Bytes),
            Document.Target,
            "different",
            out _,
            out string AbiError);
        Assert.False(AbiMismatch);
        Assert.Equal("ABI hash does not match request.", AbiError);
    }
    [Fact]
    public void Write_RejectsDuplicateEntries() {
        ShaderArtifactDocument Document = CreateSample();
        System.Collections.Generic.List<ShaderArtifactEntry> Entries = [..Document.Entries, Document.Entries[0]];
        ShaderArtifactDocument Duplicate = new() {
            Target = Document.Target,
            AbiHash = Document.AbiHash,
            SemanticHash = Document.SemanticHash,
            DependencyHash = Document.DependencyHash,
            CompilerProvenance = Document.CompilerProvenance,
            Entries = Entries,
        };
        using MemoryStream OutputStream = new();
        ShaderArtifactSerializer.Write(OutputStream, in Duplicate, out string WriteError);
        Assert.Equal("Duplicate entry.", WriteError);
    }
    [Fact]
    public void Read_RejectsTruncatedPayload() {
        ShaderArtifactDocument Document = CreateSample();
        using MemoryStream OutputStream = new();
        ShaderArtifactSerializer.Write(OutputStream, in Document, out string WriteError);
        byte[] Truncated = OutputStream.ToArray().AsSpan(0, (int)(OutputStream.Length / 2)).ToArray();
        bool ReadResult = ShaderArtifactSerializer.TryRead(new MemoryStream(Truncated), Document.Target, Document.AbiHash, out _, out string ReadError);
        Assert.False(ReadResult);
        Assert.NotEqual(string.Empty, ReadError);
    }
    private static ShaderArtifactDocument CreateSample() {
        return new() {
            Target = ShaderArtifactTargetKind.Stage,
            AbiHash = "abi-123",
            SemanticHash = "sem-456",
            DependencyHash = "dep-789",
            CompilerProvenance = new ShaderCompilerProvenance("dxc", "1.8", "v1"),
            Entries = [
                new ShaderArtifactEntry {
                    LogicalName = "B",
                    Profile = "vs_6_6",
                    Stage = ShaderArtifactEntryStage.Vertex,
                    Dxil = Encoding.UTF8.GetBytes("dxil-b"),
                    Reflection = Encoding.UTF8.GetBytes("refl-b"),
                    PsoKey = "pso-b",
                },
                new ShaderArtifactEntry {
                    LogicalName = "A",
                    Profile = "ps_6_6",
                    Stage = ShaderArtifactEntryStage.Pixel,
                    Dxil = Encoding.UTF8.GetBytes("dxil-a"),
                    Reflection = Encoding.UTF8.GetBytes("refl-a"),
                    PsoKey = "pso-a",
                },
                new ShaderArtifactEntry {
                    LogicalName = "A",
                    Profile = "ps_6_6",
                    Stage = ShaderArtifactEntryStage.Vertex,
                    Dxil = Encoding.UTF8.GetBytes("dxil-c"),
                    Reflection = Encoding.UTF8.GetBytes("refl-c"),
                    PsoKey = "pso-c",
                },
            ]
        };
    }
}
