using System.IO;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Selects the document path to track from VS shell state. Tool windows may expose a null or
/// non-file frame moniker; in that case the active/last text view wins, then the previous code file.
/// </summary>
internal static class ActiveDocumentPathSelector {
    internal static string?Select(string?FrameMoniker,string?TextViewPath,string?PreviousPath){
        if(!string.IsNullOrWhiteSpace(FrameMoniker)&&Path.IsPathRooted(FrameMoniker)){
            return FrameMoniker;
        }

        return !string.IsNullOrWhiteSpace(TextViewPath)?TextViewPath:PreviousPath;
    }
}
