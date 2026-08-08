using System.Reflection;
using System.Runtime.Loader;
using CSharpGameEngine.Entities;

namespace CSharpGameEngine.SceneAuthoring.Cli;

/// <summary>
/// Loads a target assembly out-of-process into a collectible <see cref="AssemblyLoadContext"/> and
/// actually executes its <c>[SceneDefinition]</c> class's generated <c>CreateRuntime(World)</c> -
/// not a textual/semantic re-derivation of what the code "probably" does, but the exact same compiled
/// code the real game runs, so any <c>Set(...)</c> initializer expression (constant references,
/// computed values, anything beyond simple literals) always resolves to its real value (ADR-0031).
/// </summary>
internal static class SceneRuntimeInvoker
{
    internal readonly record struct InvocationResult(World World, Entity Root, string SceneClassFullName);

    internal static InvocationResult Invoke(string targetAssemblyPath, string sceneClassSimpleNameHint)
    {
        SceneAuthoringLoadContext context = new(targetAssemblyPath);
        Assembly targetAssembly = context.LoadFromAssemblyPath(targetAssemblyPath);

        Type sceneType = FindSceneDefinitionType(targetAssembly, sceneClassSimpleNameHint)
            ?? throw new InvalidOperationException($"'{targetAssemblyPath}' に [SceneDefinition] クラスが見つかりませんでした(ヒント: '{sceneClassSimpleNameHint}')。");

        MethodInfo createRuntime = sceneType.GetMethod("CreateRuntime", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException($"'{sceneType.FullName}' に CreateRuntime(World) が見つかりませんでした。");

        World world = new();
        object? result = createRuntime.Invoke(null, [world]);

        if (result is not Entity root)
        {
            throw new InvalidOperationException($"'{sceneType.FullName}.CreateRuntime' の戻り値が Entity ではありませんでした。");
        }

        string sceneClassFullName = sceneType.FullName ?? sceneType.Name;

        // Only a request, not an immediate teardown: the caller still holds `world` (which
        // transitively references this context's loaded component types through its archetypes), so
        // actual collection is deferred by the GC until that reference is dropped. Requesting it here
        // still matters for the resident bridge server, where many requests share one long-lived
        // process (ADR-0031 flags repeated load/unload cycles under that scenario as unverified).
        context.Unload();

        return new InvocationResult(world, root, sceneClassFullName);
    }

    private static Type? FindSceneDefinitionType(Assembly assembly, string simpleNameHint)
    {
        const string SceneDefinitionAttributeFullName = "CSharpGameEngine.Entities.SceneDefinitionAttribute";

        List<Type> candidates = [];

        foreach (Type type in assembly.GetTypes())
        {
            foreach (CustomAttributeData attribute in type.GetCustomAttributesData())
            {
                if (attribute.AttributeType.FullName == SceneDefinitionAttributeFullName)
                {
                    candidates.Add(type);
                    break;
                }
            }
        }

        Type? byHint = candidates.Find(t => t.Name == simpleNameHint);

        if (byHint is not null)
        {
            return byHint;
        }

        return candidates.Count > 0 ? candidates[0] : null;
    }
}

/// <summary>
/// Prefers whatever this bridge process itself already has loaded into the default context
/// (<c>CSharpGameEngine.Entities</c> in particular, which this project references directly) so that
/// e.g. the target's <c>CreateRuntime(World)</c> parameter type and the <see cref="World"/> instance
/// constructed in <see cref="SceneRuntimeInvoker"/> are the *same* CLR type, not two independently
/// loaded copies that fail to unify. Falls back to <see cref="AssemblyDependencyResolver"/> (the
/// standard .NET plugin-loading pattern, reads the target's own <c>.deps.json</c>) for everything
/// else, so the target's full private dependency graph resolves correctly.
/// </summary>
internal sealed class SceneAuthoringLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver resolver;

    internal SceneAuthoringLoadContext(string targetAssemblyPath)
        : base(name: "ScenePreview", isCollectible: true)
    {
        this.resolver = new AssemblyDependencyResolver(targetAssemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        foreach (Assembly loaded in AssemblyLoadContext.Default.Assemblies)
        {
            if (AssemblyName.ReferenceMatchesDefinition(loaded.GetName(), assemblyName))
            {
                return loaded;
            }
        }

        string? path = this.resolver.ResolveAssemblyToPath(assemblyName);
        return path is not null ? this.LoadFromAssemblyPath(path) : null;
    }
}
