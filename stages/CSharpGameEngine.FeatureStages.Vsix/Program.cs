using CSharpGameEngine.FeatureStages;
using CSharpGameEngine.FeatureStages.Vsix;
return StagePackHost.Run("VSIX",args,[StagePackHost.Scene<VsixReferenceScene>("vsix.scene-editor-visual"),StagePackHost.Scene<VsixReferenceScene>("vsix.rendering-preview"),StagePackHost.Scene<VsixReferenceScene>("vsix.asset-authoring"),StagePackHost.Scene<VsixReferenceScene>("vsix.transform-tools"),StagePackHost.Scene<VsixReferenceScene>("vsix.project-template")]);
