using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using TerraFX.Interop;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.DirectX.DirectX;
using static TerraFX.Interop.DirectX.DXC;
using static TerraFX.Interop.DirectX.DXC_OUT_KIND;
using static TerraFX.Interop.Windows.Windows;

namespace CSharpGameEngine.Compiler.Hlsl;

/// <summary>Compiles a single HLSL entry point to DXIL bytecode using DXC (DirectXShaderCompiler).</summary>
public static class HlslCompiler {
    private static readonly Regex DiagnosticLinePattern=new(
        @"^(?<file>[^:\r\n]+):(?<line>\d+):(?<column>\d+):\s*(?<severity>error|warning|note)\s*:\s*(?<message>.*)$",
        RegexOptions.Compiled|RegexOptions.Multiline);

    public static unsafe HlslCompileResult Compile(HlslCompileRequest Request){
        ArgumentNullException.ThrowIfNull(Request);

        var FullPath=Path.GetFullPath(Request.FilePath);
        var SourceName=Path.GetFileName(FullPath);

        byte[]SourceBytes;
        try {
            SourceBytes=File.ReadAllBytes(FullPath);
        } catch(Exception Ex) when (Ex is IOException or UnauthorizedAccessException){
            return HlslCompileResult.Failed([new HlslDiagnostic(SourceName,0,0,HlslDiagnosticSeverity.Error, $"Could not read shader source: {Ex.Message}")]);
        }

        IDxcUtils*Utils=null;
        IDxcCompiler3*compiler=null;
        IDxcResult*Result=null;
        nint[]NativeArgs=[];

        try {
            Utils=CreateDxcInstance<IDxcUtils>(CLSID.CLSID_DxcUtils,SourceName,"DxcCreateInstance(CLSID_DxcUtils)");
            compiler=CreateDxcInstance<IDxcCompiler3>(CLSID.CLSID_DxcCompiler,SourceName,"DxcCreateInstance(CLSID_DxcCompiler)");

            List<string>ArgStrings=BuildArguments(Request,SourceName);
            NativeArgs=new nint[ArgStrings.Count];
            for(var i=0;i<ArgStrings.Count;i++){
                NativeArgs[i]=Marshal.StringToHGlobalUni(ArgStrings[i]);
            }

            fixed(byte*PSource=SourceBytes)
            fixed(nint*PNativeArgs=NativeArgs){
                DxcBuffer SourceBuffer=new(){
                    Ptr=PSource,
                    Size=(nuint)SourceBytes.Length,
                    Encoding=DXC_CP_UTF8,
                };

                IDxcResult*LocalResult;
                HRESULT Hr=compiler->Compile(
                    &SourceBuffer,
                    (char**)PNativeArgs,
                    (uint)NativeArgs.Length,
                    pIncludeHandler:null,
                    __uuidof<IDxcResult>(),
                    (void**)&LocalResult);
                Result=LocalResult;

                if(Hr.FAILED){
                    return HlslCompileResult.Failed([new HlslDiagnostic(SourceName,0,0,HlslDiagnosticSeverity.Error,
                        $"IDxcCompiler3::Compile failed. HRESULT=0x{Hr.Value:X8}")]);
                }
            }

            List<HlslDiagnostic>Diagnostics=ReadDiagnostics(Result,SourceName);

            HRESULT Status;
            _=Result->GetStatus(&Status);

            if(Status.FAILED){
                if(Diagnostics.Count==0){
                    Diagnostics.Add(new HlslDiagnostic(SourceName,0,0,HlslDiagnosticSeverity.Error,
                        $"DXC reported failure with no diagnostic text. HRESULT=0x{Status.Value:X8}"));
                }
                return HlslCompileResult.Failed(Diagnostics);
            }

            var Bytecode=ReadObjectBytecode(Result);
            if(Bytecode is null||Bytecode.Length==0){
                Diagnostics.Add(new HlslDiagnostic(SourceName,0,0,HlslDiagnosticSeverity.Error,
                    "DXC reported success but produced no DXIL bytecode."));
                return HlslCompileResult.Failed(Diagnostics);
            }

            return HlslCompileResult.Succeeded(Bytecode,Diagnostics);
        } finally {
            foreach(var P in NativeArgs){
                if(P!=0){
                    Marshal.FreeHGlobal(P);
                }
            }

            if(Result!=null){
                _=Result->Release();
            }

            if(compiler!=null){
                _=compiler->Release();
            }

            if(Utils!=null){
                _=Utils->Release();
            }
        }
    }

    private static unsafe T*CreateDxcInstance<T>(in Guid Clsid,string SourceName,string ApiName)
        where T:unmanaged,INativeGuid {
        Guid ClsidCopy=Clsid;
        T*Instance;
        HRESULT Hr=DxcCreateInstance(&ClsidCopy,__uuidof<T>(),(void**)&Instance);

        if(Hr.FAILED){
            throw new InvalidOperationException($"{ApiName} failed while compiling '{SourceName}'. HRESULT=0x{Hr.Value:X8}");
        }

        return Instance;
    }

    private static List<string>BuildArguments(HlslCompileRequest Request,string SourceName){
        List<string>Args=[
            SourceName,
            "-E",Request.EntryPoint,
            "-T",Request.TargetProfile,
        ];

        foreach(HlslDefine Define in Request.Defines){
            Args.Add("-D");
            Args.Add(Define.ToString());
        }

        if(Request.EnableDebugInfo){
            Args.Add("-Zi");
            Args.Add("-Od");
            Args.Add("-Qembed_debug");
        } else {
            Args.Add("-O3");
        }

        return Args;
    }

    private static unsafe List<HlslDiagnostic>ReadDiagnostics(IDxcResult*Result,string SourceName){
        List<HlslDiagnostic>Diagnostics=[];

        if(!Result->HasOutput(DXC_OUT_ERRORS)){
            return Diagnostics;
        }

        IDxcBlobUtf8*Errors=null;
        try {
            HRESULT Hr=Result->GetOutput(DXC_OUT_ERRORS,__uuidof<IDxcBlobUtf8>(),(void**)&Errors,ppOutputName:null);
            if(Hr.FAILED||Errors==null){
                return Diagnostics;
            }

            var Length=Errors->GetStringLength();
            if(Length==0){
                return Diagnostics;
            }

            var Text=Marshal.PtrToStringUTF8((nint)Errors->GetStringPointer(),(int)Length);
            MatchCollection Matches=DiagnosticLinePattern.Matches(Text);

            if(Matches.Count==0){
                Diagnostics.Add(new HlslDiagnostic(SourceName,0,0,HlslDiagnosticSeverity.Error,Text.Trim()));
                return Diagnostics;
            }

            foreach(Match match in Matches){
                HlslDiagnosticSeverity Severity=match.Groups["severity"].Value switch {
                    "error"=>HlslDiagnosticSeverity.Error,
                    "warning"=>HlslDiagnosticSeverity.Warning,
                    _ =>HlslDiagnosticSeverity.Info,
                };

                Diagnostics.Add(new HlslDiagnostic(
                    match.Groups["file"].Value,
                    int.Parse(match.Groups["line"].Value),
                    int.Parse(match.Groups["column"].Value),
                    Severity,
                    match.Groups["message"].Value.TrimEnd()));
            }

            return Diagnostics;
        } finally {
            if(Errors!=null){
                _=Errors->Release();
            }
        }
    }

    private static unsafe byte[]?ReadObjectBytecode(IDxcResult*Result){
        if(!Result->HasOutput(DXC_OUT_OBJECT)){
            return null;
        }

        IDxcBlob*ObjectBlob=null;
        try {
            HRESULT Hr=Result->GetOutput(DXC_OUT_OBJECT,__uuidof<IDxcBlob>(),(void**)&ObjectBlob,ppOutputName:null);
            if(Hr.FAILED||ObjectBlob==null){
                return null;
            }

            var Size=ObjectBlob->GetBufferSize();
            if(Size==0){
                return null;
            }

            var Bytecode=new byte[Size];
            Marshal.Copy((nint)ObjectBlob->GetBufferPointer(),Bytecode,0,Bytecode.Length);
            return Bytecode;
        } finally {
            if(ObjectBlob!=null){
                _=ObjectBlob->Release();
            }
        }
    }
}
