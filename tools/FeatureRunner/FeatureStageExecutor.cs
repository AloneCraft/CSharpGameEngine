using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
namespace CSharpGameEngine.FeatureStages;
public sealed class FeatureStageExecutor:IFeatureStageExecutor {
    public async Task<FeatureStageRunResult>ExecuteAsync(FeatureStageDefinition Stage,FeatureStageExecutionOptions Options,CancellationToken CancellationToken){
        var Started=DateTimeOffset.UtcNow;
        if(Stage.Availability==FeatureStageAvailability.NotImplemented){
            return FeatureStageRunResult.Create(Stage,Options,FeatureStageRunStatus.NotImplemented,"Stage is specified but not implemented.",[new FeatureStageAssertion("implementation-availability","NotImplemented",false)],null,null,["The feature implementation and executable oracle are not available yet."],StartedUtc:Started,FinishedUtc:DateTimeOffset.UtcNow);
        }
        var Assertions=new List<FeatureStageAssertion>();
        var Errors=new List<string>();
        var StandardOutputs=new List<string>();
        var StandardErrors=new List<string>();
        var Status=FeatureStageRunStatus.Passed;
        foreach(var Command in Stage.Commands){
            for(int Attempt=1;Attempt<=2;Attempt++){
                ProcessExecutionResult Execution;
                try {
                    Execution=await RunProcessAsync(Command,Stage,Options,CancellationToken).ConfigureAwait(false);
                } catch(OperationCanceledException)when(!CancellationToken.IsCancellationRequested){
                    Status=FeatureStageRunStatus.TimedOut;
                    Errors.Add($"Stage exceeded its {Stage.TimeoutSeconds} second timeout.");
                    Assertions.Add(new FeatureStageAssertion($"command:{Command.FileName}:attempt:{Attempt}","Timed out; timeout is never retried.",false));
                    break;
                } catch(Exception Ex)when(Ex is not OperationCanceledException){
                    StandardErrors.Add($"attempt {Attempt}: {Ex}");
                    if(Attempt==1){
                        Assertions.Add(new FeatureStageAssertion($"retry:{Command.FileName}","Infrastructure exception triggered the one permitted retry.",true));
                        continue;
                    }
                    Status=FeatureStageRunStatus.InfrastructureError;
                    Errors.Add(Ex.ToString());
                    Assertions.Add(new FeatureStageAssertion($"command:{Command.FileName}:attempt:{Attempt}",Ex.Message,false));
                    break;
                }
                StandardOutputs.Add($"attempt {Attempt}: {Execution.StandardOutput}");
                StandardErrors.Add($"attempt {Attempt}: {Execution.StandardError}");
                if(Execution.ExitCode==0){
                    Assertions.Add(new FeatureStageAssertion($"command:{Command.FileName}:attempt:{Attempt}",$"Exit code 0 after {Attempt} attempt(s).",true));
                    break;
                }
                bool Infrastructure=Command.InfrastructureErrorExitCodes.Contains(Execution.ExitCode);
                if(Infrastructure&&Attempt==1){
                    Assertions.Add(new FeatureStageAssertion($"retry:{Command.FileName}",$"Infrastructure exit code {Execution.ExitCode} triggered the one permitted retry.",true));
                    continue;
                }
                Status=Infrastructure?FeatureStageRunStatus.InfrastructureError:FeatureStageRunStatus.Failed;
                Errors.Add($"{Command.FileName} exited with code {Execution.ExitCode}.");
                Assertions.Add(new FeatureStageAssertion($"command:{Command.FileName}:attempt:{Attempt}",$"Exit code {Execution.ExitCode}",false));
                break;
            }
            if(Status!=FeatureStageRunStatus.Passed){ break; }
        }
        var StandardOutput=string.Join("\n",StandardOutputs);
        var StandardError=string.Join("\n",StandardErrors);
        var StateHash=Hash(StandardOutput);
        var EventHash=Hash(string.Join("\n",Assertions.Select(static Assertion=>$"{Assertion.Name}:{Assertion.Passed}:{Assertion.Details}")));
        var Summary=Status==FeatureStageRunStatus.Passed?$"Passed: {Stage.Oracle}":$"{Status}: {Stage.Oracle}";
        return FeatureStageRunResult.Create(Stage,Options,Status,Summary,Assertions,StateHash,EventHash,Errors,StandardOutput,StandardError,Started,DateTimeOffset.UtcNow);
    }
    private static async Task<ProcessExecutionResult>RunProcessAsync(FeatureStageCommand Command,FeatureStageDefinition Stage,FeatureStageExecutionOptions Options,CancellationToken CancellationToken){
        var StartInfo=new ProcessStartInfo(){
            FileName=Command.FileName,
            WorkingDirectory=Options.RepositoryRoot,
            UseShellExecute=false,
            RedirectStandardOutput=true,
            RedirectStandardError=true,
            CreateNoWindow=true,
        };
        foreach(var Argument in ResolveArguments(Command,Options)){
            StartInfo.ArgumentList.Add(Argument);
        }
        StartInfo.Environment["CSHARP_GAME_ENGINE_FEATURE_STAGE"]=Stage.Id;
        StartInfo.Environment["CSHARP_GAME_ENGINE_FEATURE_PROFILE"]=Options.Profile;
        StartInfo.Environment["CSHARP_GAME_ENGINE_FEATURE_ADAPTER"]=Options.Adapter.ToString();
        StartInfo.Environment["CSHARP_GAME_ENGINE_FEATURE_SEED"]=Options.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture);
        StartInfo.Environment["CSHARP_GAME_ENGINE_FEATURE_TICKS"]=Options.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
        using var Process=new Process(){StartInfo=StartInfo};
        if(!Process.Start()){ throw new InvalidOperationException($"Could not start {Command.FileName}."); }
        var OutputTask=Process.StandardOutput.ReadToEndAsync(CancellationToken);
        var ErrorTask=Process.StandardError.ReadToEndAsync(CancellationToken);
        using var TimeoutSource=CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        TimeoutSource.CancelAfter(TimeSpan.FromSeconds(Stage.TimeoutSeconds));
        try {
            await Process.WaitForExitAsync(TimeoutSource.Token).ConfigureAwait(false);
        } catch(OperationCanceledException){
            if(!Process.HasExited){
                try { Process.Kill(true); } catch(InvalidOperationException){ }
            }
            throw;
        }
        return new ProcessExecutionResult(Process.ExitCode,await OutputTask.ConfigureAwait(false),await ErrorTask.ConfigureAwait(false));
    }
    private static IEnumerable<string>ResolveArguments(FeatureStageCommand Command,FeatureStageExecutionOptions Options){
        foreach(var Argument in Command.Arguments){
            yield return Argument.Replace("{configuration}",Options.Configuration,StringComparison.Ordinal).Replace("{profile}",Options.Profile,StringComparison.Ordinal).Replace("{seed}",Options.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture),StringComparison.Ordinal).Replace("{ticks}",Options.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture),StringComparison.Ordinal);
        }
        var AdapterArguments=Options.Adapter==FeatureAdapter.Warp?Command.WarpArguments:Command.HardwareArguments;
        foreach(var Argument in AdapterArguments){ yield return Argument; }
    }
    private static string Hash(string Value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Value))).ToLowerInvariant();
    private sealed record ProcessExecutionResult(int ExitCode,string StandardOutput,string StandardError);
}
