using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Editor DocView *and* DocData for the Solution Explorer <c>*.scene.json</c> preview (ADR-0020) -
/// structurally identical to <see cref="AssetPreviewWindowPane"/> (ADR-0013): read-only, dirty state
/// always false, save unsupported. <see cref="LoadFromPath"/> is reachable from the same three
/// places (constructor, <see cref="IPersistFileFormat.Load"/>, <see cref="IVsPersistDocData.LoadDocData"/>).
/// </summary>
internal sealed class SceneWindowPane : WindowPane, IVsPersistDocData, IPersistFileFormat
{
    private readonly SceneWindowControl Control;
    private string FilePath;

    public SceneWindowPane(string FilePath)
    {
        this.FilePath = FilePath;
        Control = new SceneWindowControl(FilePath);
        Content = Control;
    }

    private int LoadFromPath(string Path)
    {
        FilePath = Path;
        Control.SetScenePath(Path);
        return VSConstants.S_OK;
    }

    // IPersist (base interface of IPersistFileFormat - a distinct member from
    // IPersistFileFormat.GetClassID despite the identical signature/semantics).
    int IPersist.GetClassID(out Guid PClassID)
    {
        PClassID = typeof(SceneFileEditorFactory).GUID;
        return VSConstants.S_OK;
    }

    // IPersistFileFormat
    int IPersistFileFormat.GetClassID(out Guid PClassID)
    {
        PClassID = typeof(SceneFileEditorFactory).GUID;
        return VSConstants.S_OK;
    }

    int IPersistFileFormat.GetFormatList(out string PpszFormatList)
    {
        PpszFormatList = string.Empty;
        return VSConstants.S_OK;
    }

    int IPersistFileFormat.IsDirty(out int PfDirty)
    {
        PfDirty = 0;
        return VSConstants.S_OK;
    }

    int IPersistFileFormat.InitNew(uint NFormatIndex) => VSConstants.E_NOTIMPL;

    int IPersistFileFormat.Load(string PszFilename, uint GrfMode, int FReadOnly) => LoadFromPath(PszFilename);

    int IPersistFileFormat.Save(string PszFilename, int FRemember, uint NFormatIndex) => VSConstants.E_NOTIMPL;

    int IPersistFileFormat.SaveCompleted(string PszFilename) => VSConstants.S_OK;

    int IPersistFileFormat.GetCurFile(out string PpszFilename, out uint PnFormatIndex)
    {
        PpszFilename = FilePath;
        PnFormatIndex = 0;
        return VSConstants.S_OK;
    }

    // IVsPersistDocData
    int IVsPersistDocData.GetGuidEditorType(out Guid PClassID)
    {
        PClassID = typeof(SceneFileEditorFactory).GUID;
        return VSConstants.S_OK;
    }

    int IVsPersistDocData.IsDocDataDirty(out int PfDirty)
    {
        PfDirty = 0;
        return VSConstants.S_OK;
    }

    int IVsPersistDocData.SetUntitledDocPath(string PszDocDataPath) => VSConstants.E_NOTIMPL;

    int IVsPersistDocData.LoadDocData(string PszMkDocument) => LoadFromPath(PszMkDocument);

    int IVsPersistDocData.SaveDocData(VSSAVEFLAGS DwSave, out string PbstrMkDocumentNew, out int PfSaveCanceled)
    {
        PbstrMkDocumentNew = null!;
        PfSaveCanceled = 0;
        return VSConstants.S_OK;
    }

    int IVsPersistDocData.Close() => VSConstants.S_OK;

    int IVsPersistDocData.OnRegisterDocData(uint DocCookie, IVsHierarchy PHierNew, uint ItemidNew) => VSConstants.S_OK;

    int IVsPersistDocData.RenameDocData(uint GrfAttribs, IVsHierarchy PHierNew, uint ItemidNew, string PszMkDocumentNew) =>
        LoadFromPath(PszMkDocumentNew);

    int IVsPersistDocData.IsDocDataReloadable(out int PfReloadable)
    {
        PfReloadable = 1;
        return VSConstants.S_OK;
    }

    int IVsPersistDocData.ReloadDocData(uint GrfFlags) => LoadFromPath(FilePath);
}
