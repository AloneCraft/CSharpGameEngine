using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
namespace CSharpGameEngine.ShaderArtifacts;
public static class ShaderArtifactSerializer {
    private const int CurrentVersion=1;
    private const int MaxEntryCount = 1_024;
    private const int MaxTextByteLength = 256;
    private const int MaxBlobByteLength = 16_777_216;
    private const int MaxPayloadByteLength = 32_000_000;
    private const int ChecksumSize = 32;
    private const int MinPayloadBytes=8+sizeof(int)+1;
    private const int MaxFileByteLength = MaxPayloadByteLength + ChecksumSize;
    private const int MinFileByteLength = MinPayloadBytes + ChecksumSize;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("CGESHDR1");
    public static void Write(Stream Output, in ShaderArtifactDocument Document, out string Error) {
        Error = string.Empty;
        try {
            if (Output is null) {
                throw new ArgumentNullException(nameof(Output));
            }
            if (!ValidateDocument(in Document, out Error)) {
                return;
            }
            using var OutputBuffer = new MemoryStream();
            using var Writer = new BinaryWriter(OutputBuffer, Encoding.UTF8, true);
            Writer.Write(Magic);
            Writer.Write(CurrentVersion);
            Writer.Write((byte)Document.Target);
            WriteString(Writer, Document.AbiHash);
            WriteString(Writer, Document.SemanticHash);
            WriteString(Writer, Document.DependencyHash);
            WriteString(Writer, Document.CompilerProvenance.CompilerName);
            WriteString(Writer, Document.CompilerProvenance.CompilerVersion);
            WriteString(Writer, Document.CompilerProvenance.DxcVersion);
            List<ShaderArtifactEntry> SortedEntries = [.. Document.Entries.OrderBy(static Entry => Entry.Stage).ThenBy(static Entry => Entry.LogicalName, StringComparer.Ordinal).ThenBy(static Entry => Entry.Profile, StringComparer.Ordinal)];
            HashSet<string> Deduplicate = new(StringComparer.Ordinal);
            foreach (ShaderArtifactEntry Entry in SortedEntries) {
                string Key = ((byte)Entry.Stage) + "|" + Entry.LogicalName + "|" + Entry.Profile;
                if (!Deduplicate.Add(Key)) {
                    Error = "Duplicate entry.";
                    return;
                }
            }
            Writer.Write(SortedEntries.Count);
            foreach (ShaderArtifactEntry Entry in SortedEntries) {
                WriteString(Writer, Entry.LogicalName);
                WriteString(Writer, Entry.Profile);
                Writer.Write((byte)Entry.Stage);
                WriteBytes(Writer, Entry.Dxil);
                WriteBytes(Writer, Entry.Reflection);
                WriteString(Writer, Entry.PsoKey);
            }
            if (OutputBuffer.Length > MaxPayloadByteLength) {
                Error = "Payload is too large.";
                return;
            }
            byte[] Checksum;
            using (SHA256 Hash = SHA256.Create()) {
                Checksum = Hash.ComputeHash(OutputBuffer.GetBuffer(), 0, (int)OutputBuffer.Length);
            }
            Writer.Write(Checksum);
            if (OutputBuffer.Length > MaxFileByteLength) {
                Error = "Payload is too large.";
                return;
            }
            Output.Write(OutputBuffer.GetBuffer(), 0, (int)OutputBuffer.Length);
        }
        catch (Exception Exception) {
            Error = Exception.Message;
        }
    }
    public static bool TryRead(Stream Input,ShaderArtifactTargetKind ExpectedTarget,string ExpectedAbiHash, out ShaderArtifactDocument? Document, out string Error) {
        Error = string.Empty;
        Document = null;
        try {
            if (Input is null) {
                throw new ArgumentNullException(nameof(Input));
            }
            using var Source = ReadAll(Input, MaxFileByteLength);
            if (Source.Length > MaxFileByteLength) {
                Error = "File is too large.";
                return false;
            }
            if (Source.Length < MinFileByteLength) {
                Error = "File is truncated or malformed.";
                return false;
            }
            int DataLength = (int)Source.Length - ChecksumSize;
            byte[] Buffer = Source.GetBuffer();
            byte[] Payload = new byte[DataLength];
            byte[] StoredChecksum = new byte[ChecksumSize];
            Array.Copy(Buffer, 0, Payload, 0, DataLength);
            Array.Copy(Buffer, DataLength, StoredChecksum, 0, ChecksumSize);
            ShaderArtifactDocument Parsed;
            using (var PayloadReader = new BinaryReader(new MemoryStream(Payload, false), Encoding.UTF8, false)) {
                byte[] ReadMagic = PayloadReader.ReadBytes(Magic.Length);
                if (!ReadMagic.SequenceEqual(Magic)) {
                    Error = "Magic is invalid.";
                    return false;
                }
                int Version = PayloadReader.ReadInt32();
                if (Version != CurrentVersion) {
                    Error = "Unsupported version.";
                    return false;
                }
                byte TargetValue=PayloadReader.ReadByte();
                if(!Enum.IsDefined(typeof(ShaderArtifactTargetKind),TargetValue)||TargetValue==(byte)ShaderArtifactTargetKind.Unknown) {
                    Error="Target contains unsupported value.";
                    return false;
                }
                var Target=(ShaderArtifactTargetKind)TargetValue;
                if(Target!=ExpectedTarget) {
                    Error = "Target does not match requested target.";
                    return false;
                }
                string AbiHash = ReadString(PayloadReader);
                if (!string.Equals(AbiHash, ExpectedAbiHash, StringComparison.Ordinal)) {
                    Error = "ABI hash does not match request.";
                    return false;
                }
                string SemanticHash = ReadString(PayloadReader);
                string DependencyHash = ReadString(PayloadReader);
                string CompilerName = ReadString(PayloadReader);
                string CompilerVersion = ReadString(PayloadReader);
                string DxcVersion = ReadString(PayloadReader);
                int EntryCount = PayloadReader.ReadInt32();
                if (EntryCount < 0 || EntryCount > MaxEntryCount) {
                    Error = "Entry count is out of range.";
                    return false;
                }
                List<ShaderArtifactEntry> Entries = new(EntryCount);
                HashSet<string> Deduplicate = new(StringComparer.Ordinal);
                for (int Index = 0; Index < EntryCount; Index++) {
                    string LogicalName = ReadString(PayloadReader);
                    string Profile = ReadString(PayloadReader);
                    ShaderArtifactEntryStage EntryStage = (ShaderArtifactEntryStage)PayloadReader.ReadByte();
                    if (!Enum.IsDefined(typeof(ShaderArtifactEntryStage), (byte)EntryStage) || EntryStage == ShaderArtifactEntryStage.Unknown) {
                        Error = "Entry stage is invalid.";
                        return false;
                    }
                    byte[] Dxil = ReadBytes(PayloadReader);
                    byte[] Reflection = ReadBytes(PayloadReader);
                    string PsoKey = ReadString(PayloadReader);
                    string Key = ((byte)EntryStage) + "|" + LogicalName + "|" + Profile;
                    if (!Deduplicate.Add(Key)) {
                        Error = "Duplicate entry.";
                        return false;
                    }
                    Entries.Add(new ShaderArtifactEntry {
                        LogicalName = LogicalName,
                        Profile = Profile,
                        Stage = EntryStage,
                        Dxil = Dxil,
                        Reflection = Reflection,
                        PsoKey = PsoKey,
                    });
                }
                if (PayloadReader.BaseStream.Position != PayloadReader.BaseStream.Length) {
                    Error = "Trailing bytes remain in payload.";
                    return false;
                }
                Parsed = new ShaderArtifactDocument {
                    Target = Target,
                    AbiHash = AbiHash,
                    SemanticHash = SemanticHash,
                    DependencyHash = DependencyHash,
                    CompilerProvenance = new ShaderCompilerProvenance(CompilerName, CompilerVersion, DxcVersion),
                    Entries = Entries,
                };
            }
            byte[] ActualChecksum;
            using (SHA256 Hash = SHA256.Create()) {
                ActualChecksum = Hash.ComputeHash(Payload);
            }
            if (!StoredChecksum.SequenceEqual(ActualChecksum)) {
                Error = "Checksum mismatch.";
                return false;
            }
            Document = Parsed;
            return true;
        }
        catch (Exception Exception) when (Exception is EndOfStreamException or IOException or FormatException or InvalidDataException) {
            Error = "File is truncated or malformed.";
            return false;
        }
    }
    private static bool ValidateDocument(in ShaderArtifactDocument Document, out string Error) {
        Error = string.Empty;
        if(Document.Target==ShaderArtifactTargetKind.Unknown||!Enum.IsDefined(typeof(ShaderArtifactTargetKind),Document.Target)) {
            Error = "Target has unknown value.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(Document.AbiHash) || string.IsNullOrWhiteSpace(Document.SemanticHash) || string.IsNullOrWhiteSpace(Document.DependencyHash)) {
            Error = "Metadata is invalid.";
            return false;
        }
        if (Encoding.UTF8.GetByteCount(Document.AbiHash) > MaxTextByteLength || Encoding.UTF8.GetByteCount(Document.SemanticHash) > MaxTextByteLength || Encoding.UTF8.GetByteCount(Document.DependencyHash) > MaxTextByteLength) {
            Error = "Metadata is too long.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(Document.CompilerProvenance.CompilerName) || string.IsNullOrWhiteSpace(Document.CompilerProvenance.CompilerVersion) || string.IsNullOrWhiteSpace(Document.CompilerProvenance.DxcVersion)) {
            Error = "Compiler provenance is invalid.";
            return false;
        }
        if (Encoding.UTF8.GetByteCount(Document.CompilerProvenance.CompilerName) > MaxTextByteLength || Encoding.UTF8.GetByteCount(Document.CompilerProvenance.CompilerVersion) > MaxTextByteLength || Encoding.UTF8.GetByteCount(Document.CompilerProvenance.DxcVersion) > MaxTextByteLength) {
            Error = "Compiler provenance is too long.";
            return false;
        }
        if (Document.Entries is null) {
            Error = "Entries are required.";
            return false;
        }
        if (Document.Entries.Count > MaxEntryCount) {
            Error = "Too many entries.";
            return false;
        }
        HashSet<string> Deduplicate = new(StringComparer.Ordinal);
        foreach (ShaderArtifactEntry Entry in Document.Entries) {
            if (string.IsNullOrWhiteSpace(Entry.LogicalName) || string.IsNullOrWhiteSpace(Entry.Profile) || string.IsNullOrWhiteSpace(Entry.PsoKey) || Entry.Stage == ShaderArtifactEntryStage.Unknown) {
                Error = "Entry metadata is invalid.";
                return false;
            }
            if (Encoding.UTF8.GetByteCount(Entry.LogicalName) > MaxTextByteLength || Encoding.UTF8.GetByteCount(Entry.Profile) > MaxTextByteLength || Encoding.UTF8.GetByteCount(Entry.PsoKey) > MaxTextByteLength) {
                Error = "Entry metadata is invalid.";
                return false;
            }
            if (Entry.Dxil is null || Entry.Reflection is null) {
                Error = "Entry payload is missing.";
                return false;
            }
            if (Entry.Dxil.Length > MaxBlobByteLength || Entry.Reflection.Length > MaxBlobByteLength) {
                Error = "Entry payload too large.";
                return false;
            }
            string Key = ((byte)Entry.Stage) + "|" + Entry.LogicalName + "|" + Entry.Profile;
            if (!Deduplicate.Add(Key)) {
                Error = "Duplicate entry.";
                return false;
            }
        }
        return true;
    }
    private static void WriteString(BinaryWriter Writer, string Text) {
        byte[] Bytes = Encoding.UTF8.GetBytes(Text);
        if (Bytes.Length > MaxTextByteLength) {
            throw new InvalidDataException();
        }
        Writer.Write(Bytes.Length);
        Writer.Write(Bytes);
    }
    private static string ReadString(BinaryReader Reader) {
        int ByteCount = Reader.ReadInt32();
        if (ByteCount is < 0 || ByteCount > MaxTextByteLength) {
            throw new InvalidDataException();
        }
        byte[] Bytes = Reader.ReadBytes(ByteCount);
        if (Bytes.Length != ByteCount) {
            throw new EndOfStreamException();
        }
        return Encoding.UTF8.GetString(Bytes);
    }
    private static void WriteBytes(BinaryWriter Writer, byte[] Data) {
        if (Data.Length > MaxBlobByteLength) {
            throw new InvalidDataException();
        }
        Writer.Write(Data.Length);
        Writer.Write(Data);
    }
    private static byte[] ReadBytes(BinaryReader Reader) {
        int ByteCount = Reader.ReadInt32();
        if (ByteCount is < 0 || ByteCount > MaxBlobByteLength) {
            throw new InvalidDataException();
        }
        byte[] Data = Reader.ReadBytes(ByteCount);
        if (Data.Length != ByteCount) {
            throw new EndOfStreamException();
        }
        return Data;
    }
    private static MemoryStream ReadAll(Stream Input, int MaxBytes) {
        MemoryStream Destination = new();
        byte[] Buffer = new byte[8192];
        while (true) {
            int Read = Input.Read(Buffer, 0, Buffer.Length);
            if (Read <= 0) {
                break;
            }
            Destination.Write(Buffer, 0, Read);
            if (Destination.Length > MaxBytes) {
                throw new InvalidDataException("File is too large.");
            }
        }
        return Destination;
    }
}