using System;
namespace CSharpGameEngine.Vsix.Automation;
internal static class Program {
    [STAThread]
    private static int Main(string[]Arguments) {
        var Parsed=AutomationOptions.Parse(Arguments);
        if(!Parsed.Success) { Console.Error.WriteLine(Parsed.Error);return (int)AutomationExitCode.InfrastructureError; }
        var Options=Parsed.Options!;
        if(Options.Command==AutomationCommand.ValidateLedger) {
            var Result=VsixExperimentalLedgerValidator.Validate(Options.ManifestPath,DateTimeOffset.UtcNow);
            foreach(var Error in Result.Errors) { Console.Error.WriteLine(Error); }
            Console.WriteLine($"ledger passed={Result.PassedCount} pending={Result.PendingCount} failed={Result.FailedCount}");
            return Result.IsValid?(int)AutomationExitCode.Success:(int)AutomationExitCode.OracleMismatch;
        }
        try {
            using var MessageFilter=OleMessageFilter.Register();
            return (int)new VsixExperimentalScenario().Run(Options);
        }
        catch(OracleMismatchException Exception) { Console.Error.WriteLine(Exception.Message);return (int)AutomationExitCode.OracleMismatch; }
        catch(Exception Exception) { Console.Error.WriteLine(Exception);return (int)AutomationExitCode.InfrastructureError; }
    }
}
internal sealed class OracleMismatchException:Exception {
    internal OracleMismatchException(string Message):base(Message) { }
}
