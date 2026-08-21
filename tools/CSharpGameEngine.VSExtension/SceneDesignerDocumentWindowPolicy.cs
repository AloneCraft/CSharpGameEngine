using System;

namespace CSharpGameEngine.VSExtension;

internal static class SceneDesignerDocumentWindowPolicy {
    /// <summary>Ad-hoc window panes have no registered editor factory.</summary>
    internal static Guid EditorTypeGuid=>Guid.Empty;
}
