using Xunit;
namespace CSharpGameEngine.Core.Tests;
/// <summary>
/// Windows PowerShell 5.1 は BOM の無いスクリプトをシステムの ANSI コードページで読む。
/// 日本語などの非 ASCII を含む UTF-8 のスクリプトは文字化けし、引用符の終わりを見失って構文エラーになる（Reinstall-CgeFixedVsix.ps1 などで発生）。
/// そのため、非 ASCII を含む PowerShell スクリプトは UTF-8 の BOM 付きで保存する。
/// </summary>
public sealed class PowerShellScriptEncodingTests {
    private static readonly string RepositoryRoot=FindRepositoryRoot();
    private static readonly string[]Extensions=[".ps1",".psm1",".psd1"];
    private static readonly string[]SkippedDirectories=["bin","obj",".git",".vs",".artifacts","node_modules"];
    [Fact]
    public void NonAsciiPowerShellScriptsCarryAUtf8ByteOrderMark(){
        var Violations=ScriptFiles(RepositoryRoot)
            .Where(static File=>{
                var Bytes=System.IO.File.ReadAllBytes(File);
                var HasBom=Bytes.Length>=3&&Bytes[0]==0xEF&&Bytes[1]==0xBB&&Bytes[2]==0xBF;
                return !HasBom&&Bytes.Any(static Value=>Value>=0x80);
            })
            .Select(File=>Path.GetRelativePath(RepositoryRoot,File).Replace('\\','/')).ToArray();
        Assert.True(Violations.Length==0,"UTF-8 の BOM 付きで保存してください:"+Environment.NewLine+string.Join(Environment.NewLine,Violations));
    }
    private static IEnumerable<string>ScriptFiles(string Directory){
        foreach(var File in System.IO.Directory.EnumerateFiles(Directory)){
            if(Extensions.Contains(Path.GetExtension(File),StringComparer.OrdinalIgnoreCase)){ yield return File; }
        }
        foreach(var Child in System.IO.Directory.EnumerateDirectories(Directory)){
            if(SkippedDirectories.Contains(Path.GetFileName(Child),StringComparer.OrdinalIgnoreCase)){ continue; }
            foreach(var File in ScriptFiles(Child)){ yield return File; }
        }
    }
    private static string FindRepositoryRoot(){
        var Current=new DirectoryInfo(AppContext.BaseDirectory);
        while(Current is not null){
            if(File.Exists(Path.Combine(Current.FullName,"CSharpGameEngine.slnx"))){ return Current.FullName; }
            Current=Current.Parent;
        }
        throw new DirectoryNotFoundException("CSharpGameEngine.slnxのルートが見つかりません。");
    }
}
