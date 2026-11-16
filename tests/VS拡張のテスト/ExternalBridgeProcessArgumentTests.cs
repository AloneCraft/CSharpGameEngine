using System;
using System.Linq;
using System.Runtime.InteropServices;
using Xunit;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// 1 回限りのブリッジはコマンドライン引数を受け取り、Windows は CommandLineToArgvW の規則で分割する。引用符付けにより各引数を正確に復元できなければならない。以前は、末尾がバックスラッシュのフォルダー引数が閉じ引用符をエスケープし、次の引数と連結されていた。
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
