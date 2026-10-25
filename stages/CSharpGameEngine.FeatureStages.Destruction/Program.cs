using CSharpGameEngine.FeatureStages;
using CSharpGameEngine.FeatureStages.Destruction;
return StagePackHost.Run("Destruction",args,[StagePackHost.Scene<DestructionReferenceScene>("destruction.prefracture"),StagePackHost.Scene<DestructionReferenceScene>("destruction.runtime-topology"),StagePackHost.Scene<DestructionReferenceScene>("destruction.voxel-sdf")]);
