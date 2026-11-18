using CSharpGameEngine.FeatureStages;
using CSharpGameEngine.FeatureStages.Celestial;
using CSharpGameEngine.FeatureStages.Coupling;
using CSharpGameEngine.FeatureStages.Deformable;
using CSharpGameEngine.FeatureStages.Destruction;
using CSharpGameEngine.FeatureStages.Fluid;
using CSharpGameEngine.FeatureStages.Foundation;
using CSharpGameEngine.FeatureStages.ProbabilityQuantum;
using CSharpGameEngine.FeatureStages.Rendering;
using CSharpGameEngine.FeatureStages.Rigid;
using CSharpGameEngine.FeatureStages.Vsix;
// 全 Pack の場面を 1 つの実行ファイルに登録する。場面の ID は Pack をまたいで重ならない（ADR-0099）。
return StagePackHost.Run(args,[
    StagePackHost.Scene<CelestialReferenceScene>("Celestial","celestial.kepler"),
    StagePackHost.Scene<CelestialReferenceScene>("Celestial","celestial.direct-nbody"),
    StagePackHost.Scene<CelestialReferenceScene>("Celestial","celestial.barnes-hut"),
    StagePackHost.Scene<CelestialReferenceScene>("Celestial","celestial.hierarchical-frames"),
    StagePackHost.Scene<CelestialReferenceScene>("Celestial","celestial.frame-relative-runtime"),
    StagePackHost.Scene<CouplingReferenceScene>("Coupling","coupling.thermal-field"),
    StagePackHost.Scene<CouplingReferenceScene>("Coupling","coupling.multidomain"),
    StagePackHost.Scene<DeformableReferenceScene>("Deformable","deformable.pose-bridge"),
    StagePackHost.Scene<DeformableReferenceScene>("Deformable","deformable.xpbd"),
    StagePackHost.Scene<DeformableReferenceScene>("Deformable","deformable.cloth"),
    StagePackHost.Scene<DeformableReferenceScene>("Deformable","deformable.rope"),
    StagePackHost.Scene<DeformableReferenceScene>("Deformable","deformable.soft-body"),
    StagePackHost.Scene<DeformableReferenceScene>("Deformable","deformable.mpm"),
    StagePackHost.Scene<DestructionReferenceScene>("Destruction","destruction.prefracture"),
    StagePackHost.Scene<DestructionReferenceScene>("Destruction","destruction.runtime-topology"),
    StagePackHost.Scene<DestructionReferenceScene>("Destruction","destruction.voxel-sdf"),
    StagePackHost.Scene<FluidReferenceScene>("Fluid","fluid.volume-renderer"),
    StagePackHost.Scene<FluidReferenceScene>("Fluid","fluid.sph-pbf"),
    StagePackHost.Scene<FluidReferenceScene>("Fluid","fluid.flip-apic"),
    StagePackHost.Scene<FluidReferenceScene>("Fluid","fluid.eulerian-grid"),
    StagePackHost.Scene<FoundationReferenceScene>("Foundation","foundation.reference"),
    StagePackHost.Scene<ProbabilityQuantumReferenceScene>("ProbabilityQuantum","probability.gameplay"),
    StagePackHost.Scene<ProbabilityQuantumReferenceScene>("ProbabilityQuantum","probability.wavefunction-grid"),
    StagePackHost.Scene<RenderingReferenceScene>("Rendering","rendering.warp-triangle"),
    StagePackHost.Scene<RenderingReferenceScene>("Rendering","rendering.baseline"),
    StagePackHost.Scene<RenderingReferenceScene>("Rendering","showcase.visible-progress"),
    StagePackHost.Scene<RenderingReferenceScene>("Rendering","foundation.d3d12-compute"),
    StagePackHost.Scene<RenderingReferenceScene>("Rendering","rendering.materials"),
    StagePackHost.Scene<RenderingReferenceScene>("Rendering","rendering.visibility"),
    StagePackHost.Scene<RenderingReferenceScene>("Rendering","rendering.quality-profiles"),
    StagePackHost.Scene<RenderingReferenceScene>("Rendering","rendering.virtual-shadows"),
    StagePackHost.Scene<RenderingReferenceScene>("Rendering","rendering.gpu-scene"),
    StagePackHost.Scene<RenderingReferenceScene>("Rendering","rendering.virtual-geometry"),
    StagePackHost.Scene<RenderingReferenceScene>("Rendering","rendering.virtual-geometry-streaming"),
    StagePackHost.Scene<RenderingReferenceScene>("Rendering","rendering.environment-lighting"),
    StagePackHost.Scene<RenderingReferenceScene>("Rendering","rendering.skeletal-animation"),
    StagePackHost.Scene<RenderingReferenceScene>("Rendering","rendering.radial-depth"),
    StagePackHost.Scene<RenderingReferenceScene>("Rendering","rendering.device-recovery"),
    StagePackHost.Scene<RenderingReferenceScene>("Rendering","rendering.performance"),
    StagePackHost.Scene<RigidReferenceScene>("Rigid","rigid.joints"),
    StagePackHost.Scene<RigidReferenceScene>("Rigid","rigid.ragdoll"),
    StagePackHost.Scene<RigidReferenceScene>("Rigid","rigid.character-controller"),
    StagePackHost.Scene<RigidReferenceScene>("Rigid","rigid.vehicle"),
    StagePackHost.Scene<RigidReferenceScene>("Rigid","rigid.dynamic-ccd"),
    StagePackHost.Scene<RigidReferenceScene>("Rigid","rigid.gpu-solver"),
    StagePackHost.Scene<VsixReferenceScene>("VSIX","vsix.scene-editor-visual"),
    StagePackHost.Scene<VsixReferenceScene>("VSIX","vsix.rendering-preview"),
    StagePackHost.Scene<VsixReferenceScene>("VSIX","vsix.asset-authoring"),
    StagePackHost.Scene<VsixReferenceScene>("VSIX","vsix.transform-tools"),
    StagePackHost.Scene<VsixReferenceScene>("VSIX","vsix.project-template"),
]);
