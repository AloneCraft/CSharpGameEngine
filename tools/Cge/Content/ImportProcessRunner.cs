using System.Diagnostics;
namespace CSharpGameEngine.ContentCompiler;
public sealed record ImportProcessOutput(int ExitCode,string Output,string Error);
/// <summary>このホストが起動したプロセスのみを所有する。プロバイダーのアセンブリをこのホストへ読み込むことはない。</summary>
public sealed class ImportProcessRunner(string CookToolPath) {
    private readonly string CookToolPath=Path.GetFullPath(CookToolPath);
    public async Task<ImportProcessOutput>RunAsync(IEnumerable<string>Arguments,CancellationToken CancellationToken=default){
        CancellationToken.ThrowIfCancellationRequested();
        var Start=new ProcessStartInfo("dotnet") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true };
        Start.ArgumentList.Add(this.CookToolPath);
        foreach(var Argument in Arguments){ Start.ArgumentList.Add(Argument); }
        using var Process=new Process { StartInfo=Start };
        if(!Process.Start()){ throw new IOException("IMP801: failed to start import worker."); }
        var Output=CaptureAsync(Process.StandardOutput,Process);
        var Error=CaptureAsync(Process.StandardError,Process);
        using var Deadline=CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        Deadline.CancelAfter(TimeSpan.FromSeconds(300));
        try {
            await Process.WaitForExitAsync(Deadline.Token);
            return new(Process.ExitCode,await Output.WaitAsync(Deadline.Token),await Error.WaitAsync(Deadline.Token));
        } catch(OperationCanceledException){
            try {
                await Process.StandardInput.WriteLineAsync("cancel").WaitAsync(TimeSpan.FromMilliseconds(200));
                await Process.StandardInput.FlushAsync().WaitAsync(TimeSpan.FromMilliseconds(200));
                await Process.WaitForExitAsync().WaitAsync(TimeSpan.FromMilliseconds(1500));
            } catch(Exception Failure) when(Failure is IOException or TimeoutException or InvalidOperationException){
                Stop(Process);
            }
            await Process.WaitForExitAsync();
            try { _=await Output;_=await Error; } catch(IOException){ }
            CancellationToken.ThrowIfCancellationRequested();
            throw new IOException("IMP802: import worker exceeded its deadline.");
        } finally {
            Stop(Process);
        }
    }
    private static async Task<string>CaptureAsync(StreamReader Reader,Process Process){
        const int MaximumCharacters=1024*1024;
        var Text=new System.Text.StringBuilder();
        var Buffer=new char[4096];
        while(true){
            var Count=await Reader.ReadAsync(Buffer);
            if(Count==0){ return Text.ToString(); }
            if(Text.Length+Count>MaximumCharacters){ Stop(Process);throw new IOException("IMP803: import worker output exceeds its bound."); }
            Text.Append(Buffer,0,Count);
        }
    }
    private static void Stop(Process Process){
        try { if(!Process.HasExited){ Process.Kill(true); } }
        catch(InvalidOperationException){ }
    }
}
