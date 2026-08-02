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
        pbstrEditorCaption = string.Empty;
        pguidCmdUI = Guid.Empty;
        pgrfCDW = 0;

        if (punkDocDataExisting != IntPtr.Zero)
        {
            // We don't know how to adopt someone else's docData - only ever create fresh.
            return VSConstants.VS_E_INCOMPATIBLEDOCDATA;
        }

        AssetPreviewWindowPane pane = new(pszMkDocument);
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
