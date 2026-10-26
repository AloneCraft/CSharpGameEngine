using System;

namespace CSharpGameEngine.VSExtension;

/// <summary>テキストエディターのフォーカス喪失イベントが、実際の Inspector 編集を示すか判定します。</summary>
internal static class InspectorTextCommitPolicy {
    internal static bool HasChanged(string OriginalDisplayText,string CurrentText)=>
        !string.Equals(OriginalDisplayText,CurrentText,StringComparison.Ordinal);
}