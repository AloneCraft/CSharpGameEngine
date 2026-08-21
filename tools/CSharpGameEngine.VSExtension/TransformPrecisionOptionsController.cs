using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dte=EnvDTE.DTE;
using RoslynDocument=Microsoft.CodeAnalysis.Document;
using RoslynSolution=Microsoft.CodeAnalysis.Solution;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;

namespace CSharpGameEngine.VSExtension;

/// <summary>Snapshot consumed by the WPF Options page; no value is persisted in VS user settings.</summary>
internal sealed class TransformPrecisionOptionsSnapshot {
    private TransformPrecisionOptionsSnapshot(
        TransformPrecisionTarget?Target,string ProjectDisplayName,string SourceDisplayPath,
        TransformPrecisionConfiguration?Configuration,string StatusMessage){
        this.Target=Target;
        this.ProjectDisplayName=ProjectDisplayName;
        this.SourceDisplayPath=SourceDisplayPath;
        this.Configuration=Configuration;
        this.StatusMessage=StatusMessage;
    }

    internal TransformPrecisionTarget?Target { get; }

    internal string ProjectDisplayName { get; }

    internal string SourceDisplayPath { get; }

    internal TransformPrecisionConfiguration?Configuration { get; }

    internal string StatusMessage { get; }

    internal bool CanApply=>this.Target is not null&&this.Configuration is not null;

    internal static TransformPrecisionOptionsSnapshot Available(
        TransformPrecisionTarget Target,TransformPrecisionConfiguration Configuration)=>
        new(Target,Target.ProjectDisplayName,Target.SourceFilePath,Configuration,
            "このSceneプロジェクトのソース定数を編集します。変更は通常のC#ソース変更として追跡されます。");

    internal static TransformPrecisionOptionsSnapshot Unavailable(string StatusMessage)=>
        new(null,"(Sceneプロジェクト未解決)","",null,StatusMessage);
}

/// <summary>Resolved writable source owner for a Scene project's precision constants.</summary>
internal sealed class TransformPrecisionTarget {
    internal TransformPrecisionTarget(ProjectId ProjectId,DocumentId DocumentId,string ProjectDisplayName,string SourceFilePath){
        this.ProjectId=ProjectId;
        this.DocumentId=DocumentId;
        this.ProjectDisplayName=ProjectDisplayName;
        this.SourceFilePath=SourceFilePath;
    }

    internal ProjectId ProjectId { get; }

    internal DocumentId DocumentId { get; }

    internal string ProjectDisplayName { get; }

    internal string SourceFilePath { get; }
}

/// <summary>Outcome of applying a precision request through the live Visual Studio Roslyn workspace.</summary>
internal sealed class TransformPrecisionApplyResult {
    private TransformPrecisionApplyResult(bool Succeeded,bool RebuildRequested,string?ErrorMessage,string?WarningMessage){
        this.Succeeded=Succeeded;
        this.RebuildRequested=RebuildRequested;
        this.ErrorMessage=ErrorMessage;
        this.WarningMessage=WarningMessage;
    }

    internal bool Succeeded { get; }

    internal bool RebuildRequested { get; }

    internal string?ErrorMessage { get; }

    internal string?WarningMessage { get; }

    internal static TransformPrecisionApplyResult Failure(string ErrorMessage)=>
        new(false,false,ErrorMessage,null);

    internal static TransformPrecisionApplyResult Success(bool RebuildRequested,string?WarningMessage)=>
        new(true,RebuildRequested,null,WarningMessage);
}

/// <summary>
/// Resolves the project that owns the currently opened Scene and edits the source canonical
/// constants through <see cref="VisualStudioWorkspace"/>.  The controller purposely does not use
/// <c>DialogPage</c> settings: precision belongs to the project and therefore source control.
/// </summary>
internal sealed class TransformPrecisionOptionsController {
    private readonly VisualStudioWorkspace Workspace;

    private TransformPrecisionOptionsController(VisualStudioWorkspace Workspace)=>this.Workspace=Workspace;

    internal static TransformPrecisionOptionsController?TryCreate(){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(Package.GetGlobalService(typeof(SComponentModel))is not IComponentModel ComponentModel){
            return null;
        }

        VisualStudioWorkspace?Workspace=ComponentModel.GetService<VisualStudioWorkspace>();
        return Workspace is null?null:new TransformPrecisionOptionsController(Workspace);
    }

    internal async Task<TransformPrecisionOptionsSnapshot> RefreshAsync(CancellationToken CancellationToken){
        string?SceneFilePath=SceneWorkspaceService.Instance?.CurrentModel?.FilePath;
        if(string.IsNullOrWhiteSpace(SceneFilePath)){
            return TransformPrecisionOptionsSnapshot.Unavailable(
                "現在開いているSceneを解決できないため、Transform Precisionは変更できません。");
        }

        RoslynDocument?SceneDocument=this.Workspace.CurrentSolution
            .GetDocumentIdsWithFilePath(SceneFilePath)
            .Select(this.Workspace.CurrentSolution.GetDocument)
            .FirstOrDefault(Candidate=>Candidate is not null);
        if(SceneDocument is null){
            return TransformPrecisionOptionsSnapshot.Unavailable(
                "現在のSceneはRoslyn Workspace内のプロジェクトに属していません。");
        }

        List<(RoslynDocument Document,TransformPrecisionSourceAnalysis Analysis)>Candidates=[];
        foreach(RoslynDocument Document in SceneDocument.Project.Documents){
            CancellationToken.ThrowIfCancellationRequested();
            string?DocumentFilePath=Document.FilePath;
            if(string.IsNullOrWhiteSpace(DocumentFilePath)
                ||DocumentFilePath!.EndsWith(".g.cs",StringComparison.OrdinalIgnoreCase)){
                continue;
            }

            SyntaxNode?Root=await Document.GetSyntaxRootAsync(CancellationToken).ConfigureAwait(false);
            if(Root is null
                ||!Root.DescendantNodes().OfType<ClassDeclarationSyntax>().Any(Declaration=>
                    string.Equals(
                        Declaration.Identifier.ValueText,
                        TransformPrecisionSourceEditor.PrecisionClassName,
                        StringComparison.Ordinal))){
                continue;
            }

            TransformPrecisionSourceAnalysis Analysis=TransformPrecisionSourceEditor.Analyze(Root);
            Candidates.Add((Document,Analysis));
        }

        if(Candidates.Count==0){
            return TransformPrecisionOptionsSnapshot.Unavailable(
                "このSceneプロジェクトに編集可能な GameEnginePrecision ソースがありません。");
        }

        if(Candidates.Count!=1){
            return TransformPrecisionOptionsSnapshot.Unavailable(
                "GameEnginePrecision のソースが複数見つかったため、編集対象を一意に決められません。");
        }

        (RoslynDocument PrecisionDocument,TransformPrecisionSourceAnalysis SourceAnalysis)=Candidates[0];
        if(!SourceAnalysis.IsEditable||SourceAnalysis.Configuration is null){
            return TransformPrecisionOptionsSnapshot.Unavailable(
                SourceAnalysis.ErrorMessage??"GameEnginePrecision のソースを編集できません。");
        }

        if(!IsWritableSourceFile(PrecisionDocument.FilePath!)){
            return TransformPrecisionOptionsSnapshot.Unavailable(
                "GameEnginePrecision は読み取り専用または生成ソースなので編集できません。");
        }

        TransformPrecisionTarget Target=new(
            PrecisionDocument.Project.Id,
            PrecisionDocument.Id,
            string.IsNullOrWhiteSpace(PrecisionDocument.Project.FilePath)
                ?PrecisionDocument.Project.Name
                :PrecisionDocument.Project.Name+" ("+PrecisionDocument.Project.FilePath+")",
            PrecisionDocument.FilePath!);
        return TransformPrecisionOptionsSnapshot.Available(Target,SourceAnalysis.Configuration.Value);
    }

    internal async Task<TransformPrecisionApplyResult> ApplyAsync(
        TransformPrecisionTarget Target,int TotalBits,int FractionalBits,CancellationToken CancellationToken){
        TransformPrecisionValidation Validation=TransformPrecisionPolicy.Validate(TotalBits,FractionalBits);
        if(!Validation.IsValid){
            return TransformPrecisionApplyResult.Failure(Validation.ErrorMessage!);
        }

        RoslynDocument?Document=this.Workspace.CurrentSolution.GetDocument(Target.DocumentId);
        if(Document is null
            ||!Document.Project.Id.Equals(Target.ProjectId)
            ||!string.Equals(Document.FilePath,Target.SourceFilePath,StringComparison.OrdinalIgnoreCase)
            ||!IsWritableSourceFile(Target.SourceFilePath)){
            return TransformPrecisionApplyResult.Failure(
                "GameEnginePrecision の対象ソースが現在のRoslyn Workspaceにないか、編集可能ではありません。");
        }

        SyntaxNode?Root=await Document.GetSyntaxRootAsync(CancellationToken).ConfigureAwait(false);
        if(Root is null){
            return TransformPrecisionApplyResult.Failure("GameEnginePrecision のソースを解析できません。");
        }

        TransformPrecisionSourceEditResult Edited=TransformPrecisionSourceEditor.Apply(Root,TotalBits,FractionalBits);
        if(!Edited.Succeeded||Edited.Root is null){
            return TransformPrecisionApplyResult.Failure(
                Edited.ErrorMessage??"GameEnginePrecision のソースを編集できません。");
        }

        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(CancellationToken);
        RoslynSolution Updated=this.Workspace.CurrentSolution.WithDocumentSyntaxRoot(Document.Id,Edited.Root);
        if(!this.Workspace.TryApplyChanges(Updated)){
            return TransformPrecisionApplyResult.Failure(
                "Visual StudioがGameEnginePrecisionのRoslyn変更を適用できませんでした。ソースを確認してください。");
        }

        try {
            if(Package.GetGlobalService(typeof(Dte))is Dte Dte){
                Dte.ExecuteCommand("Build.RebuildSolution","");
                return TransformPrecisionApplyResult.Success(true,null);
            }
        } catch(Exception Exception)when(Exception is InvalidOperationException||Exception is System.Runtime.InteropServices.COMException){
            return TransformPrecisionApplyResult.Success(
                false,"ソースは更新しましたが、Visual Studioへの再ビルド要求に失敗しました。通常のBuildで再生成してください。");
        }

        return TransformPrecisionApplyResult.Success(
            false,"ソースは更新しましたが、Visual StudioのBuildサービスを取得できません。通常のBuildで再生成してください。");
    }

    private static bool IsWritableSourceFile(string SourceFilePath){
        try {
            return File.Exists(SourceFilePath)
                &&(File.GetAttributes(SourceFilePath)&FileAttributes.ReadOnly)==0;
        } catch(IOException){
            return false;
        } catch(UnauthorizedAccessException){
            return false;
        }
    }
}
