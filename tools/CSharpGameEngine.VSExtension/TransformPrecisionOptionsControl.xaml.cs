using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace CSharpGameEngine.VSExtension;

/// <summary>WPF surface for source-controlled Transform precision; it stores no VS user setting.</summary>
public partial class TransformPrecisionOptionsControl:UserControl {
    private bool SuppressChanges;
    private bool HasResolvedTarget;
    private string TargetStatusMessage=string.Empty;

    public TransformPrecisionOptionsControl()=>this.InitializeComponent();

    internal event Action? ApplyRequested;

    internal event Action? RefreshRequested;

    internal void Load(TransformPrecisionOptionsSnapshot Snapshot){
        this.SuppressChanges=true;
        try {
            this.HasResolvedTarget=Snapshot.CanApply;
            this.TargetStatusMessage=Snapshot.StatusMessage;
            this.ProjectTextBlock.Text=Snapshot.ProjectDisplayName;
            this.SourceTextBlock.Text=Snapshot.SourceDisplayPath;
            this.TotalBitsTextBox.Text=Snapshot.Configuration is { } Configuration
                ?Configuration.TotalBits.ToString(CultureInfo.InvariantCulture)
                :string.Empty;
            this.FractionalBitsTextBox.Text=Snapshot.Configuration is { } FractionalConfiguration
                ?FractionalConfiguration.FractionalBits.ToString(CultureInfo.InvariantCulture)
                :string.Empty;
        } finally {
            this.SuppressChanges=false;
        }

        this.UpdatePreview();
    }

    internal bool TryGetRequestedConfiguration(out TransformPrecisionConfiguration Configuration,out string?ErrorMessage){
        Configuration=default;
        ErrorMessage=null;
        if(!int.TryParse(this.TotalBitsTextBox.Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out int TotalBits)
            ||!int.TryParse(this.FractionalBitsTextBox.Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out int FractionalBits)){
            ErrorMessage="総ビット数と小数部ビット数は整数で入力してください。";
            return false;
        }

        TransformPrecisionValidation Validation=TransformPrecisionPolicy.Validate(TotalBits,FractionalBits);
        if(!Validation.IsValid){
            ErrorMessage=Validation.ErrorMessage;
            return false;
        }

        Configuration=new TransformPrecisionConfiguration(TotalBits,FractionalBits);
        return true;
    }

    internal void ShowApplyResult(TransformPrecisionApplyResult Result){
        this.StatusTextBlock.Text=Result.Succeeded
            ?Result.WarningMessage??"GameEnginePrecision.cs を更新し、再ビルドを要求しました。"
            :Result.ErrorMessage??"Transform Precision の適用に失敗しました。";
    }

    private void OnPrecisionTextChanged(object Sender,TextChangedEventArgs Args){
        if(!this.SuppressChanges){
            this.UpdatePreview();
        }
    }

    private void RefreshButton_Click(object Sender,RoutedEventArgs Args)=>this.RefreshRequested?.Invoke();

    private void ApplyButton_Click(object Sender,RoutedEventArgs Args){
        if(this.ApplyButton.IsEnabled){
            this.ApplyRequested?.Invoke();
        }
    }

    private void UpdatePreview(){
        if(!this.TryGetRequestedConfiguration(out TransformPrecisionConfiguration Configuration,out string?ErrorMessage)){
            this.FormatTextBlock.Text=string.Empty;
            this.ResolutionTextBlock.Text=string.Empty;
            this.RangeTextBlock.Text=string.Empty;
            this.StatusTextBlock.Text=ErrorMessage??this.TargetStatusMessage;
            this.ApplyButton.IsEnabled=false;
            return;
        }

        TransformPrecisionPreview Preview=TransformPrecisionPolicy.CreatePreview(
            Configuration.TotalBits,Configuration.FractionalBits);
        this.FormatTextBlock.Text=Preview.Format;
        this.ResolutionTextBlock.Text="分解能: "+Preview.Resolution;
        this.RangeTextBlock.Text="表現範囲: "+Preview.Range;
        this.StatusTextBlock.Text=this.TargetStatusMessage;
        this.ApplyButton.IsEnabled=this.HasResolvedTarget&&Preview.IsValid;
    }
}
