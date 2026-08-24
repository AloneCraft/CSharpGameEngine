using CSharpGameEngine.FeatureStages;
string RepositoryRoot;
try {
    RepositoryRoot=FeatureStageRepositoryLocator.Find();
} catch(Exception Ex){
    Console.Error.WriteLine(Ex.Message);
    return 2;
}
var Catalog=FeatureStageCatalog.CreateDefault();
var CatalogErrors=Catalog.Validate();
if(CatalogErrors.Count>0){
    foreach(var Error in CatalogErrors){ Console.Error.WriteLine(Error); }
    return 2;
}
using var CancellationSource=new CancellationTokenSource();
Console.CancelKeyPress+=(Sender,Event)=>{
    Event.Cancel=true;
    CancellationSource.Cancel();
};
var Application=new FeatureRunnerApplication(Catalog,new FeatureStageExecutor(),new FeatureRunResultStore(RepositoryRoot),new GitChangedPathProvider(),Console.Out,Console.Error);
return await Application.RunAsync(args,CancellationSource.Token);
