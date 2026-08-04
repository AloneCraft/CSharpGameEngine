using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Registers a Solution Explorer editor for <c>*.scene</c> that shows the "シーン" preview document
/// window (ADR-0020, extension corrected in ADR-0021 - <c>.scene.json</c> is a compound two-dot
/// extension that <c>ProvideEditorExtension</c> never actually matched, so double-click silently did
/// nothing until this fix) - the same <see cref="IVsEditorFactory"/>/<c>WindowPane</c> DocView==DocData
/// pattern as <see cref="GlbFbxPreviewEditorFactory"/> (ADR-0013), applied to scene files instead of
/// raw mesh files.
/// </summary>
[Guid(FactoryGuidString)]
internal sealed class SceneFileEditorFactory : IVsEditorFactory
{
    public const string FactoryGuidString = "2a3f932f-0897-457e-a74f-815bda2fd1e9";

    public int SetSite(Microsoft.VisualStudio.OLE.Interop.IServiceProvider psp) => VSConstants.S_OK;

    public int Close() => VSConstants.S_OK;

    public int CreateEditorInstance(
        uint grfCreateDoc,
        string pszMkDocument,
        string pszPhysicalView,
        IVsHierarchy pvHier,
        uint itemid,
        IntPtr punkDocDataExisting,
        out IntPtr ppunkDocView,
        out IntPtr ppunkDocData,
        out string pbstrEditorCaption,
        out Guid pguidCmdUI,
        out int pgrfCDW)
    {
        ppunkDocView = IntPtr.Zero;
        ppunkDocData = IntPtr.Zero;
        // Unlike GlbFbxPreviewEditorFactory (which leaves this empty), name the tab explicitly - the
        // scene document window is otherwise indistinguishable from any other editor on a plain
        // filename. VS appends this after the base file name, e.g. "DefaultScene.scene [シーン]".
        pbstrEditorCaption = "シーン";
        pguidCmdUI = Guid.Empty;
        pgrfCDW = 0;

        if (punkDocDataExisting != IntPtr.Zero)
        {
            // We don't know how to adopt someone else's docData - only ever create fresh.
            return VSConstants.VS_E_INCOMPATIBLEDOCDATA;
        }

        SceneWindowPane pane = new(pszMkDocument);
        ppunkDocView = Marshal.GetIUnknownForObject(pane);
        ppunkDocData = Marshal.GetIUnknownForObject(pane);
        return VSConstants.S_OK;
    }

    public int MapLogicalView(ref Guid rguidLogicalView, out string pbstrPhysicalView)
    {
        pbstrPhysicalView = null!;
        return rguidLogicalView == VSConstants.LOGVIEWID_Primary || rguidLogicalView == VSConstants.LOGVIEWID_Designer
            ? VSConstants.S_OK
            : VSConstants.E_NOTIMPL;
    }
}
