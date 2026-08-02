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
public static class HlslCompiler
{
    private static readonly Regex DiagnosticLinePattern = new(
        @"^(?<file>[^:\r\n]+):(?<line>\d+):(?<column>\d+):\s*(?<severity>error|warning|note)\s*:\s*(?<message>.*)$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    public static unsafe HlslCompileResult Compile(HlslCompileRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        string fullPath = Path.GetFullPath(request.FilePath);
        string sourceName = Path.GetFileName(fullPath);

        byte[] sourceBytes;
        try
        {
            sourceBytes = File.ReadAllBytes(fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return HlslCompileResult.Failed([new HlslDiagnostic(sourceName, 0, 0, HlslDiagnosticSeverity.Error, $"Could not read shader source: {ex.Message}")]);
        }

        IDxcUtils* utils = null;
        IDxcCompiler3* compiler = null;
        IDxcResult* result = null;
        nint[] nativeArgs = [];

        try
        {
            utils = CreateDxcInstance<IDxcUtils>(CLSID.CLSID_DxcUtils, sourceName, "DxcCreateInstance(CLSID_DxcUtils)");
            compiler = CreateDxcInstance<IDxcCompiler3>(CLSID.CLSID_DxcCompiler, sourceName, "DxcCreateInstance(CLSID_DxcCompiler)");

            List<string> argStrings = BuildArguments(request, sourceName);
            nativeArgs = new nint[argStrings.Count];
            for (int i = 0; i < argStrings.Count; i++)
            {
                nativeArgs[i] = Marshal.StringToHGlobalUni(argStrings[i]);
            }

            fixed (byte* pSource = sourceBytes)
            fixed (nint* pNativeArgs = nativeArgs)
            {
                DxcBuffer sourceBuffer = new()
                {
                    Ptr = pSource,
                    Size = (nuint)sourceBytes.Length,
                    Encoding = DXC_CP_UTF8,
                };

                IDxcResult* localResult;
                HRESULT hr = compiler->Compile(
                    &sourceBuffer,
                    (char**)pNativeArgs,
                    (uint)nativeArgs.Length,
                    pIncludeHandler: null,
                    __uuidof<IDxcResult>(),
                    (void**)&localResult);
                result = localResult;

                if (hr.FAILED)
                {
                    return HlslCompileResult.Failed([new HlslDiagnostic(sourceName, 0, 0, HlslDiagnosticSeverity.Error,
                        $"IDxcCompiler3::Compile failed. HRESULT=0x{hr.Value:X8}")]);
                }
            }

            List<HlslDiagnostic> diagnostics = ReadDiagnostics(result, sourceName);

            HRESULT status;
            _ = result->GetStatus(&status);

            if (status.FAILED)
            {
                if (diagnostics.Count == 0)
                {
                    diagnostics.Add(new HlslDiagnostic(sourceName, 0, 0, HlslDiagnosticSeverity.Error,
                        $"DXC reported failure with no diagnostic text. HRESULT=0x{status.Value:X8}"));
                }
                return HlslCompileResult.Failed(diagnostics);
            }

            byte[]? bytecode = ReadObjectBytecode(result);
            if (bytecode is null || bytecode.Length == 0)
            {
                diagnostics.Add(new HlslDiagnostic(sourceName, 0, 0, HlslDiagnosticSeverity.Error,
                    "DXC reported success but produced no DXIL bytecode."));
                return HlslCompileResult.Failed(diagnostics);
            }

            return HlslCompileResult.Succeeded(bytecode, diagnostics);
        }
        finally
        {
            foreach (nint p in nativeArgs)
            {
                if (p != 0)
                {
                    Marshal.FreeHGlobal(p);
                }
            }

            if (result != null)
            {
                _ = result->Release();
            }

            if (compiler != null)
            {
                _ = compiler->Release();
            }

            if (utils != null)
            {
                _ = utils->Release();
            }
        }
    }

    private static unsafe T* CreateDxcInstance<T>(in Guid clsid, string sourceName, string apiName)
        where T : unmanaged, INativeGuid
    {
        Guid clsidCopy = clsid;
        T* instance;
        HRESULT hr = DxcCreateInstance(&clsidCopy, __uuidof<T>(), (void**)&instance);

        if (hr.FAILED)
        {
            throw new InvalidOperationException($"{apiName} failed while compiling '{sourceName}'. HRESULT=0x{hr.Value:X8}");
        }

        return instance;
    }

    private static List<string> BuildArguments(HlslCompileRequest request, string sourceName)
    {
        List<string> args =
        [
            sourceName,
            "-E", request.EntryPoint,
            "-T", request.TargetProfile,
        ];

        foreach (HlslDefine define in request.Defines)
        {
            args.Add("-D");
            args.Add(define.ToString());
        }

        if (request.EnableDebugInfo)
        {
            args.Add("-Zi");
            args.Add("-Od");
            args.Add("-Qembed_debug");
        }
        else
        {
            args.Add("-O3");
        }

        return args;
    }

    private static unsafe List<HlslDiagnostic> ReadDiagnostics(IDxcResult* result, string sourceName)
    {
        List<HlslDiagnostic> diagnostics = [];

        if (!result->HasOutput(DXC_OUT_ERRORS))
        {
            return diagnostics;
        }

        IDxcBlobUtf8* errors = null;
        try
        {
            HRESULT hr = result->GetOutput(DXC_OUT_ERRORS, __uuidof<IDxcBlobUtf8>(), (void**)&errors, ppOutputName: null);
            if (hr.FAILED || errors == null)
            {
                return diagnostics;
            }

            nuint length = errors->GetStringLength();
            if (length == 0)
            {
                return diagnostics;
            }

            string text = Marshal.PtrToStringUTF8((nint)errors->GetStringPointer(), (int)length);
            MatchCollection matches = DiagnosticLinePattern.Matches(text);

            if (matches.Count == 0)
            {
                diagnostics.Add(new HlslDiagnostic(sourceName, 0, 0, HlslDiagnosticSeverity.Error, text.Trim()));
                return diagnostics;
            }

            foreach (Match match in matches)
            {
                HlslDiagnosticSeverity severity = match.Groups["severity"].Value switch
                {
                    "error" => HlslDiagnosticSeverity.Error,
                    "warning" => HlslDiagnosticSeverity.Warning,
                    _ => HlslDiagnosticSeverity.Info,
                };

                diagnostics.Add(new HlslDiagnostic(
                    match.Groups["file"].Value,
                    int.Parse(match.Groups["line"].Value),
                    int.Parse(match.Groups["column"].Value),
                    severity,
                    match.Groups["message"].Value.TrimEnd()));
            }

            return diagnostics;
        }
        finally
        {
            if (errors != null)
            {
                _ = errors->Release();
            }
        }
    }

    private static unsafe byte[]? ReadObjectBytecode(IDxcResult* result)
    {
        if (!result->HasOutput(DXC_OUT_OBJECT))
        {
            return null;
        }

        IDxcBlob* objectBlob = null;
        try
        {
            HRESULT hr = result->GetOutput(DXC_OUT_OBJECT, __uuidof<IDxcBlob>(), (void**)&objectBlob, ppOutputName: null);
            if (hr.FAILED || objectBlob == null)
            {
                return null;
            }

            nuint size = objectBlob->GetBufferSize();
            if (size == 0)
            {
                return null;
            }

            byte[] bytecode = new byte[size];
            Marshal.Copy((nint)objectBlob->GetBufferPointer(), bytecode, 0, bytecode.Length);
            return bytecode;
        }
        finally
        {
            if (objectBlob != null)
            {
                _ = objectBlob->Release();
            }
        }
    }
}
