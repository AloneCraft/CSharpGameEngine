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

    public int SetSite(Microsoft.VisualStudio.OLE.Interop.IServiceProvider Psp) => VSConstants.S_OK;

    public int Close() => VSConstants.S_OK;

    public int CreateEditorInstance(
        uint GrfCreateDoc,
        string PszMkDocument,
        string PszPhysicalView,
        IVsHierarchy PvHier,
        uint Itemid,
        IntPtr PunkDocDataExisting,
        out IntPtr PpunkDocView,
        out IntPtr PpunkDocData,
        out string PbstrEditorCaption,
        out Guid PguidCmdUI,
        out int PgrfCDW)
    {
        PpunkDocView = IntPtr.Zero;
        PpunkDocData = IntPtr.Zero;
        // Unlike GlbFbxPreviewEditorFactory (which leaves this empty), name the tab explicitly - the
        // scene document window is otherwise indistinguishable from any other editor on a plain
        // filename. VS appends this after the base file name, e.g. "DefaultScene.scene [シーン]".
        PbstrEditorCaption = "シーン";
        PguidCmdUI = Guid.Empty;
        PgrfCDW = 0;

        if (PunkDocDataExisting != IntPtr.Zero)
        {
            // We don't know how to adopt someone else's docData - only ever create fresh.
            return VSConstants.VS_E_INCOMPATIBLEDOCDATA;
        }

        SceneWindowPane Pane = new(PszMkDocument);
        PpunkDocView = Marshal.GetIUnknownForObject(Pane);
        PpunkDocData = Marshal.GetIUnknownForObject(Pane);
        return VSConstants.S_OK;
    }

    public int MapLogicalView(ref Guid RguidLogicalView, out string PbstrPhysicalView)
    {
        PbstrPhysicalView = null!;
        return RguidLogicalView == VSConstants.LOGVIEWID_Primary || RguidLogicalView == VSConstants.LOGVIEWID_Designer
            ? VSConstants.S_OK
            : VSConstants.E_NOTIMPL;
    }
}
