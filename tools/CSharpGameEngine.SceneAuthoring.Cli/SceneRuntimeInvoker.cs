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

    internal static InvocationResult Invoke(string TargetAssemblyPath, string SceneClassSimpleNameHint)
    {
        SceneAuthoringLoadContext Context = new(TargetAssemblyPath);
        Assembly TargetAssembly = Context.LoadFromAssemblyPath(TargetAssemblyPath);

        Type SceneType = FindSceneDefinitionType(TargetAssembly, SceneClassSimpleNameHint)
            ?? throw new InvalidOperationException($"'{TargetAssemblyPath}' に [SceneDefinition] クラスが見つかりませんでした(ヒント: '{SceneClassSimpleNameHint}')。");

        MethodInfo CreateRuntime = SceneType.GetMethod("CreateRuntime", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException($"'{SceneType.FullName}' に CreateRuntime(ref World) が見つかりませんでした。");

        Type WorldType = FindType(TargetAssembly, WorldTypeFullName);
        object BoxedWorld = Activator.CreateInstance(WorldType)
            ?? throw new InvalidOperationException($"'{WorldTypeFullName}' のインスタンスを生成できませんでした。");

        // CreateRuntime takes `ref World` (World is a struct now), so the mutated World has to be read
        // back out of the argument array - reflection's standard by-ref calling convention.
        object?[] Arguments = [BoxedWorld];
        object? Result = CreateRuntime.Invoke(null, Arguments);
        BoxedWorld = Arguments[0]!;

        int RootEntityId = ReadEntityId(Result)
            ?? throw new InvalidOperationException($"'{SceneType.FullName}.CreateRuntime' の戻り値が Entity ではありませんでした。");

        Type SnapshotType = FindType(TargetAssembly, WorldSnapshotTypeFullName);
        MethodInfo Capture = SnapshotType.GetMethod("Capture", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException($"'{WorldSnapshotTypeFullName}' に Capture(in World) が見つかりませんでした。");

        Array Snapshot = (Array)Capture.Invoke(null, [BoxedWorld])!;

        string SceneClassFullName = SceneType.FullName ?? SceneType.Name;

        // Only a request, not an immediate teardown: the snapshot array's elements are the target
        // context's own types, so actual collection is deferred by the GC until the caller drops it.
        // Requesting it here still matters for the resident bridge server, where many requests share
        // one long-lived process (ADR-0031 flags repeated load/unload cycles as unverified).
        Context.Unload();

        return new InvocationResult(Snapshot, RootEntityId, SceneClassFullName);
    }

    private static Type FindType(Assembly TargetAssembly, string FullName)
    {
        // The runtime types live in whichever assembly compiled Runtime.cs - normally the target
        // itself, but a target could also link it from a shared project it references.
        Type? type = TargetAssembly.GetType(FullName);

        if (type is not null)
        {
            return type;
        }

        foreach (AssemblyName Referenced in TargetAssembly.GetReferencedAssemblies())
        {
            try
            {
                type = AssemblyLoadContext.GetLoadContext(TargetAssembly)?.LoadFromAssemblyName(Referenced).GetType(FullName);
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

        throw new InvalidOperationException($"対象アセンブリに '{FullName}' が見つかりませんでした(CSharpGameEngine.Entities のソースがリンクされていない可能性があります)。");
    }

    /// <summary><c>Entity</c> is the target's own CLR type, so its <c>Id</c> is read reflectively.</summary>
    internal static int? ReadEntityId(object? BoxedEntity)
    {
        if (BoxedEntity is null)
        {
            return null;
        }

        Type type = BoxedEntity.GetType();

        if (type.FullName != "CSharpGameEngine.Entities.Entity")
        {
            return null;
        }

        PropertyInfo? IdProperty = type.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance);

        return IdProperty?.GetValue(BoxedEntity) as int?;
    }

    private static Type? FindSceneDefinitionType(Assembly assembly, string SimpleNameHint)
    {
        const string SceneDefinitionAttributeFullName = "CSharpGameEngine.Entities.SceneDefinitionAttribute";

        List<Type> Candidates = [];

        foreach (Type type in assembly.GetTypes())
        {
            foreach (CustomAttributeData attribute in type.GetCustomAttributesData())
            {
                if (attribute.AttributeType.FullName == SceneDefinitionAttributeFullName)
                {
                    Candidates.Add(type);
                    break;
                }
            }
        }

        Type? ByHint = Candidates.Find(T => T.Name == SimpleNameHint);

        if (ByHint is not null)
        {
            return ByHint;
        }

        return Candidates.Count > 0 ? Candidates[0] : null;
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
    private readonly AssemblyDependencyResolver Resolver;

    internal SceneAuthoringLoadContext(string TargetAssemblyPath)
        : base(name: "ScenePreview", isCollectible: true)
    {
        this.Resolver = new AssemblyDependencyResolver(TargetAssemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        string? path = this.Resolver.ResolveAssemblyToPath(assemblyName);
        return path is not null ? this.LoadFromAssemblyPath(path) : null;
    }
}
