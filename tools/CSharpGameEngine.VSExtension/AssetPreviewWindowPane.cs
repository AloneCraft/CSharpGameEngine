using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Editor DocView *and* DocData (deliberately the same object - a common pattern for simple,
/// read-only editors) for the Solution Explorer 3D asset preview (ADR-0013). Read-only: dirty
/// state is always false, save is unsupported. <see cref="LoadFromPath"/> is reachable from three
/// places (constructor, <see cref="IPersistFileFormat.Load"/>, <see cref="IVsPersistDocData.LoadDocData"/>)
/// so whichever the shell actually calls, the preview ends up pointed at the right file.
/// </summary>
internal sealed class AssetPreviewWindowPane : WindowPane, IVsPersistDocData, IPersistFileFormat
{
    private readonly AssetPreviewControl _control;
    private string _filePath;

    public AssetPreviewWindowPane(string filePath)
    {
        _filePath = filePath;
        _control = new AssetPreviewControl(filePath);
        Content = _control;
    }

    private int LoadFromPath(string path)
    {
        _filePath = path;
        _control.SetModelPath(path);
        return VSConstants.S_OK;
    }

    // IPersist (base interface of IPersistFileFormat - a distinct member from
    // IPersistFileFormat.GetClassID despite the identical signature/semantics).
    int IPersist.GetClassID(out Guid pClassID)
    {
        pClassID = typeof(GlbFbxPreviewEditorFactory).GUID;
        return VSConstants.S_OK;
    }

    // IPersistFileFormat
    int IPersistFileFormat.GetClassID(out Guid pClassID)
    {
        pClassID = typeof(GlbFbxPreviewEditorFactory).GUID;
        return VSConstants.S_OK;
    }

    int IPersistFileFormat.GetFormatList(out string ppszFormatList)
    {
        ppszFormatList = string.Empty;
        return VSConstants.S_OK;
    }

    int IPersistFileFormat.IsDirty(out int pfDirty)
    {
        pfDirty = 0;
        return VSConstants.S_OK;
    }

    int IPersistFileFormat.InitNew(uint nFormatIndex) => VSConstants.E_NOTIMPL;

    int IPersistFileFormat.Load(string pszFilename, uint grfMode, int fReadOnly) => LoadFromPath(pszFilename);

    int IPersistFileFormat.Save(string pszFilename, int fRemember, uint nFormatIndex) => VSConstants.E_NOTIMPL;

    int IPersistFileFormat.SaveCompleted(string pszFilename) => VSConstants.S_OK;

    int IPersistFileFormat.GetCurFile(out string ppszFilename, out uint pnFormatIndex)
    {
        ppszFilename = _filePath;
        pnFormatIndex = 0;
        return VSConstants.S_OK;
    }

    // IVsPersistDocData
    int IVsPersistDocData.GetGuidEditorType(out Guid pClassID)
    {
        pClassID = typeof(GlbFbxPreviewEditorFactory).GUID;
        return VSConstants.S_OK;
    }

    int IVsPersistDocData.IsDocDataDirty(out int pfDirty)
    {
        pfDirty = 0;
        return VSConstants.S_OK;
    }

    int IVsPersistDocData.SetUntitledDocPath(string pszDocDataPath) => VSConstants.E_NOTIMPL;

    int IVsPersistDocData.LoadDocData(string pszMkDocument) => LoadFromPath(pszMkDocument);

    int IVsPersistDocData.SaveDocData(VSSAVEFLAGS dwSave, out string pbstrMkDocumentNew, out int pfSaveCanceled)
    {
        pbstrMkDocumentNew = null!;
        pfSaveCanceled = 0;
        return VSConstants.S_OK;
    }

    int IVsPersistDocData.Close() => VSConstants.S_OK;

    int IVsPersistDocData.OnRegisterDocData(uint docCookie, IVsHierarchy pHierNew, uint itemidNew) => VSConstants.S_OK;

    int IVsPersistDocData.RenameDocData(uint grfAttribs, IVsHierarchy pHierNew, uint itemidNew, string pszMkDocumentNew) =>
        LoadFromPath(pszMkDocumentNew);

    int IVsPersistDocData.IsDocDataReloadable(out int pfReloadable)
    {
        pfReloadable = 1;
        return VSConstants.S_OK;
    }

    int IVsPersistDocData.ReloadDocData(uint grfFlags) => LoadFromPath(_filePath);
}
