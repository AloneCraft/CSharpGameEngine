using System.Collections.Immutable;
using CSharpGameEngine.Content;
using CSharpGameEngine.Content.Models;
using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.ContentCompiler;
public static class ModelContentExporter {
    public static async Task<ImmutableArray<string>>ExportAsync(string SourceDirectory,string DestinationDirectory,CancellationToken CancellationToken=default){
        var IndexPath=Path.Combine(Path.GetFullPath(SourceDirectory),"content-index.json");
        if(new FileInfo(IndexPath).Length>16*1024*1024){ throw new InvalidDataException("Model export index exceeds its bound."); }
        var Bytes=await File.ReadAllBytesAsync(IndexPath,CancellationToken);var Index=ModelContentIndex.Read(Bytes);
        var Source=new ContentAddressedStore(SourceDirectory);
        var Roots=Index.Entries.Where(static Entry=>Entry.Kind==ContentArtifactKind.Model).Select(Reference).ToArray();
        var Files=ImmutableArray<string>.Empty;
        if(Index.Entries.Length!=0){
            var Manifest=await ShippingContentManifestBuilder.BuildAsync(new ModelArtifactReader(Source,Index),Index,Roots,CancellationToken);
            if(Manifest.Entries.Length!=Index.Entries.Length){ throw new InvalidDataException("Model export index contains artifacts outside its exact root dependency closure."); }
            Files=await Source.ExportAsync(Manifest.Entries.Select(static Entry=>new PublishedContentArtifact(Entry.Reference,Entry.Address)),DestinationDirectory,CancellationToken);
        }
        var Output=Path.Combine(Path.GetFullPath(DestinationDirectory),"content-index.json");Directory.CreateDirectory(Path.GetDirectoryName(Output)!);
        if(File.Exists(Output)&&(await File.ReadAllBytesAsync(Output,CancellationToken)).AsSpan().SequenceEqual(Bytes)){ return Files.Add(Output); }
        var Temporary=Output+"."+Guid.NewGuid().ToString("N")+".tmp";
        try { await File.WriteAllBytesAsync(Temporary,Bytes,CancellationToken);CancellationToken.ThrowIfCancellationRequested();File.Move(Temporary,Output,true); }
        finally{ if(File.Exists(Temporary)){ File.Delete(Temporary); } }
        return Files.Add(Output);
    }
    private static AssetArtifactReference Reference(ModelContentEntry Entry){
        _=AssetUri.TryParse(Entry.Uri,out var Uri,out _);return new(Uri.Key,Hash256.Parse(Entry.Hash));
    }
}
