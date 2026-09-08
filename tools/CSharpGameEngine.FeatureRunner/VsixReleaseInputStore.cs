using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace CSharpGameEngine.FeatureStages;

public sealed record VsixReleaseInputDocument(
    string SchemaVersion,DateTimeOffset CreatedUtc,string Configuration,
    string PackagePath,string PackageSha256,long PackageByteLength,
    string BuildDllPath,string BuildDllSha256,string PackageDllSha256,
    string ExtensionId,string Version,int EntryCount,string ContentClosureSha256,
    string SourcePath,string SourceSha256);

/// <summary>
/// Freezes the one Release VSIX that is allowed to receive runtime observations. Normal
/// build/test output may be regenerated afterward without changing this content-addressed input.
/// </summary>
public static class VsixReleaseInputStore {
    public const string SchemaVersion="csharp-game-engine-vsix-release-input-v1";
    public const string ActiveRelativePath=
        "Docs/Verification/VSIX-Release-Input.json";
    private const string ProductArchiveEntry=
        "vsix/CSharpGameEngine.VSExtension.vsix";
    private static readonly JsonSerializerOptions JsonOptions=
        FeatureRunResultStore.CreateJsonOptions();

    public static bool HasActive(string RepositoryRoot)=>
        File.Exists(Path.Combine(
            Path.GetFullPath(RepositoryRoot),
            ActiveRelativePath.Replace('/',Path.DirectorySeparatorChar)));

    public static VsixReleasePackage ReadActive(string RepositoryRoot){
        string Root=Path.GetFullPath(RepositoryRoot);
        string ActivePath=Path.Combine(
            Root,ActiveRelativePath.Replace('/',Path.DirectorySeparatorChar));
        if(!File.Exists(ActivePath)){
            throw new FileNotFoundException(
                "Frozen VSIX release input is required for Release.",ActivePath);
        }
        VsixReleaseInputDocument Document=JsonSerializer.Deserialize<VsixReleaseInputDocument>(
            File.ReadAllBytes(ActivePath),JsonOptions)
            ??throw new InvalidDataException("Active VSIX release input is empty.");
        if(Document.SchemaVersion!=SchemaVersion
            ||Document.Configuration!="Release"){
            throw new InvalidDataException(
                $"Active VSIX release input must use {SchemaVersion} and Release.");
        }
        VsixReleasePackage Package=VsixReleaseEvidence.InspectPackage(
            Root,Document.PackagePath,Document.BuildDllPath);
        if(Package.PackageSha256!=Document.PackageSha256
            ||Package.PackageByteLength!=Document.PackageByteLength
            ||Package.BuildDllSha256!=Document.BuildDllSha256
            ||Package.PackageDllSha256!=Document.PackageDllSha256
            ||Package.ExtensionId!=Document.ExtensionId
            ||Package.Version!=Document.Version
            ||Package.EntryCount!=Document.EntryCount
            ||Package.ContentClosureSha256!=Document.ContentClosureSha256){
            throw new InvalidDataException(
                "Active VSIX release input identity does not match its frozen package.");
        }
        return Package;
    }

    public static Task<VsixReleaseInputDocument>PublishCurrentAsync(
        string RepositoryRoot,DateTimeOffset CreatedUtc,
        CancellationToken CancellationToken=default){
        string Root=Path.GetFullPath(RepositoryRoot);
        string PackagePath=Path.Combine(
            Root,"tools","CSharpGameEngine.VSExtension","bin","Release",
            "net472","CSharpGameEngine.VSExtension.vsix");
        return PublishAsync(
            Root,PackagePath,File.ReadAllBytes(PackagePath),
            CreatedUtc,CancellationToken);
    }

    public static async Task<VsixReleaseInputDocument>PublishProductArchiveAsync(
        string RepositoryRoot,string ProductArchivePath,
        DateTimeOffset CreatedUtc,
        CancellationToken CancellationToken=default){
        string Root=Path.GetFullPath(RepositoryRoot);
        string ArchivePath=ResolveContained(Root,ProductArchivePath);
        if(!File.Exists(ArchivePath)){
            throw new FileNotFoundException(
                "Product archive does not exist.",ArchivePath);
        }
        byte[]PackageBytes;
        using(var Archive=ZipFile.OpenRead(ArchivePath)){
            ZipArchiveEntry[]Matches=[.. Archive.Entries.Where(Entry=>
                Entry.FullName==ProductArchiveEntry)];
            if(Matches.Length!=1){
                throw new InvalidDataException(
                    $"Product archive must contain exactly one {ProductArchiveEntry}.");
            }
            using Stream Stream=Matches[0].Open();
            using var Buffer=new MemoryStream();
            await Stream.CopyToAsync(Buffer,CancellationToken).ConfigureAwait(false);
            PackageBytes=Buffer.ToArray();
        }
        return await PublishAsync(
            Root,ArchivePath,PackageBytes,CreatedUtc,CancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<VsixReleaseInputDocument>PublishAsync(
        string Root,string SourcePath,byte[]PackageBytes,
        DateTimeOffset CreatedUtc,CancellationToken CancellationToken){
        string PackageHash=Hash(PackageBytes);
        string RelativeDirectory=$".artifacts/VsixReleaseInputs/{PackageHash}";
        string FinalDirectory=Path.Combine(
            Root,RelativeDirectory.Replace('/',Path.DirectorySeparatorChar));
        string PackageRelative=RelativeDirectory
            +"/CSharpGameEngine.VSExtension.vsix";
        string DllRelative=RelativeDirectory
            +"/CSharpGameEngine.VSExtension.dll";
        string TemporaryDirectory=Path.Combine(
            Root,".artifacts","VsixReleaseInputs",
            ".tmp-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TemporaryDirectory);
        try {
            string TemporaryPackage=Path.Combine(
                TemporaryDirectory,"CSharpGameEngine.VSExtension.vsix");
            string TemporaryDll=Path.Combine(
                TemporaryDirectory,"CSharpGameEngine.VSExtension.dll");
            await File.WriteAllBytesAsync(
                TemporaryPackage,PackageBytes,CancellationToken)
                .ConfigureAwait(false);
            byte[]DllBytes=ReadPackageDll(PackageBytes);
            await File.WriteAllBytesAsync(
                TemporaryDll,DllBytes,CancellationToken).ConfigureAwait(false);
            if(Directory.Exists(FinalDirectory)){
                string ExistingPackage=Path.Combine(
                    FinalDirectory,"CSharpGameEngine.VSExtension.vsix");
                string ExistingDll=Path.Combine(
                    FinalDirectory,"CSharpGameEngine.VSExtension.dll");
                if(!File.Exists(ExistingPackage)||!File.Exists(ExistingDll)
                    ||Hash(File.ReadAllBytes(ExistingPackage))!=PackageHash
                    ||Hash(File.ReadAllBytes(ExistingDll))!=Hash(DllBytes)){
                    throw new InvalidDataException(
                        "Content-addressed VSIX release input directory is corrupt.");
                }
            } else {
                Directory.Move(TemporaryDirectory,FinalDirectory);
            }
            VsixReleasePackage Package=VsixReleaseEvidence.InspectPackage(
                Root,PackageRelative,DllRelative);
            string RelativeSource=Path.GetRelativePath(
                Root,SourcePath).Replace('\\','/');
            var Document=new VsixReleaseInputDocument(
                SchemaVersion,CreatedUtc,"Release",Package.PackagePath,
                Package.PackageSha256,Package.PackageByteLength,
                Package.BuildDllPath,Package.BuildDllSha256,
                Package.PackageDllSha256,Package.ExtensionId,Package.Version,
                Package.EntryCount,Package.ContentClosureSha256,
                RelativeSource,Hash(File.ReadAllBytes(SourcePath)));
            string ActivePath=Path.Combine(
                Root,ActiveRelativePath.Replace('/',Path.DirectorySeparatorChar));
            await WriteCanonicalAtomicAsync(
                ActivePath,Document,CancellationToken).ConfigureAwait(false);
            _=ReadActive(Root);
            return Document;
        } finally {
            if(Directory.Exists(TemporaryDirectory)){
                Directory.Delete(TemporaryDirectory,true);
            }
        }
    }

    private static byte[]ReadPackageDll(byte[]PackageBytes){
        using var PackageStream=new MemoryStream(PackageBytes,false);
        using var Package=new ZipArchive(
            PackageStream,ZipArchiveMode.Read,false);
        ZipArchiveEntry[]Entries=[.. Package.Entries.Where(Entry=>
            Entry.FullName.EndsWith(
                "CSharpGameEngine.VSExtension.dll",
                StringComparison.OrdinalIgnoreCase))];
        if(Entries.Length!=1){
            throw new InvalidDataException(
                "VSIX release input must contain exactly one extension DLL.");
        }
        using Stream Stream=Entries[0].Open();
        using var Buffer=new MemoryStream();
        Stream.CopyTo(Buffer);
        return Buffer.ToArray();
    }

    private static string ResolveContained(string Root,string PathValue){
        string FullPath=Path.GetFullPath(Path.IsPathRooted(PathValue)
            ?PathValue:Path.Combine(Root,PathValue));
        string Prefix=Root.TrimEnd(Path.DirectorySeparatorChar)
            +Path.DirectorySeparatorChar;
        if(!FullPath.StartsWith(Prefix,StringComparison.OrdinalIgnoreCase)){
            throw new InvalidDataException(
                "VSIX release input path escapes the repository.");
        }
        return FullPath;
    }
    private static string Hash(byte[]Bytes)=>
        Convert.ToHexString(SHA256.HashData(Bytes)).ToLowerInvariant();
    private static async Task WriteCanonicalAtomicAsync<T>(
        string PathValue,T Value,CancellationToken CancellationToken){
        Directory.CreateDirectory(Path.GetDirectoryName(PathValue)!);
        string Temporary=PathValue+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {
            await File.WriteAllBytesAsync(
                Temporary,JsonSerializer.SerializeToUtf8Bytes(Value,JsonOptions),
                CancellationToken).ConfigureAwait(false);
            File.Move(Temporary,PathValue,true);
        } finally {
            if(File.Exists(Temporary)){ File.Delete(Temporary); }
        }
    }
}
