using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;

namespace CSharpGameEngine.VSExtension;

/// <summary>
/// Registers a Solution Explorer editor for <c>.glb</c>/<c>.gltf</c>/<c>.fbx</c> files that shows
/// an embedded 3D preview (ADR-0013) - Unity/UE-style asset browser preview, reusing the same
/// in-process renderer as the manual viewer tool window (ADR-0007/0008/0010) via
/// <see cref="MeshLoader"/>/<see cref="AssetPreviewHwndHost"/>.
/// </summary>
[Guid(FactoryGuidString)]
internal sealed class GlbFbxPreviewEditorFactory : IVsEditorFactory
{
    public const string FactoryGuidString = "6a2f0f1f-3b6a-4e6f-9b1b-3a6a2f0f1f3b";

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
        PbstrEditorCaption = string.Empty;
        PguidCmdUI = Guid.Empty;
        PgrfCDW = 0;

        if (PunkDocDataExisting != IntPtr.Zero)
        {
            // We don't know how to adopt someone else's docData - only ever create fresh.
            return VSConstants.VS_E_INCOMPATIBLEDOCDATA;
        }

        AssetPreviewWindowPane Pane = new(PszMkDocument);
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
