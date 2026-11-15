namespace CSharpGameEngine.Graphics.D3D12;

public sealed class ViewerRunOptions {
    public required string ModelPath { get; init;}

    public int?Frames { get; init;}

    public int Width { get; init;}=1280;

    public int Height { get; init;}=720;

    public bool Hidden { get; init;}

    public bool UseWarp { get; init;}

    public bool EnableDebugLayer { get; init;}

    public bool EnableGpuValidation { get; init;}

    public bool Verify { get; init;}
}

public sealed record ViewerOptionsParseResult(bool Success,ViewerRunOptions?Options,string?Error){
    public static ViewerOptionsParseResult Ok(ViewerRunOptions Options)=>new(true,Options,null);

    public static ViewerOptionsParseResult Fail(string Error)=>new(false,null,Error);
}

public static class ViewerOptionsParser {
    public static ViewerOptionsParseResult Parse(IReadOnlyList<string>Args){
        ArgumentNullException.ThrowIfNull(Args);

        string?ModelPath=null;
        int?Frames=null;
        var Width=1280;
        var Height=720;
        var Hidden=false;
        var UseWarp=false;
        var EnableDebugLayer=false;
        var EnableGpuValidation=false;
        var Verify=false;

        for(var I=0;I<Args.Count;I++){
            var Arg=Args[I];
            switch(Arg){
                case "--model":{
                        if(I+1>=Args.Count){
                            return ViewerOptionsParseResult.Fail("--model requires a file path value.");
                        }
                        ModelPath=Args[++I];
                        break;
                    }

                case "--frames":{
                        if(!TryReadIntValue(Args,ref I,out var Value)){
                            return ViewerOptionsParseResult.Fail("--frames requires an integer value.");
                        }
                        if(Value<=0){
                            return ViewerOptionsParseResult.Fail($"--frames must be a positive integer (got {Value}).");
                        }
                        Frames=Value;
                        break;
                    }

                case "--width":{
                        if(!TryReadIntValue(Args,ref I,out var Value)){
                            return ViewerOptionsParseResult.Fail("--width requires an integer value.");
                        }
                        if(Value<=0){
                            return ViewerOptionsParseResult.Fail($"--width must be a positive integer (got {Value}).");
                        }
                        Width=Value;
                        break;
                    }

                case "--height":{
                        if(!TryReadIntValue(Args,ref I,out var Value)){
                            return ViewerOptionsParseResult.Fail("--height requires an integer value.");
                        }
                        if(Value<=0){
                            return ViewerOptionsParseResult.Fail($"--height must be a positive integer (got {Value}).");
                        }
                        Height=Value;
                        break;
                    }

                case "--hidden":
                    Hidden=true;
                    break;

                case "--warp":
                    UseWarp=true;
                    break;

                case "--debug-layer":
                    EnableDebugLayer=true;
                    break;

                case "--gpu-validation":
                    EnableGpuValidation=true;
                    break;

                case "--verify":
                    Verify=true;
                    break;

                default:
                    return ViewerOptionsParseResult.Fail($"Unknown option: {Arg}");
            }
        }

        if(ModelPath is null){
            return ViewerOptionsParseResult.Fail("--model <path> is required.");
        }

        ViewerRunOptions Options=new(){
            ModelPath=ModelPath,
            Frames=Frames,
            Width=Width,
            Height=Height,
            Hidden=Hidden,
            UseWarp=UseWarp,
            EnableDebugLayer=EnableDebugLayer,
            EnableGpuValidation=EnableGpuValidation,
            Verify=Verify,
        };

        return ViewerOptionsParseResult.Ok(Options);
    }

    private static bool TryReadIntValue(IReadOnlyList<string>Args,ref int Index,out int Value){
        if(Index+1>=Args.Count||!int.TryParse(Args[Index+1],out Value)){
            Value=0;
            return false;
        }

        Index++;
        return true;
    }
}
