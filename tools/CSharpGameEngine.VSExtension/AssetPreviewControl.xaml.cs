using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.VSExtension;

/// <summary>WPF content for <see cref="AssetPreviewWindowPane"/> (ADR-0013) - hosts one <see cref="AssetPreviewHwndHost"/> pointed at the opened file.</summary>
public partial class AssetPreviewControl:UserControl,IDisposable {
    private readonly AssetPreviewHwndHost Host;
    private readonly CancellationTokenSource Lifetime=new();
    private string ModelPath;
    private AssetAuthoringResult?MetadataSnapshot;
    private int MetadataOperationGeneration;

    public AssetPreviewControl(string ModelPath){
        this.InitializeComponent();
        this.ModelPath=ModelPath;
        this.Host=new AssetPreviewHwndHost(ModelPath);
        this.Host.OperationChanged+=this.OnOperationChanged;
        this.RootGrid.Children.Add(this.Host);
        this.ApplyLocalizedLabels();
        _=this.RefreshMetadataAsync();
    }

    public void SetModelPath(string ModelPath){
        this.ModelPath=ModelPath;
        this.Host.SetModelPath(ModelPath);
        _=this.RefreshMetadataAsync();
    }

    // WindowPane only disposes Content when it implements IDisposable - without this, the native
    // window inside AssetPreviewHwndHost never runs DestroyWindowCore when the preview tab closes,
    // so the focus-release fix there (see RenderSurfaceWindow.Dispose) never fires either.
    public void Dispose(){
        this.MetadataOperationGeneration++;
        this.Lifetime.Cancel();
        this.Host.OperationChanged-=this.OnOperationChanged;
        this.Host.Dispose();
        this.Lifetime.Dispose();
    }

    private void OnRefreshMetadata(object Sender,RoutedEventArgs Arguments)=>
        _=this.RefreshMetadataAsync();

    private void OnCreateMetadata(object Sender,RoutedEventArgs Arguments){
        AssetAuthoringResult?Snapshot=this.MetadataSnapshot;
        if(Snapshot is null||Snapshot.State!=AssetAuthoringState.MissingMetadata){ return; }
        string SourcePath=this.ModelPath;
        _=this.RunMetadataOperationAsync(
            ()=>AssetAuthoringHostClient.SaveMetadata(SourcePath,Snapshot));
    }

    private Task RefreshMetadataAsync(){
        string SourcePath=this.ModelPath;
        return this.RunMetadataOperationAsync(
            ()=>AssetAuthoringHostClient.Inspect(SourcePath));
    }

    private async Task RunMetadataOperationAsync(Func<AssetAuthoringResult> Operation){
        int Generation=++this.MetadataOperationGeneration;
        this.CreateAssetMetadataButton.IsEnabled=false;
        this.RefreshAssetMetadataButton.IsEnabled=false;
        try {
            AssetAuthoringResult Result=await Task.Run(Operation,this.Lifetime.Token);
            if(this.Lifetime.IsCancellationRequested
                ||Generation!=this.MetadataOperationGeneration){ return; }
            this.MetadataSnapshot=Result;
            this.ApplyMetadataPresentation(AssetAuthoringPresentation.FromResult(Result));
        } catch(OperationCanceledException)when(this.Lifetime.IsCancellationRequested){
        } catch(Exception Exception){
            if(this.Lifetime.IsCancellationRequested
                ||Generation!=this.MetadataOperationGeneration){ return; }
            this.MetadataSnapshot=null;
            this.ApplyMetadataPresentation(AssetAuthoringPresentation.Failure(Exception));
        } finally {
            if(!this.Lifetime.IsCancellationRequested
                &&Generation==this.MetadataOperationGeneration){
                this.RefreshAssetMetadataButton.IsEnabled=true;
            }
        }
    }

    private void ApplyMetadataPresentation(AssetAuthoringPresentation Presentation){
        this.AssetMetadataStateText.Text=Presentation.State;
        this.AssetSourceUriText.Text=Presentation.SourceUri;
        this.AssetIdText.Text=Presentation.AssetId;
        this.AssetUriText.Text=Presentation.AssetUri;
        this.AssetImporterText.Text=Presentation.Importer;
        this.AssetMetadataHashText.Text=Presentation.MetadataSha256;
        this.AssetMetadataDiagnosticText.Text=Presentation.Diagnostics;
        this.AssetMetadataDiagnosticText.Visibility=string.IsNullOrEmpty(Presentation.Diagnostics)
            ?Visibility.Collapsed:Visibility.Visible;
        this.CreateAssetMetadataButton.IsEnabled=Presentation.CanCreateMetadata;
    }

    private void ApplyLocalizedLabels(){
        this.AssetMetadataStateLabel.Text=VsixText.Get("AssetAuthoring_State");
        this.AssetSourceUriLabel.Text=VsixText.Get("AssetAuthoring_SourceUri");
        this.AssetIdLabel.Text=VsixText.Get("AssetAuthoring_AssetId");
        this.AssetUriLabel.Text=VsixText.Get("AssetAuthoring_AssetUri");
        this.AssetImporterLabel.Text=VsixText.Get("AssetAuthoring_Importer");
        this.AssetMetadataHashLabel.Text=VsixText.Get("AssetAuthoring_MetadataHash");
        this.CreateAssetMetadataButton.Content=VsixText.Get("AssetAuthoring_CreateMetadata");
        this.RefreshAssetMetadataButton.Content=VsixText.Get("AssetAuthoring_Refresh");
    }

    private void OnOperationChanged(AuthoringOperationSnapshot Operation){
        this.AssetOperationProgressBar.Value=Operation.Fraction;
        this.AssetOperationStatusText.Text=Operation.Summary;
        string Diagnostics=string.Join(Environment.NewLine,Operation.Diagnostics.Select(
            static Diagnostic=>$"{Diagnostic.Code}: {Diagnostic.Message}"));
        this.AssetOperationDiagnosticText.Text=Diagnostics;
        this.AssetOperationDiagnosticText.Visibility=string.IsNullOrEmpty(Diagnostics)
            ?Visibility.Collapsed
            :Visibility.Visible;
    }
}
