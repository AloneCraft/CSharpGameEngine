using System;
using System.IO;
using Microsoft.VisualStudio.Shell.Interop;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class SceneDesignerDocumentWindowPolicyTests {
    [Fact]
    public void EditorTypeGuid_UsesGuidEmpty_ForUnregisteredDocumentPane(){
        Assert.Equal(Guid.Empty,SceneDesignerDocumentWindowPolicy.EditorTypeGuid);
    }

    [Fact]
    public void VirtualSceneDocument_UsesExternalFilesOwner_AndNeverPersists(){
        Assert.Equal(typeof(SVsExternalFilesManager),SceneDesignerDocumentWindowPolicy.HostServiceType);
        Assert.NotEqual(0u,SceneDesignerDocumentWindowPolicy.CreateFlags
            &(uint)__VSCREATEDOCWIN.CDW_fCreateNewWindow);
        Assert.NotEqual(0u,SceneDesignerDocumentWindowPolicy.CreateFlags
            &(uint)_VSRDTFLAGS.RDT_NonCreatable);
        Assert.NotEqual(0u,SceneDesignerDocumentWindowPolicy.CreateFlags
            &(uint)_VSRDTFLAGS.RDT_DontSave);
        Assert.NotEqual(0u,SceneDesignerDocumentWindowPolicy.CreateFlags
            &(uint)_VSRDTFLAGS.RDT_DontSaveAs);
        Assert.NotEqual(0u,SceneDesignerDocumentWindowPolicy.CreateFlags
            &(uint)_VSRDTFLAGS.RDT_DontAutoOpen);
        Assert.NotEqual(0u,SceneDesignerDocumentWindowPolicy.CreateFlags
            &(uint)_VSRDTFLAGS.RDT_VirtualDocument);
        Assert.NotEqual(0u,SceneDesignerDocumentWindowPolicy.CreateFlags
            &(uint)_VSRDTFLAGS.RDT_DontAddToMRU);
    }

    [Fact]
    public void AddDocumentRequest_ContainsTheCompleteVirtualDocumentContract(){
        var Key=new SceneKey("project-guid","global::Game.FirstPersonStageScene");

        SceneDesignerDocumentWindowRequest Request=SceneDesignerDocumentWindowPolicy.CreateRequest(Key);

        Assert.True(Path.IsPathRooted(Request.Moniker));
        Assert.EndsWith(".cgescene",Request.Moniker,StringComparison.OrdinalIgnoreCase);
        Assert.Equal(SceneDesignerDocumentWindowPolicy.CreateFlags,Request.Flags);
        Assert.Equal(Guid.Empty,Request.EditorTypeGuid);
        Assert.Equal(Guid.Empty,Request.CommandUiGuid);
        Assert.Equal(string.Empty,Request.PhysicalView);
        Assert.Equal(string.Empty,Request.EditorCaption);
        Assert.Equal("FirstPersonStageScene [Scene]",Request.OwnerCaption);
    }

    [Fact]
    public void AddDocumentResult_AcceptsSuccessOnlyWhenTheShellReturnsAFrame(){
        SceneDesignerDocumentWindowRequest Request=SceneDesignerDocumentWindowPolicy.CreateRequest(
            new SceneKey("project-guid","global::Game.FirstPersonStageScene"));

        SceneDesignerDocumentWindowPolicy.EnsureAddDocumentSucceeded(Request,0,new object());

        var MissingFrame=Assert.Throws<SceneDesignerDocumentWindowException>(()=>
            SceneDesignerDocumentWindowPolicy.EnsureAddDocumentSucceeded(Request,0,Frame:null));
        Assert.Equal(unchecked((int)0x8000FFFF),MissingFrame.ErrorCode);
        Assert.Equal("IVsExternalFilesManager.AddDocument returned no frame",MissingFrame.Operation);
    }

    [Fact]
    public void AddDocumentResult_PreservesTheShellFailureHResult(){
        SceneDesignerDocumentWindowRequest Request=SceneDesignerDocumentWindowPolicy.CreateRequest(
            new SceneKey("project-guid","global::Game.FirstPersonStageScene"));

        var Exception=Assert.Throws<SceneDesignerDocumentWindowException>(()=>
            SceneDesignerDocumentWindowPolicy.EnsureAddDocumentSucceeded(
                Request,unchecked((int)0x80070057),new object()));

        Assert.Equal(unchecked((int)0x80070057),Exception.ErrorCode);
        Assert.Equal("IVsExternalFilesManager.AddDocument",Exception.Operation);
    }

    [Fact]
    public void Failure_ContainsHResultAndEntireDocumentWindowContract(){
        var Exception=SceneDesignerDocumentWindowPolicy.CreateFailure(
            "IVsExternalFilesManager.AddDocument",@"C:\Temp\Scene.cgescene",unchecked((int)0x80070057));

        Assert.Equal(unchecked((int)0x80070057),Exception.ErrorCode);
        Assert.Contains("0x80070057",Exception.DiagnosticText);
        Assert.Contains(@"C:\Temp\Scene.cgescene",Exception.DiagnosticText);
        Assert.Contains("SVsExternalFilesManager",Exception.DiagnosticText);
        Assert.Contains("0x80070057",SceneDesignerDocumentWindowDiagnostics.CreateDetail(Exception));
    }
}
