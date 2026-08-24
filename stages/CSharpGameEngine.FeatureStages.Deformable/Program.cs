using CSharpGameEngine.FeatureStages;
using CSharpGameEngine.FeatureStages.Deformable;
return StagePackHost.Run("Deformable",args,[StagePackHost.Scene<DeformableReferenceScene>("deformable.pose-bridge"),StagePackHost.Scene<DeformableReferenceScene>("deformable.xpbd"),StagePackHost.Scene<DeformableReferenceScene>("deformable.cloth"),StagePackHost.Scene<DeformableReferenceScene>("deformable.rope"),StagePackHost.Scene<DeformableReferenceScene>("deformable.soft-body"),StagePackHost.Scene<DeformableReferenceScene>("deformable.mpm")]);
