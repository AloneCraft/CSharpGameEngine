using System.Text.RegularExpressions;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Cheap text-only presence check for <c>[SceneDefinition]</c>, used purely to gate
/// <see cref="PreviewSceneDefinitionCommand"/>'s context-menu visibility (ADR-0031) - not a real
/// parse (that happens in <c>CSharpGameEngine.SceneAuthoring.Cli</c>'s
/// <c>SceneDefinitionSourceParser</c>, out-of-process). False negatives on unusual formatting are
/// acceptable here (the command simply would not appear); false positives are effectively harmless
/// (the command would appear but its <see cref="PreviewSceneDefinitionCommand.Execute"/> would report
/// a clear "no [SceneDefinition] class found" error from the bridge). Accepts the bare attribute name,
/// the <c>Attribute</c>-suffixed form, and the fully-qualified form.
/// </summary>
internal static class SceneDefinitionTextDetector
{
    private static readonly Regex Pattern = new(
        @"\[\s*(CSharpGameEngine\.Entities\.)?SceneDefinition(Attribute)?\s*(\(|\])",
        RegexOptions.Compiled);

    internal static bool LooksLikeSceneDefinition(string sourceText) => Pattern.IsMatch(sourceText);
}
