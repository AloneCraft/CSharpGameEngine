using CSharpGameEngine.FeatureStages;
using CSharpGameEngine.FeatureStages.Coupling;
return StagePackHost.Run("Coupling",args,[StagePackHost.Scene<CouplingReferenceScene>("coupling.thermal-field"),StagePackHost.Scene<CouplingReferenceScene>("coupling.multidomain")]);
