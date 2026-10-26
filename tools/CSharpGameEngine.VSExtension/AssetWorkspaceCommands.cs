using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using CSharpGameEngine.Contracts;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
namespace CSharpGameEngine.VSExtension;
internal static class AssetWorkspaceCommands {
    internal static async Task ShowCreateMenuAsync(FrameworkElement Placement,AssetWorkspaceController Controller){
        await Controller.Owner.JoinableTaskFactory.SwitchToMainThreadAsync();
        var Compilation=await AssetDocumentWorkspace.CompilationAsync(Controller.Query.ProjectPath,Controller.Owner.DisposalToken);
        var Types=await Task.Run(()=>AssetDataEditing.Types(Compilation),Controller.Owner.DisposalToken);
        await Controller.Owner.JoinableTaskFactory.SwitchToMainThreadAsync();
        var Menu=new ContextMenu();
        Add(Menu,"Assets_NewFolder",()=>CreateAsync(Controller,"Folder"));
        Add(Menu,"Assets_NewScene",()=>CreateAsync(Controller,"Scene"));
        Add(Menu,"Assets_NewGraph",()=>CreateAsync(Controller,"Graph"));
        if(Types.Length>0){ Add(Menu,"Assets_NewData",()=>CreateAsync(Controller,"Data")); }
        Add(Menu,"Assets_Import",()=>ImportAsync(Controller));
        Menu.PlacementTarget=Placement;Menu.IsOpen=true;
    }
    internal static void ShowOrganizeMenu(FrameworkElement Placement,AssetWorkspaceController Controller,IReadOnlyList<AssetWorkspaceItem>Items){
        ThreadHelper.ThrowIfNotOnUIThread();
        var Menu=new ContextMenu();
        if(Items.Count==1){
            var Item=Items[0];
            var Capabilities=Item.GetCapabilities();
            Add(Menu,"Assets_Rename",()=>OrganizeAsync(Controller,Item,AssetFileOperation.Rename),(Capabilities&AssetWorkspaceCapabilities.Organize)!=0);
            Add(Menu,"Assets_Move",()=>OrganizeAsync(Controller,Item,AssetFileOperation.Move),(Capabilities&AssetWorkspaceCapabilities.Organize)!=0);
            Add(Menu,"Assets_Duplicate",()=>OrganizeAsync(Controller,Item,AssetFileOperation.Duplicate),(Capabilities&AssetWorkspaceCapabilities.Duplicate)!=0);
            Add(Menu,"Assets_Delete",()=>OrganizeAsync(Controller,Item,AssetFileOperation.Delete),(Capabilities&AssetWorkspaceCapabilities.Organize)!=0);
            Add(Menu,"Assets_References",()=>ShowReferencesAsync(Item),Item.State!=AssetWorkspaceState.MissingSource);
        }
        if(Items.Count>1){
            Add(Menu,"Assets_Open",async()=>{ await Controller.Owner.JoinableTaskFactory.SwitchToMainThreadAsync();foreach(var Item in Items){ Controller.Open(Item); } });
            Add(Menu,"Assets_References",async()=>{
                var Lines=new List<string>();
                foreach(var Item in Items){ var Plan=await AssetReferencePlanner.PlanAsync(Item,Item.Source.SourcePath,AssetFileOperation.Inspect,Controller.Owner.DisposalToken);Lines.Add(Item.Source.RelativePath+Environment.NewLine+string.Join(Environment.NewLine,Plan.Edits.Select(static Edit=>Edit.Path))); }
                await Controller.Owner.JoinableTaskFactory.SwitchToMainThreadAsync();ShowMessage(string.Join(Environment.NewLine,Lines));
            });
        }
        var MigrationSources=(Items.Count>0?Items:Controller.Items.Where(Item=>Item.Source.ProjectPath==Controller.Query.ProjectPath).ToArray()).Where(static Item=>Item.Kind==AssetWorkspaceKind.Model&&Item.State==AssetWorkspaceState.InvalidMetadata&&!Item.Source.ReadOnly).ToArray();
        Add(Menu,"Assets_Migrate",()=>MigrateAsync(Controller,MigrationSources),MigrationSources.Length>0);
        Add(Menu,"Assets_Recovery",()=>RecoverAsync(Controller));
        Menu.PlacementTarget=Placement;Menu.IsOpen=true;
    }
    private static void Add(ContextMenu Menu,string Key,Func<Task>Action,bool Enabled=true){
        var Item=new MenuItem{Header=VsixText.Get(Key),IsEnabled=Enabled};
        Item.Click+=(_,_)=>AssetsToolWindowPane.Controller?.Run(Action);
        Menu.Items.Add(Item);
    }
    private static async Task CreateAsync(AssetWorkspaceController Controller,string Kind){
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var Root=Controller.Projects.FirstOrDefault(Item=>Item.ProjectFilePath==Controller.Query.ProjectPath)?.ProjectDirectory;
        if(Root is null){ throw new InvalidOperationException(VsixText.Get("Assets_NoSelection")); }
        var Folder=Controller.Query.Folder;
        if(!AssetWorkspaceQuery.IsWithin(Folder,Root)&&!string.Equals(Folder,Root,StringComparison.OrdinalIgnoreCase)){ Folder=Root; }
        var Extension=Kind switch{"Scene"=>".json","Graph"=>".cgeshadergraph","Data"=>".cgeasset",_=>string.Empty};
        if(Kind=="Scene"&&!Folder.Replace('\\','/').Contains("/Scenes")){ Folder=Path.Combine(Root,"Scenes"); }
        if(Kind is "Graph" or "Data"&&!AssetWorkspaceQuery.IsWithin(Folder,Path.Combine(Root,"Assets"))){ Folder=Path.Combine(Root,"Assets"); }
        var PathValue=Prompt(VsixText.Get("Assets_Destination"),Path.Combine(Folder,"New"+Kind+Extension));
        if(PathValue is null){ return; }
        if(!AssetWorkspaceQuery.IsWithin(PathValue,Root)){ throw new InvalidOperationException(VsixText.Get("Assets_ReadOnly")); }
        string?SourceTextValue=null;
        if(Kind=="Scene"){
            if(!Microsoft.CodeAnalysis.CSharp.SyntaxFacts.IsValidIdentifier(Path.GetFileNameWithoutExtension(PathValue))){ throw new InvalidOperationException("Invalid scene class name."); }
            SourceTextValue="{\n  \"schemaVersion\": 3,\n  \"className\": \""+Path.GetFileNameWithoutExtension(PathValue)+"\",\n  \"root\": {\"id\": \""+Guid.NewGuid().ToString("D")+"\", \"name\": \"Root\", \"components\": [], \"children\": []}\n}\n";
        } else if(Kind=="Graph"){
            var Template=Path.Combine(Path.GetDirectoryName(typeof(ShaderGraphTemplateWizard).Assembly.Location)!,"ShaderGraphTemplates","Model.cgeshadergraph");
            var Graph=ShaderGraph.ShaderGraphCanonicalization.FromJson(File.ReadAllText(Template))??throw new InvalidDataException("Invalid graph template.");
            if(Graph.Dependencies.Count>0){ throw new InvalidOperationException("Use Add New Item to create this graph dependency bundle."); }
            SourceTextValue=ShaderGraph.ShaderGraphCanonicalization.ToCanonicalJson(ShaderGraphTemplateWizard.Remap(Graph));
        } else if(Kind=="Data"){
            var Compilation=await AssetDocumentWorkspace.CompilationAsync(Controller.Query.ProjectPath,System.Threading.CancellationToken.None);
            var Types=await Task.Run(()=>AssetDataEditing.Types(Compilation));
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var Type=Choose(VsixText.Get("Assets_NewData"),Types.Select(static Type=>Type.ToDisplayString()).ToArray());
            if(Type is null){ return; }
            var Symbol=Types.First(Value=>Value.ToDisplayString()==Type);
            var Defaults=SceneComponentInitializerPolicy.CreatePlan(Symbol,"global::"+Type,Compilation);
            var Converted=SceneAnalysis.Json.SceneJsonConverter.ConvertDataAsset(Symbol,Defaults.Members);
            SourceTextValue=Converted.Text??throw new InvalidOperationException(string.Join(Environment.NewLine,Converted.Problems));
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            AssetDataEditing.Validate(SourceTextValue,Compilation);
        }
        AssetFileTransaction.Create(Controller.Owner,PathValue,SourceTextValue);
        await Controller.RefreshAsync();
    }
    private static async Task ImportAsync(AssetWorkspaceController Controller){
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var Dialog=new Microsoft.Win32.OpenFileDialog{Multiselect=true,CheckFileExists=true};
        if(Dialog.ShowDialog()!=true){ return; }
        var Root=Controller.Projects.FirstOrDefault(Item=>Item.ProjectFilePath==Controller.Query.ProjectPath)?.ProjectDirectory;
        if(Root is null){ return; }
        var Folder=AssetWorkspaceQuery.IsWithin(Controller.Query.Folder,Root)?Controller.Query.Folder:Path.Combine(Root,"Assets");
        await ImportFilesAsync(Controller,Dialog.FileNames,Folder);
    }
    internal static async Task ImportFilesAsync(AssetWorkspaceController Controller,string[]Sources,string?Destination=null){
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var Root=Controller.Projects.FirstOrDefault(P=>P.ProjectFilePath==Controller.Query.ProjectPath)?.ProjectDirectory??throw new InvalidOperationException("Game project unavailable.");
        var Folder=Destination??(AssetWorkspaceQuery.IsWithin(Controller.Query.Folder,Path.Combine(Root,"Assets"))?Controller.Query.Folder:Path.Combine(Root,"Assets"));
        foreach(var Source in Sources){
            var Target=Path.Combine(Folder,Path.GetFileName(Source));
            if(string.Equals(Path.GetFullPath(Source),Path.GetFullPath(Target),StringComparison.OrdinalIgnoreCase)){ continue; }
            AssetFileTransaction.Copy(Controller.Owner,Source,Target);
            if(ImageImportSettings.Supports(Target)){
                var Result=await Task.Run(()=>AssetAuthoringHostClient.Image(new(){ImageCommand=3,SourcePath=Target}));
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();if(!Result.Success){ throw new InvalidOperationException(Result.Diagnostic); }
            }else if(Path.GetExtension(Target).Equals(".fbx",StringComparison.OrdinalIgnoreCase)){
                var Result=await Task.Run(()=>AssetAuthoringHostClient.Model(new(){ModelCommand=2,SourcePath=Target}));
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();if(!Result.Success){ throw new InvalidOperationException(Result.Diagnostic); }
            }
        }
        await Controller.RefreshAsync();
    }
    private static async Task OrganizeAsync(AssetWorkspaceController Controller,AssetWorkspaceItem Item,AssetFileOperation Operation){
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var ProjectRoot=Controller.Projects.FirstOrDefault(Project=>Project.ProjectFilePath==Item.Source.ProjectPath)?.ProjectDirectory??throw new InvalidOperationException(VsixText.Get("Assets_NoSelection"));
        var Initial=Operation==AssetFileOperation.Duplicate?Path.Combine(ProjectRoot,"Assets",Path.GetFileNameWithoutExtension(Item.Source.SourcePath)+"_Copy"+Path.GetExtension(Item.Source.SourcePath)):Item.Source.SourcePath;
        var Destination=Operation==AssetFileOperation.Delete?string.Empty:Prompt(VsixText.Get("Assets_Destination"),Initial);
        if(Destination is null){ return; }
        AssetWorkspaceItem?Replacement=null;
        if(Operation==AssetFileOperation.Delete){
            var ReplacementPath=Prompt(VsixText.Get("Assets_Replace"),string.Empty);
            if(ReplacementPath is null){ return; }
            if(!string.IsNullOrWhiteSpace(ReplacementPath)){
                Replacement=Controller.Items.FirstOrDefault(Candidate=>string.Equals(Candidate.Source.SourcePath,ReplacementPath,StringComparison.OrdinalIgnoreCase));
                if(Replacement is null){ throw new InvalidOperationException("Replacement is not an asset of this project."); }
            }
        }
        var Plan=await AssetReferencePlanner.PlanAsync(Item,Destination,Operation,System.Threading.CancellationToken.None,Replacement);
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        if(new AssetChangeReviewDialog(Plan).ShowDialog()!=true){ return; }
        AssetFileTransaction.Execute(Controller.Owner,Plan);
        await Controller.RefreshAsync();
    }
    internal static async Task ShowReferencesAsync(AssetWorkspaceItem Item){
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var Plan=await AssetReferencePlanner.PlanAsync(Item,Item.Source.SourcePath,AssetFileOperation.Inspect,System.Threading.CancellationToken.None);
        ShowMessage(Plan.Edits.Count==0?VsixText.Get("Assets_None"):string.Join(Environment.NewLine,Plan.Edits.Select(static Edit=>Edit.Path)));
    }
    private static async Task RecoverAsync(AssetWorkspaceController Controller){
        await Controller.Owner.JoinableTaskFactory.SwitchToMainThreadAsync();
        var Context=Controller.Projects.FirstOrDefault(Project=>Project.ProjectFilePath==Controller.Query.ProjectPath);
        if(Context is null){ return; }
        var Result=await Task.Run(()=>AssetAuthoringHostClient.MutateFiles(new(){FileMutationCommand=4,RequestId=Guid.NewGuid(),Root=Context.ProjectDirectory},Context.WorkspaceRoot));
        await Controller.Owner.JoinableTaskFactory.SwitchToMainThreadAsync();
        var Choice=Choose(VsixText.Get("Assets_Recovery"),Result.RecoveryOperations.Select(static Operation=>Operation.Description).ToArray());
        var Selected=Result.RecoveryOperations.FirstOrDefault(Operation=>Operation.Description==Choice);
        if(Selected is null||!Confirm(Selected.Description)){ return; }
        await Task.Run(()=>AssetAuthoringHostClient.MutateFiles(new(){FileMutationCommand=5,RequestId=Guid.NewGuid(),Root=Context.ProjectDirectory,OperationId=Selected.OperationId},Context.WorkspaceRoot));
        await Controller.RefreshAsync();
    }
    private static async Task MigrateAsync(AssetWorkspaceController Controller,IReadOnlyList<AssetWorkspaceItem>Items){
        await Controller.Owner.JoinableTaskFactory.SwitchToMainThreadAsync();
        if(!Confirm(VsixText.Get("Assets_Migrate")+Environment.NewLine+string.Join(Environment.NewLine,Items.Select(static Item=>Item.Source.RelativePath)))){ return; }
        var Errors=new List<string>();var Converted=0;
        foreach(var Item in Items){
            try {
                var Result=await Task.Run(()=>AssetAuthoringHostClient.MigrateLegacy(Item.Source.SourcePath,Item.MetadataSha256));
                if(Result.Success){ Converted++; }else { Errors.Add(Item.Source.RelativePath+": "+string.Join("; ",Result.Diagnostics.Select(static Diagnostic=>Diagnostic.Message))); }
            }catch(Exception Error){ Errors.Add(Item.Source.RelativePath+": "+Error.Message); }
        }
        await Controller.RefreshAsync();await Controller.Owner.JoinableTaskFactory.SwitchToMainThreadAsync();
        Controller.Report(VsixText.Format("Assets_MigrationResult",Converted,Items.Count)+Environment.NewLine+string.Join(Environment.NewLine,Errors));
    }
    internal static string?Prompt(string Title,string Initial){
        ThreadHelper.ThrowIfNotOnUIThread();
        var Dialog=new DialogWindow{Title=Title,Width=620,Height=160,ResizeMode=ResizeMode.NoResize};
        AssetTheme.Apply(Dialog);
        var Panel=new StackPanel{Margin=new Thickness(12)};var Input=new TextBox{Text=Initial,Margin=new Thickness(0,0,0,12)};
        var Button=new Button{Content=VsixText.Get("Assets_Apply"),IsDefault=true,HorizontalAlignment=HorizontalAlignment.Right,Padding=new Thickness(16,3,16,3)};
        Button.Click+=(_,_)=>Dialog.DialogResult=true;Panel.Children.Add(Input);Panel.Children.Add(Button);Dialog.Content=Panel;
        Dialog.Loaded+=(_,_)=>{ Input.Focus();Input.SelectAll(); };
        return Dialog.ShowDialog()==true?Input.Text:null;
    }
    internal static string?Choose(string Title,string[]Values){
        ThreadHelper.ThrowIfNotOnUIThread();
        if(Values.Length==0){ return null; }
        var Dialog=new DialogWindow{Title=Title,Width=550,Height=160,ResizeMode=ResizeMode.NoResize};
        AssetTheme.Apply(Dialog);
        var Panel=new StackPanel{Margin=new Thickness(12)};var Input=new ComboBox{ItemsSource=Values,SelectedIndex=0,Margin=new Thickness(0,0,0,12)};
        var Apply=new Button{Content=VsixText.Get("Assets_Create"),IsDefault=true,HorizontalAlignment=HorizontalAlignment.Right};
        Apply.Click+=(_,_)=>Dialog.DialogResult=true;Panel.Children.Add(Input);Panel.Children.Add(Apply);Dialog.Content=Panel;
        return Dialog.ShowDialog()==true?Input.SelectedItem as string:null;
    }
    internal static bool Confirm(string Text)=>VsShellUtilities.ShowMessageBox(ServiceProvider.GlobalProvider,Text,VsixText.Get("Assets_PreviewChanges"),OLEMSGICON.OLEMSGICON_QUERY,OLEMSGBUTTON.OLEMSGBUTTON_OKCANCEL,OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_SECOND)==(int)VSConstants.MessageBoxResult.IDOK;
    internal static void ShowMessage(string Text)=>VsShellUtilities.ShowMessageBox(ServiceProvider.GlobalProvider,Text,VsixText.Get("Assets_Title"),OLEMSGICON.OLEMSGICON_INFO,OLEMSGBUTTON.OLEMSGBUTTON_OK,OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
}
