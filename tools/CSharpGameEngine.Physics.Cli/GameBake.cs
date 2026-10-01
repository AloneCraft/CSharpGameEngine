using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;

namespace CSharpGameEngine.Physics.Cli;

/// <summary>
/// M0096 G: runs a game's generated <c>GeneratedSceneBakeHost.TryBake</c> from the game's own build. The host does
/// not reference a game: the build is loaded into a collectible context of its own, as the preview host's runtime
/// view does, so each Bake sees that build's code and statics and the resident host can bake another game next.
/// </summary>
internal static class GameBake {
    private const string BakeHostTypeName="CSharpGameEngine.Generated.GeneratedSceneBakeHost";
    private const string ContentRootTypeName="CSharpGameEngine.Entities.ContentRoot";

    internal static bool TryBake(
        string GameAssembly,string SceneName,int TickCount,string DependencyHash,Stream Output,
        CancellationToken CancellationToken,out string Error){
        string Directory=Path.GetDirectoryName(GameAssembly)!;
        GameLoadContext Context=new(Directory);
        try {
            Assembly Game;
            try { Game=Context.LoadFromAssemblyPath(GameAssembly); }
            catch(Exception Exception)when(Exception is FileLoadException or BadImageFormatException or FileNotFoundException){
                Error=$"The game assembly could not be loaded: {Exception.Message}";
                return false;
            }
            if(Game.GetType(BakeHostTypeName,throwOnError:false)?.GetMethod("TryBake",BindingFlags.Public|BindingFlags.Static)
                is not { }TryBakeMethod){
                Error=$"The game has no generated scene Bake host: {GameAssembly}";
                return false;
            }
            // The game resolves its meshes and collision against its content root, which defaults to this process's
            // directory; the bake reads them from the game's build, as the runtime view does.
            if(Game.GetType(ContentRootTypeName,throwOnError:false)?.GetMethod("Use",[typeof(string)]) is { }UseContentRoot){
                Invoke(UseContentRoot,[Directory]);
            }
            object?[]Arguments=[SceneName,TickCount,DependencyHash,Output,CancellationToken,null];
            bool Succeeded=(bool)Invoke(TryBakeMethod,Arguments)!;
            Error=Arguments[5] as string??string.Empty;
            return Succeeded;
        }
        finally { Context.Unload(); }
    }

    private static object?Invoke(MethodInfo Method,object?[]Arguments){
        try { return Method.Invoke(null,Arguments); }
        catch(TargetInvocationException Error)when(Error.InnerException is not null){
            ExceptionDispatchInfo.Capture(Error.InnerException).Throw();
            throw;
        }
    }

    /// <summary>Resolves the game's dependencies from its own directory; the framework comes from the default context.</summary>
    private sealed class GameLoadContext(string Directory):AssemblyLoadContext("CSharpGameEngine.PhysicsBake",isCollectible:true) {
        protected override Assembly?Load(AssemblyName Name){
            string Candidate=Path.Combine(Directory,Name.Name+".dll");
            return File.Exists(Candidate)?this.LoadFromAssemblyPath(Candidate):null;
        }
        protected override nint LoadUnmanagedDll(string UnmanagedDllName){
            string Candidate=Path.Combine(Directory,UnmanagedDllName.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)?UnmanagedDllName:UnmanagedDllName+".dll");
            return File.Exists(Candidate)?this.LoadUnmanagedDllFromPath(Candidate):0;
        }
    }
}
