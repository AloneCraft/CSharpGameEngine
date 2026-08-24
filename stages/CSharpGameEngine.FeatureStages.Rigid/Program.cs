using CSharpGameEngine.FeatureStages;
using CSharpGameEngine.FeatureStages.Rigid;
return StagePackHost.Run("Rigid",args,[StagePackHost.Scene<RigidReferenceScene>("rigid.joints"),StagePackHost.Scene<RigidReferenceScene>("rigid.ragdoll"),StagePackHost.Scene<RigidReferenceScene>("rigid.character-controller"),StagePackHost.Scene<RigidReferenceScene>("rigid.vehicle"),StagePackHost.Scene<RigidReferenceScene>("rigid.dynamic-ccd"),StagePackHost.Scene<RigidReferenceScene>("rigid.gpu-solver")]);
