using CSharpGameEngine.FeatureStages;
using CSharpGameEngine.FeatureStages.Fluid;
return StagePackHost.Run("Fluid",args,[StagePackHost.Scene<FluidReferenceScene>("fluid.volume-renderer"),StagePackHost.Scene<FluidReferenceScene>("fluid.sph-pbf"),StagePackHost.Scene<FluidReferenceScene>("fluid.flip-apic"),StagePackHost.Scene<FluidReferenceScene>("fluid.eulerian-grid")]);
