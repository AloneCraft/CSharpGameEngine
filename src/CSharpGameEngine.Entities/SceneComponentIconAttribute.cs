using System;

namespace CSharpGameEngine.Entities;

/// <summary>
/// Associates a source-authored component <see langword="struct"/> with an icon that lives in the
/// owning project.  The value is intentionally just authoring metadata: it is consumed by the
/// Visual Studio extension and has no runtime or scene-generation behaviour.
/// </summary>
[AttributeUsage(AttributeTargets.Struct,AllowMultiple=false,Inherited=false)]
public sealed class SceneComponentIconAttribute(string ProjectRelativePath) : Attribute {
    /// <summary>
    /// Initializes the attribute with a project-relative <c>.svg</c> or <c>.png</c> asset path.
    /// Path validation is performed by the authoring UI when the attribute is written and by its
    /// reader when an icon is rendered, so hand-written invalid values safely fall back to the
    /// standard component glyph.
    /// </summary>
    /// <summary>Project-relative source asset path selected for this component.</summary>
    public string ProjectRelativePath { get; }=ProjectRelativePath??throw new ArgumentNullException(nameof(ProjectRelativePath));
}
