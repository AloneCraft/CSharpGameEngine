using System.Reflection;
using System.Runtime.Loader;
using CSharpGameEngine.Cooking;

namespace CSharpGameEngine.Cook.Cli;

/// <summary>
/// Loads a plugin's Cook module and runs its cookers (ADR-0079). The DLL and its dependencies load into their own collectible
/// context; the contract assembly is taken from this host so that <see cref="IAssetCooker"/> is the same type on both sides.
/// </summary>
internal sealed class CookerHost:IDisposable {
    /// <summary>A cooker reported failure. <see cref="Messages"/> are its own lines, printed as they are so that a build can read them as diagnostics.</summary>
    internal sealed class CookFailedException(string CookerName,IReadOnlyList<string>Messages):InvalidOperationException($"The cooker {CookerName} failed: {string.Join(" ",Messages)}") {
        internal IReadOnlyList<string> Messages { get; }=Messages;
    }

    private readonly PluginModuleLoadContext Context;

    private CookerHost(PluginModuleLoadContext Context,IReadOnlyList<IAssetCooker> Cookers){
        this.Context=Context;
        this.Cookers=Cookers;
    }

    internal IReadOnlyList<IAssetCooker> Cookers { get; }

    internal static CookerHost Load(string ModulePath){
        var FullPath=Path.GetFullPath(ModulePath);
        if(!File.Exists(FullPath)){ throw new FileNotFoundException($"The Cook module was not found: {FullPath}",FullPath); }
        var Context=new PluginModuleLoadContext(FullPath);
        var Assembly=Context.LoadFromAssemblyPath(FullPath);
        List<IAssetCooker> Cookers=[];
        foreach(var Type in Assembly.GetExportedTypes().OrderBy(static Type=>Type.FullName,StringComparer.Ordinal)){
            if(Type.IsAbstract||!typeof(IAssetCooker).IsAssignableFrom(Type)){ continue; }
            if(Type.GetConstructor(Type.EmptyTypes) is null){
                throw new InvalidOperationException($"The cooker {Type.FullName} needs a public constructor without parameters.");
            }
            Cookers.Add((IAssetCooker)Activator.CreateInstance(Type)!);
        }
        return new CookerHost(Context,Cookers);
    }

    /// <summary>The cooker named <paramref name="Name"/>, or the only one that reads the input's extension.</summary>
    internal IAssetCooker Select(string InputPath,string?Name){
        if(Name is not null){
            return this.Cookers.FirstOrDefault(Cooker=>string.Equals(Cooker.Name,Name,StringComparison.Ordinal))
                ??throw new InvalidOperationException($"The module has no cooker named '{Name}' (it has: {string.Join(", ",this.Cookers.Select(static Cooker=>Cooker.Name))}).");
        }
        var Extension=Path.GetExtension(InputPath).ToLowerInvariant();
        var Matching=this.Cookers.Where(Cooker=>Cooker.InputExtensions.Contains(Extension,StringComparer.OrdinalIgnoreCase)).ToArray();
        return Matching.Length switch{
            1=>Matching[0],
            0=>throw new InvalidOperationException($"No cooker of the module reads '{Extension}' files."),
            _=>throw new InvalidOperationException($"Several cookers read '{Extension}' files ({string.Join(", ",Matching.Select(static Cooker=>Cooker.Name))}); choose one with --cooker."),
        };
    }

    /// <summary>
    /// Cooks <paramref name="InputPath"/> into <paramref name="OutputDirectory"/>. With <paramref name="Verify"/> it cooks twice into
    /// separate folders first and fails unless both wrote the same files with the same bytes (a cooker must be deterministic).
    /// Returns the written files, relative to <paramref name="OutputDirectory"/>; the messages a successful cooker reported (warnings, statistics) go to <paramref name="Messages"/>.
    /// </summary>
    internal static IReadOnlyList<string> Cook(IAssetCooker Cooker,string InputPath,string OutputDirectory,string ProjectDirectory,bool Verify,IList<string>?Messages=null){
        var Input=Path.GetFullPath(InputPath);
        if(!File.Exists(Input)){ throw new FileNotFoundException($"The input was not found: {Input}",Input); }
        var Scratch=Path.Combine(Path.GetTempPath(),"cge-cook-"+Guid.NewGuid().ToString("N"));
        try {
            var First=CookInto(Cooker,Input,Path.Combine(Scratch,"1"),ProjectDirectory,Messages);
            if(Verify){
                var Second=CookInto(Cooker,Input,Path.Combine(Scratch,"2"),ProjectDirectory,null);
                if(!First.SequenceEqual(Second,StringComparer.Ordinal)){
                    throw new InvalidOperationException($"The cooker {Cooker.Name} is not deterministic: two runs wrote different files ({string.Join(", ",First)} / {string.Join(", ",Second)}).");
                }
                foreach(var Output in First){
                    if(!File.ReadAllBytes(Path.Combine(Scratch,"1",Output)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(Scratch,"2",Output)))){
                        throw new InvalidOperationException($"The cooker {Cooker.Name} is not deterministic: two runs wrote different bytes to {Output}.");
                    }
                }
            }
            Directory.CreateDirectory(OutputDirectory);
            foreach(var Output in First){
                var Destination=Path.Combine(OutputDirectory,Output);
                Directory.CreateDirectory(Path.GetDirectoryName(Destination)!);
                File.Copy(Path.Combine(Scratch,"1",Output),Destination,overwrite:true);
            }
            return First;
        } finally {
            if(Directory.Exists(Scratch)){ Directory.Delete(Scratch,recursive:true); }
        }
    }

    private static IReadOnlyList<string> CookInto(IAssetCooker Cooker,string Input,string Directory,string ProjectDirectory,IList<string>?Messages){
        System.IO.Directory.CreateDirectory(Directory);
        var Result=Cooker.Cook(new CookRequest(Input,Directory,Path.GetFullPath(ProjectDirectory)));
        if(!Result.Succeeded){
            throw new CookFailedException(Cooker.Name,Result.Messages);
        }
        if(Messages is not null){
            foreach(var Message in Result.Messages){ Messages.Add(Message); }
        }
        var Root=Path.GetFullPath(Directory)+Path.DirectorySeparatorChar;
        List<string> Outputs=[];
        foreach(var Output in Result.Outputs){
            var Full=Path.GetFullPath(Path.Combine(Directory,Output));
            if(Path.IsPathRooted(Output)||!Full.StartsWith(Root,StringComparison.OrdinalIgnoreCase)){
                throw new InvalidOperationException($"The cooker {Cooker.Name} reported an output outside its output folder: {Output}");
            }
            if(!File.Exists(Full)){
                throw new InvalidOperationException($"The cooker {Cooker.Name} reported an output it did not write: {Output}");
            }
            Outputs.Add(Full.Substring(Root.Length));
        }
        Outputs.Sort(StringComparer.Ordinal);
        return Outputs;
    }

    public void Dispose()=>this.Context.Unload();

}
