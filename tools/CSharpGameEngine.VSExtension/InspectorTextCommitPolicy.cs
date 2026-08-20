using System;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Decides whether a text editor's lost-focus event represents an actual Inspector edit.
/// </summary>
internal static class InspectorTextCommitPolicy {
    internal static bool HasChanged(string OriginalDisplayText,string CurrentText)=>
        !string.Equals(OriginalDisplayText,CurrentText,StringComparison.Ordinal);
}