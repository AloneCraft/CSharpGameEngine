using System;
using System.Diagnostics;
using System.IO;
using Microsoft.VisualStudio.Shell;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// ActivityLog is present only when devenv is started with /Log. Keep a small, deterministic
/// companion log so a Document Well failure remains diagnosable when the package is loaded from a
/// normal solution launch or an F5 extension session.
/// </summary>
internal static class SceneDesignerDocumentWindowDiagnostics {
    private static readonly object Gate=new object();

    internal static string PersistentLogPath=>Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CSharpGameEngine","Logs","SceneDesignerDocumentWindow.log");

    internal static void ReportFailure(Exception Exception){
        string Detail=CreateDetail(Exception);
        try { ActivityLog.LogError(nameof(SceneDesignerDocumentWindowDiagnostics),Detail); }
        catch { /* Diagnostics must never hide the original document-window failure. */ }

        try {
            string?LogDirectory=Path.GetDirectoryName(PersistentLogPath);
            if(!string.IsNullOrWhiteSpace(LogDirectory)){ System.IO.Directory.CreateDirectory(LogDirectory); }
            lock(Gate){ File.AppendAllText(PersistentLogPath,Detail+Environment.NewLine); }
        } catch(Exception LogException){
            Trace.WriteLine($"Scene Designer Document Well diagnostic write failed: {LogException}");
        }
    }

    internal static string CompactDiagnostic(Exception Exception)=>
        Exception is SceneDesignerDocumentWindowException DocumentException
            ?$"{DocumentException.Operation}, HRESULT {SceneDesignerDocumentWindowPolicy.FormatHResult(DocumentException.ErrorCode)}"
            :$"{Exception.GetType().Name}, HRESULT {SceneDesignerDocumentWindowPolicy.FormatHResult(Exception.HResult)}";

    internal static string CreateDetail(Exception Exception){
        string Contract=Exception is SceneDesignerDocumentWindowException DocumentException
            ?DocumentException.DiagnosticText
            :$"operation=unclassified; HRESULT={SceneDesignerDocumentWindowPolicy.FormatHResult(Exception.HResult)}; "
                +$"flags={SceneDesignerDocumentWindowPolicy.FormatHResult(unchecked((int)SceneDesignerDocumentWindowPolicy.CreateFlags))}; "
                +$"editor={SceneDesignerDocumentWindowPolicy.EditorTypeGuid:B}; commandUi={Guid.Empty:B}; "
                +$"hostService={SceneDesignerDocumentWindowPolicy.HostServiceType.FullName}.";
        return $"{DateTime.UtcNow:O} {Contract}{Environment.NewLine}{Exception}";
    }
}
