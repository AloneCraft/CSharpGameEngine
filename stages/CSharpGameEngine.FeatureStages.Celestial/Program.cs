using CSharpGameEngine.FeatureStages;
using CSharpGameEngine.FeatureStages.Celestial;
return StagePackHost.Run("Celestial",args,[StagePackHost.Scene<CelestialReferenceScene>("celestial.kepler"),StagePackHost.Scene<CelestialReferenceScene>("celestial.direct-nbody"),StagePackHost.Scene<CelestialReferenceScene>("celestial.barnes-hut"),StagePackHost.Scene<CelestialReferenceScene>("celestial.hierarchical-frames"),StagePackHost.Scene<CelestialReferenceScene>("celestial.frame-relative-runtime")]);
