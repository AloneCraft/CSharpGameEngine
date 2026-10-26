using System.IO;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// VS シェルの状態から追跡対象のドキュメントパスを選択する。ツールウィンドウが null またはファイルではないフレームモニカーを公開する場合は、アクティブなテキストビュー、次に直前のコードファイルを使う。
/// </summary>
internal static class ActiveDocumentPathSelector {
    internal static string?Select(string?FrameMoniker,string?TextViewPath,string?PreviousPath){
        if(!string.IsNullOrWhiteSpace(FrameMoniker)&&Path.IsPathRooted(FrameMoniker)){
            return FrameMoniker;
        }

        return !string.IsNullOrWhiteSpace(TextViewPath)?TextViewPath:PreviousPath;
    }
}
