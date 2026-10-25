using System.Collections.Immutable;
using System.Reflection;
using CSharpGameEngine.Cooking;
using CSharpGameEngine.Interchange;
namespace CSharpGameEngine.Cook.Cli;
internal sealed class ImporterHost:IDisposable {
    private readonly PluginModuleLoadContext Context;
    private ImporterHost(PluginModuleLoadContext Context,ImmutableArray<IAssetImporter>Importers,ImmutableArray<IModelProcessor>Processors){
        this.Context=Context;this.Importers=Importers;this.Processors=Processors;
    }
    internal ImmutableArray<IAssetImporter>Importers{get;}
    internal ImmutableArray<IModelProcessor>Processors{get;}
    internal static ImporterHost Load(string ModulePath){
        var Full=Path.GetFullPath(ModulePath);
        if(!File.Exists(Full)){ throw new FileNotFoundException("Import module not found.",Full); }
        var Context=new PluginModuleLoadContext(Full);
        try {
            var Assembly=Context.LoadFromAssemblyPath(Full);
            var Importers=ImmutableArray.CreateBuilder<IAssetImporter>();
            var Processors=ImmutableArray.CreateBuilder<IModelProcessor>();
            foreach(var Type in Assembly.GetExportedTypes().OrderBy(Type=>Type.FullName,StringComparer.Ordinal)){
                if(Type.IsAbstract||Type.IsInterface||Type.ContainsGenericParameters){ continue; }
                if(!typeof(IAssetImporter).IsAssignableFrom(Type)&&!typeof(IModelProcessor).IsAssignableFrom(Type)){ continue; }
                if(Type.GetConstructor(Type.EmptyTypes) is null){ throw new InvalidDataException($"IMP002: {Type.FullName} requires a public parameterless constructor."); }
                var Instance=Activator.CreateInstance(Type)!;
                if(Instance is IAssetImporter Importer){ Validate(Importer.Descriptor);Importers.Add(Importer); }
                if(Instance is IModelProcessor Processor){ Processors.Add(Processor); }
            }
            if(Importers.Select(Importer=>Importer.Descriptor.Identity).Distinct(StringComparer.Ordinal).Count()!=Importers.Count||Processors.Select(Processor=>Processor.Descriptor.Identity).Distinct(StringComparer.Ordinal).Count()!=Processors.Count){
                throw new InvalidDataException("IMP002: duplicate importer/processor identity in module.");
            }
            return new(Context,Importers.ToImmutable(),Processors.ToImmutable());
        } catch { Context.Unload();throw; }
    }
    internal IAssetImporter Select(string Identity,string Extension)=>this.Importers.SingleOrDefault(Importer=>Importer.Descriptor.Identity==Identity&&Importer.Descriptor.Extensions.Contains(Extension,StringComparer.OrdinalIgnoreCase))
        ??throw new InvalidDataException($"IMP002: module does not register '{Identity}' for '{Extension}'.");
    internal async ValueTask<InterchangeDocument?>ImportAsync(string Identity,ImportRequest Request,IImportContext Context,CancellationToken CancellationToken){
        var Importer=this.Select(Identity,Path.GetExtension(Request.SourcePath));
        var Allowed=Importer.Descriptor.Options.Select(Option=>Option.Name).Concat(this.Processors.SelectMany(Processor=>Processor.Descriptor.Options.Select(Option=>Option.Name))).ToHashSet(StringComparer.Ordinal);
        foreach(var Key in Request.Options.Keys){ if(!Allowed.Contains(Key)){ throw new InvalidDataException($"IMP004: unknown option '{Key}'."); } }
        CancellationToken.ThrowIfCancellationRequested();
        var Result=await Importer.ImportAsync(Request,Context,CancellationToken);
        if(Result.Document is not{ }Document){ return null; }
        Document=await this.ProcessAsync(Document,ModelProcessorStage.SourceSpace,Context,CancellationToken);
        var Normalized=ModelImportPipeline.Normalize(Document);
        foreach(var Diagnostic in Normalized.Diagnostics){ Context.Report(Diagnostic); }
        if(Normalized.Document is null){ return null; }
        Document=await this.ProcessAsync(Normalized.Document,ModelProcessorStage.AfterNormalize,Context,CancellationToken);
        Document=await this.ProcessAsync(Document,ModelProcessorStage.BeforeWrite,Context,CancellationToken);
        var Validation=ModelImportPipeline.ValidateEngine(Document);
        foreach(var Diagnostic in Validation){ Context.Report(Diagnostic); }
        return Validation.Any(D=>D.Severity==ImportSeverity.Error)?null:Document;
    }
    private async ValueTask<InterchangeDocument>ProcessAsync(InterchangeDocument Document,ModelProcessorStage Stage,IProcessContext Context,CancellationToken CancellationToken){
        foreach(var Processor in this.Processors.Where(Processor=>Processor.Descriptor.Stage==Stage).OrderBy(Processor=>Processor.Descriptor.Order).ThenBy(Processor=>Processor.Descriptor.Identity,StringComparer.Ordinal)){
            CancellationToken.ThrowIfCancellationRequested();
            if(Processor.AppliesTo(Document,Context)){ Document=await Processor.ProcessAsync(Document,Context,CancellationToken); }
        }
        return Document;
    }
    private static void Validate(ImporterDescriptor Descriptor){
        if(string.IsNullOrWhiteSpace(Descriptor.Identity)||Descriptor.Version<1||Descriptor.Extensions.Length==0||Descriptor.Extensions.Any(Extension=>Extension.Length<2||Extension[0]!='.'||Extension!=Extension.ToLowerInvariant())||Descriptor.Extensions.Distinct(StringComparer.Ordinal).Count()!=Descriptor.Extensions.Length||Descriptor.Options.Select(Option=>Option.Name).Distinct(StringComparer.Ordinal).Count()!=Descriptor.Options.Length){
            throw new InvalidDataException("IMP002: invalid importer descriptor.");
        }
    }
    public void Dispose()=>this.Context.Unload();
}
