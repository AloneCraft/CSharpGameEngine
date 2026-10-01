using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
namespace CSharpGameEngine.VSExtension;
/// <summary>Stops a Scene view's editor world: the preview host started with --assembly (ADR-0047).</summary>
internal static class EditorWorldProcess {
    /// <summary>Asks the editor world to shut down, then ends it if it has not.</summary>
    internal static void Stop(Process EditorWorld){
        try{
            if(!EditorWorld.HasExited){
                RequestShutdown(EditorWorld.Id);
                if(!EditorWorld.WaitForExit(2_000)){
                    EditorWorld.Kill();
                    EditorWorld.WaitForExit(1_000);
                }
            }
        }catch(InvalidOperationException){
        }catch(System.ComponentModel.Win32Exception){
        }
    }
    /// <summary>
    /// M0095 3-6: the host answers on the same pipe and its control loop finishes only once the answer is
    /// read, so the request reads it. The exchange runs on its own and the caller never waits for it:
    /// <see cref="Stop"/> ends the process within about three seconds either way. (The request used to open
    /// the pipe write-only and never read the answer, so neither side finished and the process stayed.)
    /// </summary>
    private static void RequestShutdown(int ProcessId)=>
        _=System.Threading.Tasks.Task.Run(()=>{
            try{
                using NamedPipeClientStream Pipe=new(
                    ".",$"CSharpGameEngine.EditorPreview.{ProcessId}",
                    PipeDirection.InOut,PipeOptions.None);
                Pipe.Connect(500);
                using StreamWriter Writer=new(Pipe){AutoFlush=true};
                using StreamReader Reader=new(Pipe);
                Writer.WriteLine("shutdown");
                _=Reader.ReadLine();
            }catch(TimeoutException){
            }catch(IOException){
            }catch(UnauthorizedAccessException){
            }catch(ObjectDisposedException){
            }
        });
}
