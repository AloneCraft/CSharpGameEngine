using System.Reflection;
using System.Runtime.Loader;

namespace CSharpGameEngine.SceneAuthoring.Cli;

/// <summary>
/// Loads a target assembly out-of-process into a collectible <see cref="AssemblyLoadContext"/> and
/// actually executes its <c>[SceneDefinition]</c> class's generated <c>CreateRuntime(ref World)</c> -
/// not a textual/semantic re-derivation of what the code "probably" does, but the exact same compiled
/// code the real game runs, so any <c>Set(...)</c> initializer expression (constant references,
/// computed values, anything beyond simple literals) always resolves to its real value (ADR-0031).
///
/// <para>
/// Everything here is pure reflection over the <b>target's own</b> types (ADR-0033). Under the
/// closed-archetype design, <c>CSharpGameEngine.Entities</c> is consumed as shared source rather than
/// as a shared compiled assembly - <c>partial struct World</c> can only be completed inside each
/// consumer's own compilation - so every consuming project has its own distinct <c>World</c>/
/// <c>Entity</c> CLR types. The old trick of forcing the target to reuse this process's already-loaded
/// <c>CSharpGameEngine.Entities</c> so both sides shared one CLR type is therefore no longer possible,
/// and this bridge never names those types at compile time.
/// </para>
/// </summary>
internal static class SceneRuntimeInvoker
{
    /// <summary>
    /// <paramref name="Snapshot"/> is the boxed <c>ArchetypeSnapshotEntry[]</c> produced by the
    /// target's generated <c>WorldSnapshot.Capture</c>; it is read reflectively by
    /// <see cref="WorldInspector"/>. <paramref name="RootEntityId"/> is the root node's Entity Id.
    /// </summary>
    internal readonly record struct InvocationResult(Array Snapshot, int RootEntityId, string SceneClassFullName);

    private const string WorldTypeFullName = "CSharpGameEngine.Entities.World";
    private const string WorldSnapshotTypeFullName = "CSharpGameEngine.Entities.WorldSnapshot";

    internal static InvocationResult Invoke(string targetAssemblyPath, string sceneClassSimpleNameHint)
    {
        SceneAuthoringLoadContext context = new(targetAssemblyPath);
        Assembly targetAssembly = context.LoadFromAssemblyPath(targetAssemblyPath);

        Type sceneType = FindSceneDefinitionType(targetAssembly, sceneClassSimpleNameHint)
            ?? throw new InvalidOperationException($"'{targetAssemblyPath}' に [SceneDefinition] クラスが見つかりませんでした(ヒント: '{sceneClassSimpleNameHint}')。");

        MethodInfo createRuntime = sceneType.GetMethod("CreateRuntime", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException($"'{sceneType.FullName}' に CreateRuntime(ref World) が見つかりませんでした。");

        Type worldType = FindType(targetAssembly, WorldTypeFullName);
        object boxedWorld = Activator.CreateInstance(worldType)
            ?? throw new InvalidOperationException($"'{WorldTypeFullName}' のインスタンスを生成できませんでした。");

        // CreateRuntime takes `ref World` (World is a struct now), so the mutated World has to be read
        // back out of the argument array - reflection's standard by-ref calling convention.
        object?[] arguments = [boxedWorld];
        object? result = createRuntime.Invoke(null, arguments);
        boxedWorld = arguments[0]!;

        int rootEntityId = ReadEntityId(result)
            ?? throw new InvalidOperationException($"'{sceneType.FullName}.CreateRuntime' の戻り値が Entity ではありませんでした。");

        Type snapshotType = FindType(targetAssembly, WorldSnapshotTypeFullName);
        MethodInfo capture = snapshotType.GetMethod("Capture", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException($"'{WorldSnapshotTypeFullName}' に Capture(in World) が見つかりませんでした。");

        Array snapshot = (Array)capture.Invoke(null, [boxedWorld])!;

        string sceneClassFullName = sceneType.FullName ?? sceneType.Name;

        // Only a request, not an immediate teardown: the snapshot array's elements are the target
        // context's own types, so actual collection is deferred by the GC until the caller drops it.
        // Requesting it here still matters for the resident bridge server, where many requests share
        // one long-lived process (ADR-0031 flags repeated load/unload cycles as unverified).
        context.Unload();

        return new InvocationResult(snapshot, rootEntityId, sceneClassFullName);
    }

    private static Type FindType(Assembly targetAssembly, string fullName)
    {
        // The runtime types live in whichever assembly compiled Runtime.cs - normally the target
        // itself, but a target could also link it from a shared project it references.
        Type? type = targetAssembly.GetType(fullName);

        if (type is not null)
        {
            return type;
        }

        foreach (AssemblyName referenced in targetAssembly.GetReferencedAssemblies())
        {
            try
            {
                type = AssemblyLoadContext.GetLoadContext(targetAssembly)?.LoadFromAssemblyName(referenced).GetType(fullName);
            }
            catch (FileNotFoundException)
            {
                continue;
            }
            catch (BadImageFormatException)
            {
                continue;
            }

            if (type is not null)
            {
                return type;
            }
        }

        throw new InvalidOperationException($"対象アセンブリに '{fullName}' が見つかりませんでした(CSharpGameEngine.Entities のソースがリンクされていない可能性があります)。");
    }

    /// <summary><c>Entity</c> is the target's own CLR type, so its <c>Id</c> is read reflectively.</summary>
    internal static int? ReadEntityId(object? boxedEntity)
    {
        if (boxedEntity is null)
        {
            return null;
        }

        Type type = boxedEntity.GetType();

        if (type.FullName != "CSharpGameEngine.Entities.Entity")
        {
            return null;
        }

        PropertyInfo? idProperty = type.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance);

        return idProperty?.GetValue(boxedEntity) as int?;
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
/// Resolves the target's private dependency graph via <see cref="AssemblyDependencyResolver"/> (the
/// standard .NET plugin-loading pattern, reads the target's own <c>.deps.json</c>).
///
/// <para>
/// Unlike the pre-ADR-0033 version, this deliberately does <b>not</b> redirect
/// <c>CSharpGameEngine.Entities</c> to this process's own already-loaded copy: there is no shared
/// compiled runtime assembly anymore (see <see cref="SceneRuntimeInvoker"/>), so the target must load
/// its own. Everything the bridge reads back is handled reflectively for exactly that reason.
/// </para>
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
        string? path = this.resolver.ResolveAssemblyToPath(assemblyName);
        return path is not null ? this.LoadFromAssemblyPath(path) : null;
    }
}
