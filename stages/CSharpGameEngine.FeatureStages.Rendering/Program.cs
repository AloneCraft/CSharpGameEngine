using CSharpGameEngine.FeatureStages;
using CSharpGameEngine.FeatureStages.Rendering;
return StagePackHost.Run("Rendering",args,[StagePackHost.Scene<RenderingReferenceScene>("rendering.warp-triangle"),StagePackHost.Scene<RenderingReferenceScene>("foundation.d3d12-compute")]);
