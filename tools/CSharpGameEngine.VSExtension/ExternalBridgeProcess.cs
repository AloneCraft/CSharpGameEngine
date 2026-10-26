using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.VSExtension;

internal sealed class AuthoringHostOperationException:InvalidOperationException {
    internal AuthoringHostOperationException(
        string BridgeIdentity,AuthoringHostResult Result)
        :base($"{BridgeIdentity}: {Result?.Output}"){
        this.Result=Result??throw new ArgumentNullException(nameof(Result));
    }

    internal AuthoringHostResult Result { get; }
}

/// <summary>
/// プロセス外ブリッジを共通起動する処理 (ADR-0010。ADR-0019/ADR-0021/ADR-0023 で一般化)。3 段階のフォールバックを使い、各段階はひとつ前の堅牢性を補う。高速経路が使えるかどうかに正しさが依存することはない。
/// <list type="number">
/// <item>常駐名前付きパイプサーバー (<see cref="ResidentBridgeClient"/>、ADR-0023)：呼び出しごとに OS プロセスを作成して .NET ランタイムを起動するコストをかけず、すでに実行中のブリッジプロセスを再利用する。<c>useResidentServer</c> が <see langword="true"/> で、ビルド済み exe が見つかった場合のみ試す。</item>
/// <item>ブリッジプロジェクトのビルド済み apphost <c>.exe</c> を直接起動する (<see cref="BridgeExecutableLocator"/>)。</item>
/// <item><c>dotnet run --project &lt;bridgeProjectRelativePath&gt; -- &lt;firstArg&gt; &lt;secondArg&gt;</c>：ビルド済み実行ファイルがまだない場合（新規 clone など）に限る。`dotnet run` は最初の 1 回だけでなく、実質的に呼び出しごとに MSBuild 評価コストがかかるため重要である。ADR-0010 では、既知の先送りコスト（「体感速度の最適化...は今回のスコープ外とした」）として挙げられ、シーンタブがメッシュを複数（メッシュごとにブリッジプロセス 1 つ）読み込むようになると、読み込みごとの支配的なコストになった。</item>
/// </list>
/// <see cref="ExternalFbxLoader"/>/<see cref="ExternalGltfLoader"/> からは第 2 引数を JSON パスとして使い、呼び出し元が <see cref="RunAndReadJson"/> で読み戻す。また <see cref="SceneModelRenderObjectBuilder.ResolveViaBridge"/> からは、第 2 引数を <c>.glb</c> パスとして渡し、呼び出し元は後からファイルが存在することだけを <see cref="RunProducingFile"/> で確認する。モデル/メッシュデータを <c>devenv.exe</c> 内のプロセスで生成する必要はない。
/// </summary>
internal static class ExternalBridgeProcess {
    public static string RunAndReadJson(string BridgeProjectRelativePath,string InputPath,string OutputJsonPath,string RepoRoot,TimeSpan Timeout,bool UseResidentServer){
        RunProcess(BridgeProjectRelativePath,InputPath,OutputJsonPath,RepoRoot,Timeout,UseResidentServer);

        if(!File.Exists(OutputJsonPath)){
            throw new InvalidOperationException(VsixText.Format("Bridge_OutputNotGenerated",BridgeProjectRelativePath));
        }

        return File.ReadAllText(OutputJsonPath);
    }

    /// <summary><see cref="RunAndReadJson"/> と同様ですが、バイナリファイル（例：<c>.glb</c>）を書き込む bridge 用です。呼び出し側が別途読み込むため、ここではテキストとして読み戻しません。</summary>
    public static void RunProducingFile(string BridgeProjectRelativePath,string FirstArg,string OutputPath,string RepoRoot,TimeSpan Timeout,bool UseResidentServer){
        RunProcess(BridgeProjectRelativePath,[FirstArg,OutputPath],RepoRoot,Timeout,UseResidentServer);

        if(!File.Exists(OutputPath)){
            throw new InvalidOperationException(VsixText.Format("Bridge_GeneratedOutputNotGenerated",BridgeProjectRelativePath));
        }
    }

    public static void RunCommandProducingFile(string BridgeProjectRelativePath,string[]Arguments,string OutputPath,string RepoRoot,TimeSpan Timeout,bool UseResidentServer){
        if(Arguments is null){ throw new ArgumentNullException(nameof(Arguments)); }
        RunProcess(BridgeProjectRelativePath,Arguments,RepoRoot,Timeout,UseResidentServer);
        if(!File.Exists(OutputPath)){
            throw new InvalidOperationException(VsixText.Format("Bridge_GeneratedOutputNotGenerated",BridgeProjectRelativePath));
        }
    }

    public static void RunExecutableCommandProducingFile(
        string ExecutablePath,string BridgeIdentity,string[]Arguments,string OutputPath,
        string RepoRoot,TimeSpan Timeout,bool UseResidentServer){
        RunExecutableCommandProducingFile(
            ExecutablePath,BridgeIdentity,Arguments,OutputPath,RepoRoot,Timeout,
            UseResidentServer,CancellationToken.None);
    }

    public static void RunExecutableCommandProducingFile(
        string ExecutablePath,string BridgeIdentity,string[]Arguments,string OutputPath,
        string RepoRoot,TimeSpan Timeout,bool UseResidentServer,CancellationToken CancellationToken){
        if(string.IsNullOrWhiteSpace(ExecutablePath)){ throw new ArgumentException(nameof(ExecutablePath)); }
        if(Arguments is null){ throw new ArgumentNullException(nameof(Arguments)); }
        RunProcess(
            BridgeIdentity,Arguments,RepoRoot,Timeout,UseResidentServer,ExecutablePath,
            CancellationToken);
        if(!File.Exists(OutputPath)){
            throw new InvalidOperationException(VsixText.Format("Bridge_GeneratedOutputNotGenerated",BridgeIdentity));
        }
    }

    private static void RunProcess(string BridgeProjectRelativePath,string FirstArg,string OutputPath,string RepoRoot,TimeSpan Timeout,bool UseResidentServer){
        RunProcess(BridgeProjectRelativePath,[FirstArg,OutputPath],RepoRoot,Timeout,UseResidentServer);
    }

    private static void RunProcess(string BridgeProjectRelativePath,string[]Arguments,string RepoRoot,TimeSpan Timeout,bool UseResidentServer){
        var ExecutablePath=BridgeExecutableLocator.FindExecutable(RepoRoot,BridgeProjectRelativePath);
        RunProcess(BridgeProjectRelativePath,Arguments,RepoRoot,Timeout,UseResidentServer,ExecutablePath);
    }

    private static void RunProcess(
        string BridgeProjectRelativePath,string[]Arguments,string RepoRoot,TimeSpan Timeout,
        bool UseResidentServer,string?ExecutablePath){
        RunProcess(
            BridgeProjectRelativePath,Arguments,RepoRoot,Timeout,UseResidentServer,
            ExecutablePath,CancellationToken.None);
    }

    private static void RunProcess(
        string BridgeProjectRelativePath,string[]Arguments,string RepoRoot,TimeSpan Timeout,
        bool UseResidentServer,string?ExecutablePath,CancellationToken CancellationToken){

        CancellationToken.ThrowIfCancellationRequested();
        if(ExecutablePath is not null&&UseResidentServer){
            var Attempt=ResidentBridgeClient.TryRunDetailed(
                ExecutablePath,RepoRoot,BridgeProjectRelativePath,Arguments,Timeout,
                CancellationToken,out AuthoringHostResult?ResidentResult);
            if(Attempt==ResidentBridgeAttemptStatus.Completed){
                if(ResidentResult is null){
                    throw new InvalidOperationException(
                        VsixText.Get("ResidentBridge_EmptyResponse"));
                }
                if(!IsSuccessfulResult(ResidentResult)){
                    throw new AuthoringHostOperationException(
                        BridgeProjectRelativePath,ResidentResult);
                }
                return;
            }
            if(Attempt==ResidentBridgeAttemptStatus.TimedOut){
                var TimeoutSeconds=Timeout.Ticks/(float)TimeSpan.TicksPerSecond;
                throw new TimeoutException(VsixText.Format("Bridge_TimedOut",BridgeProjectRelativePath,TimeoutSeconds.ToString(CultureInfo.InvariantCulture)));
            }
            if(Attempt is ResidentBridgeAttemptStatus.HostCrashed or ResidentBridgeAttemptStatus.ProtocolMismatch){
                if(ResidentResult is not null){
                    throw new AuthoringHostOperationException(
                        BridgeProjectRelativePath,ResidentResult);
                }
                throw new InvalidOperationException(
                    VsixText.Format("Bridge_ExitedWithError",BridgeProjectRelativePath,"1",string.Empty));
            }
        }

        RunOneShotProcess(
            BridgeProjectRelativePath,Arguments,RepoRoot,Timeout,ExecutablePath,
            CancellationToken);
    }

    internal static bool IsSuccessfulResult(AuthoringHostResult Result){
        if(Result is null||Result.State!=AuthoringHostTerminalState.Succeeded
            ||Result.ExitCode!=0){ return false; }
        foreach(AuthoringHostDiagnostic?Diagnostic in Result.Diagnostics
            ??Array.Empty<AuthoringHostDiagnostic>()){
            if(Diagnostic is null
                ||Diagnostic.Severity==AuthoringHostDiagnosticSeverity.Error){
                return false;
            }
        }
        return true;
    }

    private static void RunOneShotProcess(
        string BridgeProjectRelativePath,string[]Arguments,string RepoRoot,TimeSpan Timeout,
        string?ExecutablePath,CancellationToken CancellationToken){
        string CommandArguments=string.Join(" ",Array.ConvertAll(Arguments,QuoteArgument));
        var StartInfo=ExecutablePath is not null
                          ?new ProcessStartInfo(ExecutablePath,CommandArguments)
                          :new ProcessStartInfo(
                                                "dotnet",
                                                $"run --project \"{BridgeProjectRelativePath}\" -- {CommandArguments}");

        StartInfo.WorkingDirectory=RepoRoot;
        StartInfo.UseShellExecute=false;
        StartInfo.RedirectStandardOutput=true;
        StartInfo.RedirectStandardError=true;
        StartInfo.CreateNoWindow=true;

        using Process Process=new(){ StartInfo=StartInfo };

        // イベントベース API（BeginOutputReadLine/BeginErrorReadLine）で両方のストリームを読み取ります。
        // WaitForExit より前に実施します。ここで同期的かつ順番に読み取ると、
        // 先に読む側のストリームが未読のまま書き込みを待っている間に、子プロセスが
        // 後から読む側のストリームに書き込み、OS パイプバッファーを満たすとデッドロックするためです。
        StringBuilder Stdout=new();
        StringBuilder Stderr=new();
        Process.OutputDataReceived+=(_,E)=>{ if(E.Data is not null)Stdout.AppendLine(E.Data);};
        Process.ErrorDataReceived+=(_,E)=>{ if(E.Data is not null)Stderr.AppendLine(E.Data);};

        Process.Start();
        Process.BeginOutputReadLine();
        Process.BeginErrorReadLine();

        var TimeoutMilliseconds=Math.Min(int.MaxValue,Math.Max(0L,Timeout.Ticks/TimeSpan.TicksPerMillisecond));
        Stopwatch Timer=Stopwatch.StartNew();
        bool Exited=false;
        while(!(Exited=Process.WaitForExit(100))
            &&Timer.ElapsedMilliseconds<TimeoutMilliseconds){
            if(!CancellationToken.IsCancellationRequested){ continue; }
            try { Process.Kill();} catch(InvalidOperationException){ }
            CancellationToken.ThrowIfCancellationRequested();
        }
        if(!Exited){
            try { Process.Kill();} catch(InvalidOperationException){ }
            var TimeoutSeconds=Timeout.Ticks/(float)TimeSpan.TicksPerSecond;
            throw new TimeoutException(VsixText.Format("Bridge_TimedOut",BridgeProjectRelativePath,TimeoutSeconds.ToString(CultureInfo.InvariantCulture)));
        }

        if(Process.ExitCode!=0){
            throw new InvalidOperationException(
                VsixText.Format("Bridge_ExitedWithError",BridgeProjectRelativePath,Process.ExitCode.ToString(CultureInfo.InvariantCulture),Stdout+Environment.NewLine+Stderr));
        }
    }

    /// <summary>
    /// Windows コマンドライン用に引用符で囲んだ1つの引数を、CommandLineToArgvW と .NET のパーサーで同じ値として読み戻します。引用符の内側または閉じ引用符の直前にあるバックスラッシュを二重化するため、<c>C:\Out\</c> が閉じ引用符をエスケープして次の引数を取り込むことはありません。
    /// </summary>
    internal static string QuoteArgument(string Argument){
        if(Argument is null){ throw new ArgumentNullException(nameof(Argument)); }
        var Builder=new System.Text.StringBuilder("\"");
        var Backslashes=0;
        foreach(var Character in Argument){
            if(Character=='\\'){ Backslashes++;continue; }
            Builder.Append('\\',Character=='"'?Backslashes*2+1:Backslashes).Append(Character);
            Backslashes=0;
        }
        return Builder.Append('\\',Backslashes*2).Append('"').ToString();
    }
}

/// <summary>
/// bridge プロジェクト用に、すでにビルド済みの最新 apphost <c>.exe</c> があれば検索します。これにより <see cref="ExternalBridgeProcess"/> は `dotnet run` による MSBuild 評価を丸ごと省略できます。bridge プロジェクト（<c>Import.Fbx.Cli</c>/<c>Import.Gltf.Cli</c>/<c>Primitive.Cli</c>）はいずれも <c>&lt;AssemblyName&gt;</c> を明示していないため、出力 <c>.exe</c> 名は <c>bridgeProjectRelativePath</c> の最後のセグメントであるプロジェクトフォルダー名と一致します。<c>RuntimeIdentifier</c> を設定するのは AssimpNet のネイティブ依存関係を持つ <c>Import.Fbx.Cli</c> だけなので、プロジェクト別の特別扱いはせず、RID ありとなしの両方の出力配置を調べます。<c>CSharpGameEngine.VSExtension.Tests</c> が実プロセスを起動せず、<c>InternalsVisibleTo</c> 経由でパス選択ロジックを単体テストできるようにした、トップレベル（入れ子でない）の <c>internal</c> クラスです。
/// </summary>
internal static class BridgeExecutableLocator {
    private static readonly string[]Configurations=["Release","Debug"];

    /// <summary>この拡張機能のフォルダー。VSIX は各 bridge を <c>Bridges/&lt;name&gt;/</c> に同梱します（ADR-0065）。</summary>
    internal static string ExtensionDirectory=>Path.GetDirectoryName(typeof(BridgeExecutableLocator).Assembly.Location)??AppDomain.CurrentDomain.BaseDirectory;

    public static string?FindExecutable(string RepoRoot,string BridgeProjectRelativePath)=>
        FindExecutable(ExtensionDirectory,RepoRoot,BridgeProjectRelativePath);

    /// <summary>
    /// 拡張機能に同梱された bridge があればそれを使用し、なければエンジンリポジトリ内の最新の実行可能なビルドを使用します（開発時のみ）。どちらもない場合は null です。
    /// </summary>
    public static string?FindExecutable(string ExtensionDirectory,string RepoRoot,string BridgeProjectRelativePath){
        var ExeFileName=Path.GetFileName(BridgeProjectRelativePath)+".exe";
        var Bundled=Path.Combine(ExtensionDirectory,"Bridges",Path.GetFileName(BridgeProjectRelativePath),ExeFileName);
        if(File.Exists(Bundled)&&File.Exists(Path.ChangeExtension(Bundled,".runtimeconfig.json"))){
            return Bundled;
        }
        var BinDirectory=Path.Combine(RepoRoot,BridgeProjectRelativePath,"bin");

        string?Best=null;
        var BestWriteTimeUtc=DateTime.MinValue;

        foreach(var Configuration in Configurations){
            string[]Candidates=[
                Path.Combine(BinDirectory,Configuration,"net10.0",ExeFileName),
                Path.Combine(BinDirectory,Configuration,"net10.0","win-x64",ExeFileName),
            ];

            foreach(var Candidate in Candidates){
                // runtimeconfig のない apphost は実行できません。不完全なビルドで残ることがあり、
                // 起動すると誤解を招く hostpolicy.dll エラーが発生します。
                // portable 出力が完成するまでは dotnet run にフォールバックします。
                var RuntimeConfigPath=Path.ChangeExtension(Candidate,".runtimeconfig.json");
                if(!File.Exists(Candidate)||!File.Exists(RuntimeConfigPath)){
                    continue;
                }

                var WriteTimeUtc=File.GetLastWriteTimeUtc(Candidate);
                if(Best is null||WriteTimeUtc>BestWriteTimeUtc){
                    Best=Candidate;
                    BestWriteTimeUtc=WriteTimeUtc;
                }
            }
        }

        return Best;
    }
}
