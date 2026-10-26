using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.Content;

public sealed class PackagedContentStore:IContentArtifactReader {
    private const int DefaultMaximumArtifactBytes=512*1024*1024;
    private readonly string RootDirectory;
    private readonly int MaximumArtifactBytes;

    public PackagedContentStore(string RootDirectory,int MaximumArtifactBytes=DefaultMaximumArtifactBytes){
        if(string.IsNullOrWhiteSpace(RootDirectory)){ throw new ArgumentException("A package root directory is required.",nameof(RootDirectory)); }
        if(MaximumArtifactBytes<=0){ throw new ArgumentOutOfRangeException(nameof(MaximumArtifactBytes)); }
        this.RootDirectory=Path.GetFullPath(RootDirectory);
        this.MaximumArtifactBytes=MaximumArtifactBytes;
    }

    public async ValueTask<ReadOnlyMemory<byte>>ReadAsync(
        Hash256 Address,CancellationToken CancellationToken=default){
        if(!Address.IsValid){ throw new ArgumentException("A valid artifact address is required.",nameof(Address)); }
        string PathValue=ShippingContentPackage.GetArtifactPath(this.RootDirectory,Address);
        var Information=new FileInfo(PathValue);
        if(!Information.Exists){ throw new FileNotFoundException("Packaged artifact does not exist.",PathValue); }
        if(Information.Length<=0||Information.Length>this.MaximumArtifactBytes){
            throw new InvalidDataException("Packaged artifact length exceeds supported bounds.");
        }
        byte[]Bytes=await File.ReadAllBytesAsync(PathValue,CancellationToken).ConfigureAwait(false);
        if(Bytes.Length!=Information.Length||ContentArtifactCodec.ComputeAddress(Bytes)!=Address){
            throw new InvalidDataException("Packaged artifact address does not match its bytes.");
        }
        if(!ContentArtifactCodec.TryRead(Bytes,out _,out var Error)){
            throw new InvalidDataException($"Packaged artifact is invalid: {Error.Field}: {Error.Message}");
        }
        return Bytes;
    }
}

/// <summary>厳密に指定された1つのマニフェスト依存関係一式を含む、検証済みの不変なパッケージディレクトリ。</summary>
public sealed class ShippingContentPackage {
    public const string ManifestFileName="content.manifest.json";
    private const int MaximumManifestBytes=64*1024*1024;

    private ShippingContentPackage(string RootDirectory,ShippingContentManifest Manifest,PackagedContentStore Artifacts){
        this.RootDirectory=RootDirectory;
        this.Manifest=Manifest;
        this.Artifacts=Artifacts;
    }

    public string RootDirectory{get;}
    public ShippingContentManifest Manifest{get;}
    public PackagedContentStore Artifacts{get;}

    public static async ValueTask PublishAsync(
        IContentArtifactReader Source,ShippingContentManifest Manifest,
        string OutputDirectory,CancellationToken CancellationToken=default){
        ArgumentNullException.ThrowIfNull(Source);
        ArgumentNullException.ThrowIfNull(Manifest);
        if(string.IsNullOrWhiteSpace(OutputDirectory)){ throw new ArgumentException("An output directory is required.",nameof(OutputDirectory)); }
        string Destination=Path.GetFullPath(OutputDirectory);
        if(Directory.Exists(Destination)||File.Exists(Destination)){
            throw new IOException($"Shipping package output already exists: {Destination}.");
        }
        string Parent=Path.GetDirectoryName(Destination)??throw new ArgumentException("Package output requires a parent directory.",nameof(OutputDirectory));
        Directory.CreateDirectory(Parent);
        string Temporary=Destination+".tmp."+Guid.NewGuid().ToString("N");
        try {
            Directory.CreateDirectory(Temporary);
            byte[]ManifestBytes=ShippingContentManifestCodec.Write(Manifest);
            await WriteNewAsync(Path.Combine(Temporary,ManifestFileName),ManifestBytes,CancellationToken).ConfigureAwait(false);
            var WrittenAddresses=new HashSet<Hash256>();
            foreach(var Entry in Manifest.Entries){
                CancellationToken.ThrowIfCancellationRequested();
                ReadOnlyMemory<byte>Bytes=await Source.ReadAsync(Entry.Address,CancellationToken).ConfigureAwait(false);
                if(!ManifestArtifactValidator.TryValidate(Bytes,Entry,out _,out string Error)){
                    throw new InvalidDataException($"Source artifact {Entry.Reference.Key} does not match the shipping manifest: {Error}");
                }
                if(!WrittenAddresses.Add(Entry.Address)){ continue; }
                string PathValue=GetArtifactPath(Temporary,Entry.Address);
                Directory.CreateDirectory(Path.GetDirectoryName(PathValue)!);
                await WriteNewAsync(PathValue,Bytes,CancellationToken).ConfigureAwait(false);
            }
            _=await OpenAsync(Temporary,CancellationToken).ConfigureAwait(false);
            Directory.Move(Temporary,Destination);
        } finally {
            if(Directory.Exists(Temporary)){ Directory.Delete(Temporary,true); }
        }
    }

    public static async ValueTask<ShippingContentPackage>OpenAsync(
        string RootDirectory,CancellationToken CancellationToken=default){
        if(string.IsNullOrWhiteSpace(RootDirectory)){ throw new ArgumentException("A package root directory is required.",nameof(RootDirectory)); }
        string Root=Path.GetFullPath(RootDirectory);
        if(!Directory.Exists(Root)){ throw new InvalidDataException("Shipping package directory is missing."); }
        string ManifestPath=Path.Combine(Root,ManifestFileName);
        var ManifestInformation=new FileInfo(ManifestPath);
        if(!ManifestInformation.Exists){ throw new InvalidDataException("Shipping package manifest is missing."); }
        if(ManifestInformation.Length<=0||ManifestInformation.Length>MaximumManifestBytes){
            throw new InvalidDataException("Shipping package manifest length exceeds supported bounds.");
        }
        byte[]ManifestBytes=await File.ReadAllBytesAsync(ManifestPath,CancellationToken).ConfigureAwait(false);
        if(!ShippingContentManifestCodec.TryRead(ManifestBytes,out var Manifest,out var ManifestError)||Manifest is null){
            throw new InvalidDataException($"Shipping package manifest is invalid: {ManifestError.Field}: {ManifestError.Message}");
        }
        var Expected=new HashSet<string>(StringComparer.OrdinalIgnoreCase){ Path.GetFullPath(ManifestPath) };
        foreach(var Entry in Manifest.Entries){ Expected.Add(Path.GetFullPath(GetArtifactPath(Root,Entry.Address))); }
        string[]Actual=Directory.EnumerateFiles(Root,"*",SearchOption.AllDirectories).Select(Path.GetFullPath).ToArray();
        if(Actual.Any(PathValue=>(File.GetAttributes(PathValue)&FileAttributes.ReparsePoint)!=0)){
            throw new InvalidDataException("Shipping package cannot contain filesystem reparse points.");
        }
        if(Actual.Length!=Expected.Count||Actual.Any(PathValue=>!Expected.Contains(PathValue))){
            throw new InvalidDataException("Shipping package contains missing or extra files outside its exact manifest closure.");
        }
        var Store=new PackagedContentStore(Root);
        foreach(var Entry in Manifest.Entries){
            ReadOnlyMemory<byte>Bytes;
            try { Bytes=await Store.ReadAsync(Entry.Address,CancellationToken).ConfigureAwait(false); }
            catch(Exception Exception) when(Exception is IOException or InvalidDataException or UnauthorizedAccessException){
                throw new InvalidDataException($"Shipping package artifact {Entry.Reference.Key} cannot be validated.",Exception);
            }
            if(!ManifestArtifactValidator.TryValidate(Bytes,Entry,out _,out string Error)){
                throw new InvalidDataException($"Shipping package artifact {Entry.Reference.Key} does not match the manifest: {Error}");
            }
        }
        return new ShippingContentPackage(Root,Manifest,Store);
    }

    public static string GetArtifactPath(string RootDirectory,Hash256 Address){
        if(string.IsNullOrWhiteSpace(RootDirectory)){ throw new ArgumentException("A package root directory is required.",nameof(RootDirectory)); }
        if(!Address.IsValid){ throw new ArgumentException("A valid artifact address is required.",nameof(Address)); }
        string Text=Address.ToString();
        return Path.Combine(Path.GetFullPath(RootDirectory),"objects",Text.Substring(0,2),Text+".cgea");
    }

    private static async ValueTask WriteNewAsync(
        string PathValue,ReadOnlyMemory<byte>Bytes,CancellationToken CancellationToken){
        await using var Stream=new FileStream(
            PathValue,FileMode.CreateNew,FileAccess.Write,FileShare.None,
            64*1024,FileOptions.Asynchronous|FileOptions.WriteThrough);
        await Stream.WriteAsync(Bytes,CancellationToken).ConfigureAwait(false);
        await Stream.FlushAsync(CancellationToken).ConfigureAwait(false);
    }
}
