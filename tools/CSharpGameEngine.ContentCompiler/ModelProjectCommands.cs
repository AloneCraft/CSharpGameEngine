using System.Text.Json;
namespace CSharpGameEngine.ContentCompiler;
internal static class ModelProjectCommands {
    internal static async Task<int>RunAsync(string[]Arguments,CancellationToken CancellationToken){
        try {
            var Project=Directory.GetCurrentDirectory();string?Cook=null;string?Inputs=null;string?Output=null;string?Resources=null;string?CopyTo=null;string?FileList=null;var Verify=false;
            var Modules=new List<(string ModulePath,bool ProjectModule)>();var Options=new SortedDictionary<string,string>(StringComparer.Ordinal);
            for(var Index=1;Index<Arguments.Length;Index++){
                switch(Arguments[Index]){
                    case "--project" when Index+1<Arguments.Length:Project=Arguments[++Index];break;
                    case "--cook" when Index+1<Arguments.Length:Cook=Arguments[++Index];break;
                    case "--module" when Index+1<Arguments.Length:Modules.Add((Arguments[++Index],true));break;
                    case "--resources-module" when Index+1<Arguments.Length:Resources=Arguments[++Index];break;
                    case "--inputs" when Index+1<Arguments.Length:Inputs=Arguments[++Index];break;
                    case "--out" when Index+1<Arguments.Length:Output=Arguments[++Index];break;
                    case "--copy-to" when Index+1<Arguments.Length:CopyTo=Arguments[++Index];break;
                    case "--file-list" when Index+1<Arguments.Length:FileList=Arguments[++Index];break;
                    case "--verify":Verify=true;break;
                    case "--option" when Index+1<Arguments.Length:
                        var Pair=Arguments[++Index];var Separator=Pair.IndexOf('=');
                        if(Separator<=0||!Options.TryAdd(Pair[..Separator],Pair[(Separator+1)..])){ throw new ArgumentException("Model options require unique name=value pairs."); }
                        break;
                    default:throw new ArgumentException("Unknown or incomplete model project option: "+Arguments[Index]);
                }
            }
            if(Cook is null||Inputs is null||Output is null){ throw new ArgumentException("compile-project requires --cook, --inputs and --out."); }
            if(FileList is not null&&CopyTo is null){ throw new ArgumentException("--file-list requires --copy-to."); }
            if(new FileInfo(Inputs).Length>2*1024*1024){ throw new InvalidDataException("IMP803: model project input list exceeds its bound."); }
            var Sources=(await File.ReadAllLinesAsync(Inputs,CancellationToken)).Where(static Line=>!string.IsNullOrWhiteSpace(Line)).ToArray();
            if(Sources.Length>65535){ throw new InvalidDataException("IMP803: model project has too many inputs."); }
            var Result=await new ModelProjectCompiler(Project,Cook,Modules,Output,Resources).CompileAsync(Sources,Options,Verify,CancellationToken);
            foreach(var Diagnostic in Result.Diagnostics){ Console.Error.WriteLine(Diagnostic); }
            if(Result.Succeeded&&CopyTo is not null){
                var Files=await ModelContentExporter.ExportAsync(Output,CopyTo,CancellationToken);
                if(FileList is not null){ Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FileList))!);await File.WriteAllLinesAsync(FileList,Files,CancellationToken); }
            }
            Console.WriteLine(JsonSerializer.Serialize(new { Schema="cge-model-project-compile/1",Result.Succeeded,Entries=Result.Index.Entries.Length,
                LastKnownGood=Result.Index.Entries.Count(static Entry=>Entry.LastKnownGood),Rebuilt=Result.Compilation?.Rebuilt.Length,Reused=Result.Compilation?.Reused.Length }));
            return Result.Succeeded?0:1;
        }catch(OperationCanceledException){ Console.Error.WriteLine("IMP802: model project preparation cancelled.");return 1; }
        catch(Exception Error)when(Error is IOException or ArgumentException or JsonException or UnauthorizedAccessException){ Console.Error.WriteLine(Error.Message);return 1; }
    }
}
