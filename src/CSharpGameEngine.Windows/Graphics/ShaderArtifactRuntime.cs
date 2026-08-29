using CSharpGameEngine.ShaderArtifacts;
namespace CSharpGameEngine.Graphics.D3D12;
internal sealed class ShaderArtifactPackage {
    private readonly IReadOnlyDictionary<string,ShaderArtifactEntry>Entries;
    public ShaderArtifactPackage(ShaderArtifactDocument Document) {
        this.Document=Document;
        this.Entries=Document.Entries.ToDictionary(Entry=>Entry.LogicalName,StringComparer.Ordinal);
    }
    public ShaderArtifactDocument Document { get; }
    public byte[]GetBytecode(string LogicalName) {
        if(!this.Entries.TryGetValue(LogicalName,out var Entry)) {
            throw new InvalidDataException($"Shader artifact entry '{LogicalName}' is missing.");
        }
        return Entry.Dxil;
    }
}
internal static class ShaderArtifactRuntime {
    public static bool TryLoad(string ShaderArtifactPath,ShaderArtifactTargetKind ExpectedTarget,out ShaderArtifactPackage? Package,out string Error) {
        Package=null;
        Error=string.Empty;
        try {
            using var Stream=new FileStream(ShaderArtifactPath,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
            if(!ShaderArtifactSerializer.TryRead(Stream,ExpectedTarget,ShaderArtifactAbi.GetHash(ExpectedTarget),out var Document,out Error)||Document is null) {
                return false;
            }
            var Contracts=ShaderArtifactAbi.GetEntries(ExpectedTarget);
            if(Document.Entries.Count!=Contracts.Count) {
                Error=$"Shader artifact has {Document.Entries.Count} entries; ABI requires {Contracts.Count}.";
                return false;
            }
            foreach(var Contract in Contracts) {
                var Matches=Document.Entries.Where(Entry=>string.Equals(Entry.LogicalName,Contract.LogicalName,StringComparison.Ordinal)&&string.Equals(Entry.Profile,Contract.Profile,StringComparison.Ordinal)&&Entry.Stage==Contract.Stage).ToArray();
                if(Matches.Length!=1||Matches[0].Dxil.Length==0||Matches[0].Reflection.Length==0) {
                    Error=$"Shader artifact entry '{Contract.LogicalName}' does not match its fixed ABI contract.";
                    return false;
                }
            }
            Package=new ShaderArtifactPackage(Document);
            return true;
        } catch(Exception Exception) when(Exception is IOException or UnauthorizedAccessException or InvalidDataException) {
            Error=Exception.Message;
            return false;
        }
    }
}
internal sealed class ShaderArtifactMonitor:IDisposable {
#if DEBUG
    private readonly FileSystemWatcher Watcher;
    private int ReloadPending;
#endif
    internal static bool IsEnabled {
        get {
#if DEBUG
            return true;
#else
            return false;
#endif
        }
    }
    public ShaderArtifactMonitor(string ShaderArtifactPath) {
#if DEBUG
        var FullPath=Path.GetFullPath(ShaderArtifactPath);
        this.Watcher=new FileSystemWatcher(Path.GetDirectoryName(FullPath)!,Path.GetFileName(FullPath)) {
            NotifyFilter=NotifyFilters.FileName|NotifyFilters.LastWrite|NotifyFilters.CreationTime|NotifyFilters.Size,
        };
        this.Watcher.Changed+=this.OnChanged;
        this.Watcher.Created+=this.OnChanged;
        this.Watcher.Deleted+=this.OnChanged;
        this.Watcher.Renamed+=this.OnRenamed;
        this.Watcher.EnableRaisingEvents=true;
#endif
    }
    public bool ConsumeReloadRequest() {
#if DEBUG
        return Interlocked.Exchange(ref this.ReloadPending,0)!=0;
#else
        return false;
#endif
    }
#if DEBUG
    private void OnChanged(object Sender,FileSystemEventArgs Arguments) {
        _=Interlocked.Exchange(ref this.ReloadPending,1);
    }
    private void OnRenamed(object Sender,RenamedEventArgs Arguments) {
        _=Interlocked.Exchange(ref this.ReloadPending,1);
    }
#endif
    public void Dispose() {
#if DEBUG
        this.Watcher.EnableRaisingEvents=false;
        this.Watcher.Changed-=this.OnChanged;
        this.Watcher.Created-=this.OnChanged;
        this.Watcher.Deleted-=this.OnChanged;
        this.Watcher.Renamed-=this.OnRenamed;
        this.Watcher.Dispose();
#endif
    }
}