using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;

namespace CSharpGameEngine.Physics.Cli;

/// <summary>
/// M0096 G：ゲーム自身のビルドにある、生成済み <c>GeneratedSceneBakeHost.TryBake</c> を実行する。ホストはゲームを参照しない。プレビューホストのランタイムビューと同様に、ビルドを独自のアンロード可能なコンテキストに読み込むため、各 Bake でそのビルドのコードと静的データが使われ、常駐ホストは次のゲームも Bake できる。
/// </summary>
internal static class GameBake {
    private const string BakeHostTypeName="CSharpGameEngine.Generated.GeneratedSceneBakeHost";
    private const string ContentRootTypeName="CSharpGameEngine.Entities.ContentRoot";

    internal static bool TryBake(
        string GameAssembly,string SceneName,int TickCount,string DependencyHash,Stream Output,
        CancellationToken CancellationToken,out string Error)=>
        TryRun(GameAssembly,"TryBake",[SceneName,TickCount,DependencyHash,Output,CancellationToken,null],out Error);

    /// <summary>ADR-0078/M0113：ゲーム自身のビルドでベイク済みキャッシュを再生し（生成された <c>TryReplay</c>）、tick ごとにワールドが保持した内容を書き出す。</summary>
    internal static bool TryReplay(
        string GameAssembly,string SceneName,int TickCount,string DependencyHash,Stream Cache,Stream Output,
        CancellationToken CancellationToken,out string Error)=>
        TryRun(GameAssembly,"TryReplay",[SceneName,TickCount,DependencyHash,Cache,Output,CancellationToken,null],out Error);

    private static bool TryRun(string GameAssembly,string MethodName,object?[]Arguments,out string Error){
        string Directory=Path.GetDirectoryName(GameAssembly)!;
        GameLoadContext Context=new(Directory);
        try {
            Assembly Game;
            try { Game=Context.LoadFromAssemblyPath(GameAssembly); }
            catch(Exception Exception)when(Exception is FileLoadException or BadImageFormatException or FileNotFoundException){
                Error=$"The game assembly could not be loaded: {Exception.Message}";
                return false;
            }
            if(Game.GetType(BakeHostTypeName,throwOnError:false)?.GetMethod(MethodName,BindingFlags.Public|BindingFlags.Static)
                is not { }TryBakeMethod){
                Error=$"The game has no generated scene Bake host with {MethodName}: {GameAssembly}";
                return false;
            }
            // ゲームはメッシュとコリジョンを自身のコンテンツルートから解決する。既定では、このプロセスの
            // ディレクトリが既定値である。ベイクでは、ランタイムビューと同じように、ゲームのビルドからメッシュを読み込む。
            if(Game.GetType(ContentRootTypeName,throwOnError:false)?.GetMethod("Use",[typeof(string)]) is { }UseContentRoot){
                Invoke(UseContentRoot,[Directory]);
            }
            bool Succeeded=(bool)Invoke(TryBakeMethod,Arguments)!;
            Error=Arguments[^1] as string??string.Empty;
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

    /// <summary>ゲームの依存関係はゲーム自身のディレクトリから解決し、フレームワークは既定のコンテキストから取得する。</summary>
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
