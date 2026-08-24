using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using CSharpGameEngine.SceneAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities;

namespace CSharpGameEngine.VSExtension;

/// <summary>All information needed to edit the component declaration represented by a glyph.</summary>
internal sealed class ComponentIconGlyphTarget {
    internal ComponentIconGlyphTarget(
        ITextBuffer Buffer,DocumentId DocumentId,ProjectId ProjectId,string FilePath,string ProjectDirectory,
        int DeclarationSpanStart,string FullyQualifiedTypeName,SceneVisualIcon Icon){
        this.Buffer=Buffer;
        this.DocumentId=DocumentId;
        this.ProjectId=ProjectId;
        this.FilePath=FilePath;
        this.ProjectDirectory=ProjectDirectory;
        this.DeclarationSpanStart=DeclarationSpanStart;
        this.FullyQualifiedTypeName=FullyQualifiedTypeName;
        this.Icon=Icon;
    }

    internal ITextBuffer Buffer { get; }

    internal DocumentId DocumentId { get; }

    internal ProjectId ProjectId { get; }

    internal string FilePath { get; }

    internal string ProjectDirectory { get; }

    internal int DeclarationSpanStart { get; }

    internal string FullyQualifiedTypeName { get; }

    internal SceneVisualIcon Icon { get; }
}

/// <summary>Glyph-margin tag for a source component declaration.</summary>
internal sealed class ComponentIconGlyphTag : IGlyphTag {
    internal ComponentIconGlyphTag(ComponentIconGlyphTarget Target)=>this.Target=Target;

    internal ComponentIconGlyphTarget Target { get; }
}

/// <summary>
/// MEF tagger for the native C# glyph margin.  It emits tags only for addable component structs
/// declared by the currently open writable source document; metadata/referenced components never
/// have a source declaration in this buffer and therefore cannot show an editing glyph.
/// </summary>
[Export(typeof(ITaggerProvider))]
[ContentType("CSharp")]
[ContentType("code")]
[TagType(typeof(ComponentIconGlyphTag))]
internal sealed class ComponentIconGlyphTaggerProvider : ITaggerProvider {
    [Import]
    internal IComponentModel ComponentModel { get;set;}=null!;

    [Import]
    internal ITextDocumentFactoryService TextDocumentFactoryService { get;set;}=null!;

    public ITagger<T>?CreateTagger<T>(ITextBuffer Buffer)where T:ITag {
        if(!this.TextDocumentFactoryService.TryGetTextDocument(Buffer,out var Document)
            ||!ComponentIconGlyphTagger.IsWritableSourceFile(Document.FilePath)){
            return null;
        }

        var Workspace=this.ComponentModel.GetService<VisualStudioWorkspace>();
        if(Workspace is null){
            return null;
        }

        return Buffer.Properties.GetOrCreateSingletonProperty(
            ()=>new ComponentIconGlyphTagger(Buffer,Document.FilePath,Workspace))as ITagger<T>;
    }
}

internal sealed class ComponentIconGlyphTagger : ITagger<ComponentIconGlyphTag> {
    private readonly ITextBuffer Buffer;
    private readonly string FilePath;
    private readonly VisualStudioWorkspace Workspace;
    private readonly object Gate=new();
    private IReadOnlyList<TrackedGlyph>Glyphs=[];
    private CancellationTokenSource?PendingRefresh;
    private JoinableTask?RunningRefresh;

    internal ComponentIconGlyphTagger(ITextBuffer Buffer,string FilePath,VisualStudioWorkspace Workspace){
        this.Buffer=Buffer;
        this.FilePath=FilePath;
        this.Workspace=Workspace;
        this.Buffer.Changed+=this.OnBufferChanged;
        this.Workspace.WorkspaceChanged+=this.OnWorkspaceChanged;
        this.ScheduleRefresh();
    }

    public event EventHandler<SnapshotSpanEventArgs>?TagsChanged;

    public IEnumerable<ITagSpan<ComponentIconGlyphTag>>GetTags(NormalizedSnapshotSpanCollection Spans){
        if(Spans.Count==0){
            yield break;
        }

        var Snapshot=Spans[0].Snapshot;
        IReadOnlyList<TrackedGlyph>CurrentGlyphs;
        lock(this.Gate){
            CurrentGlyphs=this.Glyphs;
        }

        foreach(var Glyph in CurrentGlyphs){
            SnapshotSpan Span;
            try {
                Span=Glyph.Span.GetSpan(Snapshot);
            } catch(ArgumentException){
                continue;
            }

            if(Spans.IntersectsWith(Span)){
                yield return new TagSpan<ComponentIconGlyphTag>(Span,Glyph.Tag);
            }
        }
    }

    internal static bool IsWritableSourceFile(string?FilePath){
        if(string.IsNullOrWhiteSpace(FilePath)){
            return false;
        }

        var SourcePath=FilePath!;
        if(!SourcePath.EndsWith(".cs",StringComparison.OrdinalIgnoreCase)
            ||SourcePath.EndsWith(".g.cs",StringComparison.OrdinalIgnoreCase)){
            return false;
        }

        try {
            return File.Exists(SourcePath)
                &&((File.GetAttributes(SourcePath)&FileAttributes.ReadOnly)==0);
        } catch(UnauthorizedAccessException){
            return false;
        } catch(IOException){
            return false;
        }
    }

    private void OnBufferChanged(object?Sender,TextContentChangedEventArgs Args)=>this.ScheduleRefresh();

    private void OnWorkspaceChanged(object?Sender,WorkspaceChangeEventArgs Args){
        // The workspace can report any of several linked DocumentIds for one physical path.  A
        // cheap path check is safer than assuming the first ID stays stable during project reload.
        if(Args.DocumentId is { }Id
            &&this.Workspace.CurrentSolution.GetDocument(Id) is { }Document
            &&string.Equals(Document.FilePath,this.FilePath,StringComparison.OrdinalIgnoreCase)){
            this.ScheduleRefresh();
        }
    }

    private void ScheduleRefresh(){
        CancellationTokenSource Current;
        lock(this.Gate){
            this.PendingRefresh?.Cancel();
            this.PendingRefresh?.Dispose();
            this.PendingRefresh=new CancellationTokenSource();
            Current=this.PendingRefresh;
        }

        this.RunningRefresh=ThreadHelper.JoinableTaskFactory.RunAsync(async ()=>{
            try {
                // Roslyn's workspace notification follows a TextBuffer edit asynchronously.  A
                // tiny debounce prevents a transient mismatched snapshot from blanking glyphs as
                // the user types while still keeping the margin responsive.
                await Task.Delay(TimeSpan.FromMilliseconds(80),Current.Token).ConfigureAwait(false);
                await this.RefreshAsync(Current.Token).ConfigureAwait(false);
            } catch(OperationCanceledException){
                // A newer buffer/workspace generation superseded this one.
            } catch(Exception Exception){
                ActivityLog.LogError(nameof(ComponentIconGlyphTagger),Exception.ToString());
            }
        });
    }

    private async Task RefreshAsync(CancellationToken CancellationToken){
        var Document=this.Workspace.CurrentSolution.GetDocumentIdsWithFilePath(this.FilePath)
                        .Select(this.Workspace.CurrentSolution.GetDocument)
                        .FirstOrDefault(static Candidate=>Candidate is not null);
        if(Document is null||string.IsNullOrWhiteSpace(Document.Project.FilePath)){
            await this.PublishAsync([],CancellationToken).ConfigureAwait(false);
            return;
        }

        var Text=await Document.GetTextAsync(CancellationToken).ConfigureAwait(false);
        var Snapshot=this.Buffer.CurrentSnapshot;
        if(!string.Equals(Text.ToString(),Snapshot.GetText(),StringComparison.Ordinal)){
            // The next workspace or buffer event will retry against the matching live generation.
            return;
        }

        var Root=await Document.GetSyntaxRootAsync(CancellationToken).ConfigureAwait(false);
        var SemanticModel=await Document.GetSemanticModelAsync(CancellationToken).ConfigureAwait(false);
        var Compilation=await Document.Project.GetCompilationAsync(CancellationToken).ConfigureAwait(false);
        if(Root is null||SemanticModel is null||Compilation is null){
            await this.PublishAsync([],CancellationToken).ConfigureAwait(false);
            return;
        }

        var Catalog=SceneComponentCatalog.Build(Compilation);
        var ProjectDirectory=Path.GetDirectoryName(Document.Project.FilePath)!;
        List<TrackedGlyph>Glyphs=[];
        foreach(var Declaration in Root.DescendantNodes().OfType<StructDeclarationSyntax>()){
            CancellationToken.ThrowIfCancellationRequested();
            if(SemanticModel.GetDeclaredSymbol(Declaration,CancellationToken)is not INamedTypeSymbol Type
                ||!Catalog.Any(Entry=>SymbolEqualityComparer.Default.Equals(Entry.Symbol,Type))
                ||!IsAuthoritativeDeclaration(Type,Declaration,CancellationToken)){
                continue;
            }

            if(Declaration.Identifier.Span.End>Snapshot.Length){
                continue;
            }

            var Icon=SceneComponentIconResolver.Resolve(Type,ProjectDirectory);
            ComponentIconGlyphTarget Target=new(
                this.Buffer,Document.Id,Document.Project.Id,this.FilePath,ProjectDirectory,
                Declaration.SpanStart,SceneTypePredicates.ToFullyQualifiedName(Type),Icon);
            var Span=Snapshot.CreateTrackingSpan(
                                                 Declaration.Identifier.SpanStart,Math.Max(1,Declaration.Identifier.Span.Length),
                                                 SpanTrackingMode.EdgeExclusive);
            Glyphs.Add(new TrackedGlyph(Span,new ComponentIconGlyphTag(Target)));
        }

        await this.PublishAsync(Glyphs,CancellationToken).ConfigureAwait(false);
    }

    private static bool IsAuthoritativeDeclaration(
        INamedTypeSymbol Type,StructDeclarationSyntax Declaration,CancellationToken CancellationToken){
        var IconAttribute=Type.GetAttributes().FirstOrDefault(Attribute=>
                                                                 string.Equals(Attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                                                                               SceneComponentIconMetadata.AttributeFullyQualifiedName,StringComparison.Ordinal));
        if(IconAttribute?.ApplicationSyntaxReference is null){
            // No icon yet: the clicked declaration becomes its owner.  This is intentional for a
            // partial component because writing another document would violate the one-buffer Undo
            // and no-focus contract.
            return true;
        }

        var AttributeSyntax=IconAttribute.ApplicationSyntaxReference.GetSyntax(CancellationToken);
        var Owner=AttributeSyntax.FirstAncestorOrSelf<StructDeclarationSyntax>();
        return Owner is not null
            &&string.Equals(Owner.SyntaxTree.FilePath,Declaration.SyntaxTree.FilePath,StringComparison.OrdinalIgnoreCase)
            &&Owner.SpanStart==Declaration.SpanStart;
    }

    private async Task PublishAsync(IReadOnlyList<TrackedGlyph>Glyphs,CancellationToken CancellationToken){
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(CancellationToken);
        lock(this.Gate){
            this.Glyphs=Glyphs;
        }

        var Snapshot=this.Buffer.CurrentSnapshot;
        this.TagsChanged?.Invoke(this,new SnapshotSpanEventArgs(new SnapshotSpan(Snapshot,0,Snapshot.Length)));
    }

    private sealed class TrackedGlyph {
        internal TrackedGlyph(ITrackingSpan Span,ComponentIconGlyphTag Tag){
            this.Span=Span;
            this.Tag=Tag;
        }

        internal ITrackingSpan Span { get; }

        internal ComponentIconGlyphTag Tag { get; }
    }
}

/// <summary>Writes the selected icon metadata into the already-open glyph document in one Undo unit.</summary>
internal sealed class ComponentIconEditorService {
    private readonly VisualStudioWorkspace Workspace;
    private readonly SceneTextBufferWriter TextBufferWriter;

    private ComponentIconEditorService(VisualStudioWorkspace Workspace,ITextUndoHistoryRegistry UndoHistoryRegistry){
        this.Workspace=Workspace;
        this.TextBufferWriter=new SceneTextBufferWriter(UndoHistoryRegistry);
    }

    internal static ComponentIconEditorService?TryCreate(IComponentModel ComponentModel){
        var Workspace=ComponentModel.GetService<VisualStudioWorkspace>();
        var UndoHistory=ComponentModel.GetService<ITextUndoHistoryRegistry>();
        return Workspace is null||UndoHistory is null?null:new ComponentIconEditorService(Workspace,UndoHistory);
    }

    internal async Task<ComponentIconWriteResult>ApplyAsync(
        ComponentIconGlyphTarget Target,string?SelectedProjectRelativePath,CancellationToken CancellationToken){
        string?NormalizedPath=null;
        if(SelectedProjectRelativePath is not null){
            if(!ProjectIconPath.TryResolve(
                Target.ProjectDirectory,SelectedProjectRelativePath,out var Normalized,out _)){
                return ComponentIconWriteResult.Failure(VsixText.Get("ComponentIcon_SelectedAssetInvalid"));
            }
            NormalizedPath=Normalized;
        }

        var Document=this.Workspace.CurrentSolution.GetDocument(Target.DocumentId);
        if(Document is null
            ||!Document.Project.Id.Equals(Target.ProjectId)
            ||!string.Equals(Document.FilePath,Target.FilePath,StringComparison.OrdinalIgnoreCase)){
            return ComponentIconWriteResult.Failure(VsixText.Get("ComponentIcon_SourceMissingFromWorkspace"));
        }

        var Before=await Document.GetTextAsync(CancellationToken).ConfigureAwait(false);
        var Root=await Document.GetSyntaxRootAsync(CancellationToken).ConfigureAwait(false);
        if(Root is null){
            return ComponentIconWriteResult.Failure(VsixText.Get("ComponentIcon_SourceParseFailed"));
        }

        var Edited=ComponentIconSourceEditor.Apply(
                                                   Root,Target.DeclarationSpanStart,NormalizedPath);
        if(!Edited.Succeeded){
            return ComponentIconWriteResult.Failure(Edited.ErrorMessage??VsixText.Get("ComponentIcon_EditFailed"));
        }

        var After=SourceText.From(Edited.Root!.ToFullString(),Before.Encoding);
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(CancellationToken);
        return this.TextBufferWriter.TryApply(Target.Buffer,Before,After,VsixText.Get("ComponentIcon_UndoName"))
            ?ComponentIconWriteResult.Success()
            :ComponentIconWriteResult.Failure(VsixText.Get("ComponentIcon_SourceChanged"));
    }
}

internal readonly struct ComponentIconWriteResult {
    private ComponentIconWriteResult(bool Succeeded,string?ErrorMessage){
        this.Succeeded=Succeeded;
        this.ErrorMessage=ErrorMessage;
    }

    internal bool Succeeded { get; }

    internal string? ErrorMessage { get; }

    internal static ComponentIconWriteResult Success()=>new(true,null);

    internal static ComponentIconWriteResult Failure(string ErrorMessage)=>new(false,ErrorMessage);
}

/// <summary>Provides clickable visuals in Visual Studio's native glyph margin.</summary>
[Export(typeof(IGlyphFactoryProvider))]
[Name("CSharpGameEngine.ComponentIconGlyphFactory")]
[Order(After="VsTextMarker")]
[ContentType("CSharp")]
[ContentType("code")]
[TagType(typeof(ComponentIconGlyphTag))]
internal sealed class ComponentIconGlyphFactoryProvider : IGlyphFactoryProvider {
    [Import]
    internal IComponentModel ComponentModel { get;set;}=null!;

    public IGlyphFactory GetGlyphFactory(IWpfTextView TextView,IWpfTextViewMargin Margin)=>
        new ComponentIconGlyphFactory(ComponentIconEditorService.TryCreate(this.ComponentModel));
}

internal sealed class ComponentIconGlyphFactory : IGlyphFactory {
    private readonly ComponentIconEditorService?EditorService;
    private JoinableTask?RunningPicker;

    internal ComponentIconGlyphFactory(ComponentIconEditorService?EditorService)=>this.EditorService=EditorService;

    public UIElement GenerateGlyph(Microsoft.VisualStudio.Text.Formatting.IWpfTextViewLine Line,IGlyphTag Tag){
        if(Tag is not ComponentIconGlyphTag ComponentTag){
            return new Border();
        }

        var Button=new Button {
                                  Width=16d,
                                  Height=16d,
                                  Padding=new Thickness(0d),
                                  BorderThickness=new Thickness(0d),
                                  Background=System.Windows.Media.Brushes.Transparent,
                                  Focusable=false,
                                  IsTabStop=false,
                                  ToolTip=ComponentTag.Target.Icon.ToolTip,
                                  Content=new SceneVisualIconPresenter {
                                                                           Icon=ComponentTag.Target.Icon,
                                                                           IconSize=16d,
                                                                       },
                              };
        Button.Click+=(Sender,Args)=>this.ShowPicker(Button,ComponentTag.Target);
        return Button;
    }

    private void ShowPicker(FrameworkElement Anchor,ComponentIconGlyphTarget Target){
        if(this.EditorService is null){
            return;
        }

        this.RunningPicker=ThreadHelper.JoinableTaskFactory.RunAsync(async ()=>{
            var Assets=await Task.Run(
                                     ()=>ProjectIconAssetCatalog.Enumerate(Target.ProjectDirectory)).ConfigureAwait(false);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if(!Anchor.IsVisible){
                return;
            }

            ComponentIconPickerPopup.Show(Anchor,Target,Assets,this.EditorService);
        });
    }
}

/// <summary>Anchored picker menu used only by a glyph click; it never opens or activates another document.</summary>
internal sealed class ComponentIconPickerPopup {
    private static readonly ComponentIconPickerPopup Shared=new();
    private JoinableTask?RunningWrite;

    internal static void Show(
        FrameworkElement Anchor,ComponentIconGlyphTarget Target,IReadOnlyList<ProjectIconAsset>Assets,
        ComponentIconEditorService EditorService)=>Shared.ShowCore(Anchor,Target,Assets,EditorService);

    private void ShowCore(
        FrameworkElement Anchor,ComponentIconGlyphTarget Target,IReadOnlyList<ProjectIconAsset>Assets,
        ComponentIconEditorService EditorService){
        var Menu=new ContextMenu {
                                     PlacementTarget=Anchor,
                                     Placement=PlacementMode.Bottom,
                                     StaysOpen=false,
                                 };
        Menu.Items.Add(this.CreateItem(VsixText.Get("ComponentIcon_DefaultIcon"),SceneVisualIcon.ComponentDefault(),Target,ProjectRelativePath:null,Menu,EditorService));
        foreach(var Asset in Assets){
            var Icon=SceneVisualIcon.ProjectAsset(Asset.ProjectRelativePath,Asset.AbsolutePath);
            Menu.Items.Add(this.CreateItem(Asset.DisplayName,Icon,Target,Asset.ProjectRelativePath,Menu,EditorService));
        }

        Menu.IsOpen=true;
    }

    private MenuItem CreateItem(
        string DisplayName,SceneVisualIcon Icon,ComponentIconGlyphTarget Target,string?ProjectRelativePath,
        ContextMenu Menu,ComponentIconEditorService EditorService){
        var Content=new StackPanel { Orientation=Orientation.Horizontal };
        Content.Children.Add(new SceneVisualIconPresenter { Icon=Icon,IconSize=16d,Margin=new Thickness(0d,0d,6d,0d) });
        Content.Children.Add(new TextBlock { Text=DisplayName,VerticalAlignment=VerticalAlignment.Center });
        var Item=new MenuItem { Header=Content,ToolTip=Icon.ToolTip };
        Item.Click+=(Sender,Args)=>{
            Menu.IsOpen=false;
            this.RunningWrite=ThreadHelper.JoinableTaskFactory.RunAsync(async ()=>{
                var Result=await EditorService.ApplyAsync(
                                                          Target,ProjectRelativePath,CancellationToken.None).ConfigureAwait(false);
                if(!Result.Succeeded){
                    ActivityLog.LogError(nameof(ComponentIconPickerPopup),Result.ErrorMessage??VsixText.Get("ComponentIcon_UpdateFailed"));
                }
            });
        };
        return Item;
    }
}
