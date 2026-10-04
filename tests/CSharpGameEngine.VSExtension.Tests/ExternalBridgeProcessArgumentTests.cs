using System;
using System.Linq;
using System.Runtime.InteropServices;
using Xunit;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// A one-shot bridge gets its arguments on a command line, which Windows splits with CommandLineToArgvW's rules: the
/// quoting must give each argument back exactly. A folder argument ending in a backslash used to escape its closing
/// quote and join the next argument.
/// </summary>
public sealed class ExternalBridgeProcessArgumentTests {
    [DllImport("shell32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern IntPtr CommandLineToArgvW(string CommandLine,out int Count);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr Memory);

    private static string[]Split(string CommandLine){
        var Pointer=CommandLineToArgvW(CommandLine,out var Count);
        Assert.NotEqual(IntPtr.Zero,Pointer);
        try {
            return Enumerable.Range(0,Count).Select(Index=>Marshal.PtrToStringUni(Marshal.ReadIntPtr(Pointer,Index*IntPtr.Size))!).ToArray();
        } finally { LocalFree(Pointer); }
    }

    [Theory]
    [InlineData(@"C:\Out\",@"next")]
    [InlineData(@"C:\path with space\model.glb",@"C:\out put\model.json")]
    [InlineData("say \"hi\"",@"back\slash")]
    [InlineData(@"tail\\",@"a\""b")]
    [InlineData("",@"日本語 フォルダ\")]
    public void QuotedArguments_ReadBackAsThemselves(string First,string Second){
        var CommandLine="bridge.exe "+ExternalBridgeProcess.QuoteArgument(First)+" "+ExternalBridgeProcess.QuoteArgument(Second);

        Assert.Equal(new[]{"bridge.exe",First,Second},Split(CommandLine));
    }
}
