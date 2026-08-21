using System;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class SceneDesignerDocumentWindowPolicyTests {
    [Fact]
    public void EditorTypeGuid_UsesGuidEmpty_ForUnregisteredDocumentPane(){
        Assert.Equal(Guid.Empty,SceneDesignerDocumentWindowPolicy.EditorTypeGuid);
    }
}
