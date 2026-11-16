namespace CSharpGameEngine.Cge;

/// <summary>
/// エンジンのコマンドの道具の入口:<c>Cge &lt;command&gt; &lt;arguments&gt;</c>。最初の引数で下位コマンドを選び、残りの引数を
/// そのまま渡す。下位コマンドは、まとめる前のそれぞれの道具と同じ引数を取る(Docs/refactoring-designs/20261117-command-tools-consolidation.md)。
/// </summary>
internal static class Program {
    /// <summary>使い方の文に出す下位コマンドの名前と説明。下位コマンドを足したら、下の振り分けにも足す。</summary>
    internal static readonly (string Name,string Summary)[]Commands=[
        ("asset-authoring","Asset import, registration and model preview requests for the Visual Studio extension (bridge)"),
        ("content","Model content compilation, import and cache recovery"),
        ("fbx","Read an .fbx model into the editor's JSON document (bridge)"),
        ("font","Bake a TrueType font into a .cgefont distance-field atlas"),
        ("gltf","Read a .glb/.gltf model into the editor's JSON document (bridge)"),
        ("primitive","Write a primitive shape (Box, Sphere, ...) as a .glb (bridge)"),
        ("scene-schema","Copy the scene JSON Schema out of a game assembly"),
        ("shader-graph","Compile shader graphs and compute shaders, publish artifacts, build distance fields"),
        ("unreal","Import Unreal Engine level and template exports"),
    ];

    private static async Task<int> Main(string[] Arguments){
        if(Arguments.Length==0){
            WriteUsage(Console.Error);
            return 2;
        }
        if(Arguments[0] is "-h" or "--help" or "help" or "/?"){
            WriteUsage(Console.Out);
            return 0;
        }
        var Rest=Arguments[1..];
        switch(Arguments[0]){
            case "asset-authoring":return await CSharpGameEngine.Import.AssetAuthoring.Cli.AssetAuthoringBridgeCommand.RunAsync(Rest);
            case "content":return await CSharpGameEngine.ContentCompiler.ContentCommand.RunAsync(Rest);
            case "fbx":return await CSharpGameEngine.Import.Fbx.Cli.FbxCommand.RunAsync(Rest);
            // font と shader-graph は Windows の機能(GDI+、DXC)を使う。対象は net10.0 なので、ここで確かめてから呼ぶ。
            case "font":return OperatingSystem.IsWindowsVersionAtLeast(6,1)?CSharpGameEngine.FontBaker.FontBakerCommand.Run(Rest):WindowsOnly(Arguments[0]);
            case "gltf":return await CSharpGameEngine.Import.Gltf.Cli.GltfCommand.RunAsync(Rest);
            case "primitive":return await CSharpGameEngine.Primitive.Cli.PrimitiveCommand.RunAsync(Rest);
            case "scene-schema":return CSharpGameEngine.SceneSchema.Cli.Program.Run(Rest);
            case "shader-graph":return OperatingSystem.IsWindowsVersionAtLeast(6,1)?CSharpGameEngine.ShaderGraph.Cli.ShaderGraphCli.Run(Rest):WindowsOnly(Arguments[0]);
            case "unreal":return CSharpGameEngine.Unreal.SceneImporter.UnrealCommand.Run(Rest);
            default:
                Console.Error.WriteLine($"Unknown command: {Arguments[0]}");
                WriteUsage(Console.Error);
                return 2;
        }
    }

    private static int WindowsOnly(string Command){
        Console.Error.WriteLine($"The {Command} command runs on Windows only.");
        return 1;
    }

    private static void WriteUsage(TextWriter Writer){
        Writer.WriteLine("Usage: Cge <command> <arguments>");
        Writer.WriteLine("Run a command without arguments to see its own usage.");
        Writer.WriteLine();
        Writer.WriteLine("Commands:");
        foreach(var (Name,Summary) in Commands){
            Writer.WriteLine($"  {Name,-16}{Summary}");
        }
    }
}
