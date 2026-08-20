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
internal sealed class AssetPreviewWindowPane:WindowPane,IVsPersistDocData,IPersistFileFormat {
    private readonly AssetPreviewControl Control;
    private string FilePath;

    public AssetPreviewWindowPane(string FilePath){
        this.FilePath=FilePath;
        this.Control=new AssetPreviewControl(FilePath);
        this.Content= this.Control;
    }

    private int LoadFromPath(string Path){
        this.FilePath=Path;
        this.Control.SetModelPath(Path);
        return VSConstants.S_OK;
    }

    // IPersist (base interface of IPersistFileFormat - a distinct member from
    // IPersistFileFormat.GetClassID despite the identical signature/semantics).
    int IPersist.GetClassID(out Guid PClassID){
        PClassID=typeof(GlbFbxPreviewEditorFactory).GUID;
        return VSConstants.S_OK;
    }

    // IPersistFileFormat
    int IPersistFileFormat.GetClassID(out Guid PClassID){
        PClassID=typeof(GlbFbxPreviewEditorFactory).GUID;
        return VSConstants.S_OK;
    }

    int IPersistFileFormat.GetFormatList(out string PpszFormatList){
        PpszFormatList=string.Empty;
        return VSConstants.S_OK;
    }

    int IPersistFileFormat.IsDirty(out int PfDirty){
        PfDirty=0;
        return VSConstants.S_OK;
    }

    int IPersistFileFormat.InitNew(uint NFormatIndex)=>VSConstants.E_NOTIMPL;

    int IPersistFileFormat.Load(string PszFilename,uint GrfMode,int FReadOnly)=> this.LoadFromPath(PszFilename);

    int IPersistFileFormat.Save(string PszFilename,int FRemember,uint NFormatIndex)=>VSConstants.E_NOTIMPL;

    int IPersistFileFormat.SaveCompleted(string PszFilename)=>VSConstants.S_OK;

    int IPersistFileFormat.GetCurFile(out string PpszFilename,out uint PnFormatIndex){
        PpszFilename= this.FilePath;
        PnFormatIndex=0;
        return VSConstants.S_OK;
    }

    // IVsPersistDocData
    int IVsPersistDocData.GetGuidEditorType(out Guid PClassID){
        PClassID=typeof(GlbFbxPreviewEditorFactory).GUID;
        return VSConstants.S_OK;
    }

    int IVsPersistDocData.IsDocDataDirty(out int PfDirty){
        PfDirty=0;
        return VSConstants.S_OK;
    }

    int IVsPersistDocData.SetUntitledDocPath(string PszDocDataPath)=>VSConstants.E_NOTIMPL;

    int IVsPersistDocData.LoadDocData(string PszMkDocument)=> this.LoadFromPath(PszMkDocument);

    int IVsPersistDocData.SaveDocData(VSSAVEFLAGS DwSave,out string PbstrMkDocumentNew,out int PfSaveCanceled){
        PbstrMkDocumentNew=null!;
        PfSaveCanceled=0;
        return VSConstants.S_OK;
    }

    int IVsPersistDocData.Close()=>VSConstants.S_OK;

    int IVsPersistDocData.OnRegisterDocData(uint DocCookie,IVsHierarchy PHierNew,uint ItemidNew)=>VSConstants.S_OK;

    int IVsPersistDocData.RenameDocData(uint GrfAttribs,IVsHierarchy PHierNew,uint ItemidNew,string PszMkDocumentNew)=> this.LoadFromPath(PszMkDocumentNew);

    int IVsPersistDocData.IsDocDataReloadable(out int PfReloadable){
        PfReloadable=1;
        return VSConstants.S_OK;
    }

    int IVsPersistDocData.ReloadDocData(uint GrfFlags)=> this.LoadFromPath(this.FilePath);
}
