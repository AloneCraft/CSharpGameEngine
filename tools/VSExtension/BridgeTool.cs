using System;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// 拡張機能が Visual Studio の外の別プロセスで動かす道具。プロジェクトのフォルダー(実行ファイルを探す場所)と、
/// <c>Cge</c> の下位コマンド(下位コマンドを持たない道具では <see langword="null"/>)の組。
/// 1 回だけ動かすときは下位コマンドを引数の前に付け、常駐させるときは <c>Cge.exe &lt;下位コマンド&gt; --serve ...</c> で起動する。
/// 常駐の依頼の引数には下位コマンドを付けない(サーバーがすでにその下位コマンドで動いているため)。
/// </summary>
internal sealed class BridgeTool {
    /// <summary>エンジンのコマンドの道具をまとめた実行ファイルのプロジェクト(Docs/refactoring-designs/20261117-command-tools-consolidation.md)。</summary>
    internal const string CgeProjectRelativePath=@"tools\Cge";

    internal static readonly BridgeTool Gltf=new(CgeProjectRelativePath,"gltf");
    internal static readonly BridgeTool Fbx=new(CgeProjectRelativePath,"fbx");
    internal static readonly BridgeTool Primitive=new(CgeProjectRelativePath,"primitive");
    internal static readonly BridgeTool AssetAuthoring=new(CgeProjectRelativePath,"asset-authoring");
    internal static readonly BridgeTool ShaderGraph=new(CgeProjectRelativePath,"shader-graph");

    internal BridgeTool(string ProjectRelativePath,string?Command){
        if(string.IsNullOrWhiteSpace(ProjectRelativePath)){ throw new ArgumentException(nameof(ProjectRelativePath)); }
        this.ProjectRelativePath=ProjectRelativePath;
        this.Command=Command;
    }

    internal string ProjectRelativePath { get; }

    internal string?Command { get; }

    /// <summary>メッセージと常駐のパイプの名前に使う名前。下位コマンドが違えば別の名前になる。</summary>
    internal string Identity=>this.Command is null?this.ProjectRelativePath:this.ProjectRelativePath+" "+this.Command;

    /// <summary>実行ファイルに渡す引数。下位コマンドがあれば先頭に付ける。</summary>
    internal string[]CommandLine(string[]Arguments){
        if(Arguments is null){ throw new ArgumentNullException(nameof(Arguments)); }
        return this.Command is null?Arguments:[this.Command,..Arguments];
    }

    public override string ToString()=>this.Identity;
}
