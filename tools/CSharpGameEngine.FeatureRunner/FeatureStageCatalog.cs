namespace CSharpGameEngine.FeatureStages;
public sealed class FeatureStageCatalog {
    public const string ExtendedEcsMaskFeatureId="F-L3-020";
    public static IReadOnlyList<string>ExistingFeatureIds{get;}=[
        "F-L7-001","F-L7-002","F-L7-012","F-L7-013","F-L7-015","F-L7-016","F-L7-017",
        "F-L1-001","F-L1-002","F-L1-003","F-L1-005","F-L1-006","F-L2-003","F-L0-006",
        "F-L0-003","F-L0-005","F-L2-002","F-L7-005",
        "F-L0-001","F-L0-002","F-L0-004","F-L0-007","F-L4-002",
        "F-L3-011","F-L3-012","F-L3-013",
        "F-L1-007","F-L4-001",
        "F-L8-001","F-L8-002","F-L8-005","F-L8-008",
        "F-L8-010","F-L8-011","F-L8-012","F-L8-015","F-L8-016","F-L8-017",
        "F-L3-015","F-L3-016","F-L3-017",
        "F-L5-001","F-L5-002","F-L5-003","F-L3-018","F-L6-002","F-L7-020","F-L8-014",
    ];
    public static IReadOnlyList<string>DeferredSimulationFeatureIds{get;}=[
        "F-L5-004","F-L5-005","F-L5-006","F-L5-007","F-L5-008","F-L5-009","F-L5-010","F-L5-011",
        "F-L5-012","F-L5-013","F-L5-014","F-L5-015","F-L5-016","F-L5-017","F-L5-018","F-L5-019",
        "F-L5-020","F-L5-021","F-L5-022","F-L5-023","F-L5-024","F-L5-025","F-L5-026","F-L5-027",
        "F-L5-028","F-L5-029","F-L5-030","F-L5-031",
    ];
    public IReadOnlyList<FeatureDefinition>Features{get;}
    public IReadOnlyList<FeatureStageDefinition>Stages{get;}
    private FeatureStageCatalog(IReadOnlyList<FeatureDefinition>Features,IReadOnlyList<FeatureStageDefinition>Stages){
        this.Features=Features;
        this.Stages=Stages;
    }
    public static FeatureStageCatalog CreateDefault(){
        var Features=new List<FeatureDefinition>();
        AddExistingFeatures(Features);
        AddInfrastructureFeatures(Features);
        AddDeferredSimulationFeatures(Features);
        var Stages=new List<FeatureStageDefinition>();
        AddExistingStages(Stages);
        AddInfrastructureStages(Stages);
        AddDeferredSimulationStages(Stages);
        return new FeatureStageCatalog(Features,Stages);
    }
    public FeatureDefinition GetFeature(string FeatureId)=>this.Features.FirstOrDefault(Feature=>Feature.Id==FeatureId)??throw new KeyNotFoundException($"Unknown feature ID: {FeatureId}");
    public FeatureStageDefinition GetStage(string StageId)=>this.Stages.FirstOrDefault(Stage=>Stage.Id==StageId)??throw new KeyNotFoundException($"Unknown stage ID: {StageId}");
    public IReadOnlyList<string>Validate(){
        var Errors=new List<string>();
        foreach(var Group in this.Features.GroupBy(static Feature=>Feature.Id,StringComparer.Ordinal).Where(static Group=>Group.Count()>1)){
            Errors.Add($"Duplicate feature ID: {Group.Key}");
        }
        foreach(var Group in this.Stages.GroupBy(static Stage=>Stage.Id,StringComparer.Ordinal).Where(static Group=>Group.Count()>1)){
            Errors.Add($"Duplicate stage ID: {Group.Key}");
        }
        var FeatureIds=new HashSet<string>(this.Features.Select(static Feature=>Feature.Id));
        foreach(var Stage in this.Stages){
            if(Stage.FeatureIds.Count==0){ Errors.Add($"Stage has no feature: {Stage.Id}"); }
            foreach(var FeatureId in Stage.FeatureIds.Where(FeatureId=>!FeatureIds.Contains(FeatureId))){ Errors.Add($"Stage {Stage.Id} references unknown feature {FeatureId}"); }
            if(Stage.Availability==FeatureStageAvailability.Executable&&Stage.Commands.Count==0){ Errors.Add($"Executable stage has no command: {Stage.Id}"); }
            if(Stage.Availability==FeatureStageAvailability.NotImplemented&&Stage.Commands.Count!=0){ Errors.Add($"NotImplemented stage has commands: {Stage.Id}"); }
            if(Stage.Mode==FeatureStageMode.Visual&&Stage.AdapterRequirement!=FeatureAdapterRequirement.WarpRequiredHardwareOptional){ Errors.Add($"Visual stage must require WARP: {Stage.Id}"); }
            if(Stage.Mode==FeatureStageMode.Visual&&(string.IsNullOrWhiteSpace(Stage.SceneProject)||string.IsNullOrWhiteSpace(Stage.SceneId))){ Errors.Add($"Visual stage must bind a C# scene: {Stage.Id}"); }
            if(Stage.Seed==0){ Errors.Add($"Stage seed must be nonzero: {Stage.Id}"); }
            if(Stage.Ticks<=0){ Errors.Add($"Stage ticks must be positive: {Stage.Id}"); }
            if(Stage.TimeoutSeconds<=0){ Errors.Add($"Stage timeout must be positive: {Stage.Id}"); }
            if(string.IsNullOrWhiteSpace(Stage.Oracle)){ Errors.Add($"Stage oracle is required: {Stage.Id}"); }
        }
        foreach(var Feature in this.Features.Where(Feature=>!this.Stages.Any(Stage=>Stage.FeatureIds.Contains(Feature.Id,StringComparer.Ordinal)))){
            Errors.Add($"Feature has no stage: {Feature.Id}");
        }
        return Errors;
    }
    private static void AddExistingFeatures(List<FeatureDefinition>Features){
        Add(Features,"F-L7-001","CodeAnalysis project foundation","L7",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L7-002","Bootstrap source generator","L7",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L7-012","Scene DSL analysis and runtime generation","L7",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L7-013","Scene DSL diagnostics","L7",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L7-015","Game system source generator","L7",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L7-016","Game system diagnostics","L7",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L7-017","Startup scene system generation","L7",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L1-001","RUF semantic coordinates","L1",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L1-002","RUF quaternion and transform conventions","L1",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L1-003","Engine coordinate convention presets","L1",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L1-005","glTF coordinate convention","L1",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L1-006","FBX coordinate convention","L1",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L2-003","Camera and light basis schema","L2",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L0-006","Shared camera view matrix","L0",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L0-003","Procedural mesh factory","L0",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L0-005","glTF scene importer","L0",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L2-002","Geometry primitives","L2",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L7-005","Unreal level import pipeline","L7",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L0-001","Windows camera runtime","L0",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L0-002","D3D12 stage renderer","L0",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L0-004","Win32 input backend","L0",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L0-007","Directional shadows HDR ACES and bloom","L0",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L4-002","Windows render server","L4",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L3-011","Scene DSL runtime contracts","L3",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L3-012","Closed-set archetype runtime","L3",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L3-013","Startup scene contract","L3",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L1-007","Audio backend abstraction","L1",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L4-001","XAudio2 backend","L4",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L8-001","Open game command","L8",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L8-002","VSIX glTF bridge","L8",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L8-005","Game Engine layout","L8",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L8-008","Resident bridge process","L8",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L8-010","Scene Designer model rendering","L8",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L8-011","Scene editor interaction","L8",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L8-012","Scene inspector editing","L8",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L8-015","VSIX localization","L8",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L8-016","Scene View physics sidebar","L8",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L8-017","Scene Designer Escape routing","L8",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L3-015","Entity loose octree","L3",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L3-016","World spatial snapshot","L3",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L3-017","Spatial debug projection","L3",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L5-001","Shared Windows ECS runtime","L5",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L5-002","Deterministic simulation runtime","L5",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L5-003","Deterministic rigid-body runtime","L5",FeatureLifecycleStatus.PartiallyImplemented);
        Add(Features,"F-L3-018","System-major fixed simulation phases","L3",FeatureLifecycleStatus.PartiallyImplemented);
        Add(Features,"F-L6-002","Physics cooking and cache artifacts","L6",FeatureLifecycleStatus.PartiallyImplemented);
        Add(Features,"F-L7-020","Physics CLI and bake host","L7",FeatureLifecycleStatus.PartiallyImplemented);
        Add(Features,"F-L8-014","Scene View physics controls and overlays","L8",FeatureLifecycleStatus.PartiallyImplemented);
    }
    private static void AddInfrastructureFeatures(List<FeatureDefinition>Features){
        Add(Features,"F-L7-021","Feature Runner","L7",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L9-008","Feature stage packs","L9",FeatureLifecycleStatus.Implemented);
        Add(Features,"F-L0-008","D3D12 compute and readback","L0",FeatureLifecycleStatus.Deferred);
        Add(Features,"F-L3-019","Deterministic job runtime","L3",FeatureLifecycleStatus.Deferred);
        Add(Features,ExtendedEcsMaskFeatureId,"Extended static ECS component mask","L3",FeatureLifecycleStatus.Deferred);
        Add(Features,"F-L4-003","Simulation backend contracts and adapters","L4",FeatureLifecycleStatus.Deferred);
        Add(Features,"F-L6-003","Field and graph artifacts","L6",FeatureLifecycleStatus.Deferred);
        Add(Features,"F-L5-032","Animation physics pose bridge","L5",FeatureLifecycleStatus.Deferred);
        Add(Features,"F-L5-033","Volume and field renderer","L5",FeatureLifecycleStatus.Deferred);
        Add(Features,"F-L5-034","Thermal field domain","L5",FeatureLifecycleStatus.Deferred);
    }
    private static void AddDeferredSimulationFeatures(List<FeatureDefinition>Features){
        var Names=new[]{"Joints","Ragdoll","Character controller","Vehicle","Dynamic-dynamic CCD","Parallel solver","GPU solver","Rollback","PBD and XPBD","Cloth","Rope","Soft body","FEM","MPM","SPH and PBF","FLIP and APIC","Eulerian grid fluid","Runtime Voronoi and Boolean destruction","Voxel and SDF destruction","Kepler orbit","Direct N-body","Barnes-Hut gravity","Fast multipole gravity","Hierarchical celestial coordinates","Gameplay probability domain","Wavefunction grid","Monte Carlo scientific quantum domain","Cross-domain coupling"};
        for(var Index=0;Index<DeferredSimulationFeatureIds.Count;Index++){
            Add(Features,DeferredSimulationFeatureIds[Index],Names[Index],"L5",FeatureLifecycleStatus.Deferred);
        }
    }
    private static void AddExistingStages(List<FeatureStageDefinition>Stages){
        AddExecutable(Stages,"foundation.codegen","Source generator contracts",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L7-001","F-L7-002","F-L7-012","F-L7-013","F-L7-015","F-L7-016","F-L7-017"],[Test("tests/CSharpGameEngine.CodeAnalysis.Tests/CSharpGameEngine.CodeAnalysis.Tests.csproj")],["src/CSharpGameEngine.CodeAnalysis/","tests/CSharpGameEngine.CodeAnalysis.Tests/"],"All generator and analyzer tests exit with code zero.",900,["changed","smoke","full","stress"]);
        AddExecutable(Stages,"foundation.core-spatial","Core coordinate contracts",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L1-001","F-L1-002","F-L1-003","F-L1-005","F-L1-006","F-L2-003","F-L0-006"],[Test("tests/CSharpGameEngine.Numerics.Tests/CSharpGameEngine.Numerics.Tests.csproj"),Test("tests/CSharpGameEngine.Core.Tests/CSharpGameEngine.Core.Tests.csproj")],["src/CSharpGameEngine.Core/","src/CSharpGameEngine.Numerics/","tests/CSharpGameEngine.Numerics.Tests/","tests/CSharpGameEngine.Core.Tests/"],"Core and numerical contract suites exit with code zero.",900,["changed","smoke","full","stress"]);
        AddExecutable(Stages,"geometry.import","Geometry and import contracts",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L0-003","F-L0-005","F-L2-002","F-L7-005"],[Test("tests/CSharpGameEngine.Import.Tests/CSharpGameEngine.Import.Tests.csproj")],["src/CSharpGameEngine.Import/","tests/CSharpGameEngine.Import.Tests/","tools/CSharpGameEngine.Unreal.SceneImporter/"],"Import fixtures and coordinate conversions match their expected artifacts.",900,["changed","smoke","full"]);
        AddExecutable(Stages,"rendering.warp-triangle","D3D12 WARP visual readback",FeatureStagePack.Rendering,FeatureStageMode.Visual,["F-L0-001","F-L0-002","F-L0-004","F-L0-007","F-L4-002"],[Scene("stages/CSharpGameEngine.FeatureStages.Rendering/CSharpGameEngine.FeatureStages.Rendering.csproj","rendering.warp-triangle"),new FeatureStageCommand("dotnet",["run","--project","tools/CSharpGameEngine.TestHost/CSharpGameEngine.TestHost.csproj","--configuration","{configuration}","--","triangle","--hidden","--frames","3","--debug-layer","--verify"],["--warp"],[])],["src/CSharpGameEngine.Windows/","src/CSharpGameEngine.Runtime.Windows/","tools/CSharpGameEngine.TestHost/","stages/CSharpGameEngine.FeatureStages.Rendering/","assets/Shaders/"],"C# Scene instantiate/unload succeeds, readback contains the rendered triangle, and the D3D12 debug layer reports no errors.",900,["changed","smoke","full","stress"],FeatureAdapterRequirement.WarpRequiredHardwareOptional);
        AddExecutable(Stages,"ecs.scene","Static ECS and scene lifecycle",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L3-011","F-L3-012","F-L3-013"],[Test("tests/CSharpGameEngine.CodeAnalysis.Tests/CSharpGameEngine.CodeAnalysis.Tests.csproj","FullyQualifiedName~Scene|FullyQualifiedName~Archetype|FullyQualifiedName~Startup")],["src/CSharpGameEngine.Entities/","src/CSharpGameEngine.CodeAnalysis/","tests/CSharpGameEngine.Entities.Tests/","tests/CSharpGameEngine.CodeAnalysis.Tests/"],"Generated archetype, scene, and startup lifecycle contracts pass.",900,["changed","smoke","full","stress"]);
        AddExecutable(Stages,"audio.windows","Audio abstraction and XAudio2 contracts",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L1-007","F-L4-001"],[Test("tests/CSharpGameEngine.Core.Tests/CSharpGameEngine.Core.Tests.csproj","FullyQualifiedName~Audio"),Test("tests/CSharpGameEngine.Windows.Tests/CSharpGameEngine.Windows.Tests.csproj","FullyQualifiedName~Audio")],["src/CSharpGameEngine.Core/Audio/","src/CSharpGameEngine.Windows/Audio/","tests/CSharpGameEngine.Core.Tests/Audio/"],"Audio abstraction and backend contract tests pass without requiring audible output.",900,["changed","smoke","full"]);
        AddExecutable(Stages,"vsix.toolchain","VSIX command and bridge contracts",FeatureStagePack.Vsix,FeatureStageMode.Headless,["F-L8-001","F-L8-002","F-L8-005","F-L8-008"],[Test("tests/CSharpGameEngine.VSExtension.Tests/CSharpGameEngine.VSExtension.Tests.csproj")],["tools/CSharpGameEngine.VSExtension/","tests/CSharpGameEngine.VSExtension.Tests/","tools/CSharpGameEngine.Bridge.Hosting/"],"VSIX model, package, and bridge tests pass.",1800,["changed","smoke","full","vsix"]);
        AddExecutable(Stages,"vsix.scene-editor","Scene editor contracts",FeatureStagePack.Vsix,FeatureStageMode.Headless,["F-L8-010","F-L8-011","F-L8-012","F-L8-015","F-L8-016","F-L8-017"],[Test("tests/CSharpGameEngine.VSExtension.Tests/CSharpGameEngine.VSExtension.Tests.csproj")],["tools/CSharpGameEngine.VSExtension/","tests/CSharpGameEngine.VSExtension.Tests/","Docs/Specifications/Scene-Editor-Design.md"],"Scene editor state, editing, localization, layout, and Escape routing tests pass.",1800,["changed","smoke","full","vsix"]);
        AddExecutable(Stages,"spatial.runtime","Spatial runtime and debug contracts",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L3-015","F-L3-016","F-L3-017"],[Test("tests/CSharpGameEngine.Windows.Tests/CSharpGameEngine.Windows.Tests.csproj","FullyQualifiedName~Spatial"),Test("tests/CSharpGameEngine.CodeAnalysis.Tests/CSharpGameEngine.CodeAnalysis.Tests.csproj","FullyQualifiedName~Spatial")],["src/CSharpGameEngine.Entities/Spatial","src/CSharpGameEngine.CodeAnalysis/Spatial","tests/CSharpGameEngine.Windows.Tests/Runtime/Spatial"],"Octree, world snapshot, and debug projection tests pass.",900,["changed","smoke","full","stress"]);
        AddExecutable(Stages,"rigid.physics","Deterministic physics foundation",FeatureStagePack.Rigid,FeatureStageMode.Headless,["F-L5-001","F-L5-002","F-L5-003","F-L3-018","F-L6-002","F-L7-020","F-L8-014"],[Test("tests/CSharpGameEngine.Windows.Tests/CSharpGameEngine.Windows.Tests.csproj","FullyQualifiedName~Physics|FullyQualifiedName~Simulation")],["src/CSharpGameEngine.Runtime.Simulation/","src/CSharpGameEngine.Runtime.Physics/","tools/CSharpGameEngine.Physics.Cli/","tests/CSharpGameEngine.Windows.Tests/Physics/"],"Deterministic state, message, rigid-body, cooking, cache, and debug contracts pass.",1800,["changed","smoke","full","stress"]);
        AddExecutable(Stages,"runner.contract","Feature Runner catalog and result contracts",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L7-021"],[Test("tests/CSharpGameEngine.FeatureRunner.Tests/CSharpGameEngine.FeatureRunner.Tests.csproj")],["tools/CSharpGameEngine.FeatureRunner/","tests/CSharpGameEngine.FeatureRunner.Tests/","Docs/Feature-Stages.md"],"Catalog coverage, CLI selection, and versioned result serialization tests pass.",600,["changed","smoke","full","stress"]);
        AddExecutable(Stages,"stage-packs.contract","C# IScene Stage Pack contracts",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L9-008"],[Scene("stages/CSharpGameEngine.FeatureStages.Foundation/CSharpGameEngine.FeatureStages.Foundation.csproj","foundation.reference"),Scene("stages/CSharpGameEngine.FeatureStages.Rendering/CSharpGameEngine.FeatureStages.Rendering.csproj","rendering.warp-triangle"),Scene("stages/CSharpGameEngine.FeatureStages.Rigid/CSharpGameEngine.FeatureStages.Rigid.csproj","rigid.joints"),Scene("stages/CSharpGameEngine.FeatureStages.Deformable/CSharpGameEngine.FeatureStages.Deformable.csproj","deformable.xpbd"),Scene("stages/CSharpGameEngine.FeatureStages.Fluid/CSharpGameEngine.FeatureStages.Fluid.csproj","fluid.sph-pbf"),Scene("stages/CSharpGameEngine.FeatureStages.Destruction/CSharpGameEngine.FeatureStages.Destruction.csproj","destruction.runtime-topology"),Scene("stages/CSharpGameEngine.FeatureStages.Celestial/CSharpGameEngine.FeatureStages.Celestial.csproj","celestial.kepler"),Scene("stages/CSharpGameEngine.FeatureStages.ProbabilityQuantum/CSharpGameEngine.FeatureStages.ProbabilityQuantum.csproj","probability.gameplay"),Scene("stages/CSharpGameEngine.FeatureStages.Coupling/CSharpGameEngine.FeatureStages.Coupling.csproj","coupling.multidomain"),Scene("stages/CSharpGameEngine.FeatureStages.Vsix/CSharpGameEngine.FeatureStages.Vsix.csproj","vsix.scene-editor-visual")],["stages/"],"Every isolated Pack compiles its C# IScene, instantiates generated ECS entities, verifies ownership, unloads them, and emits a stable state hash.",1200,["changed","smoke","full","stress","vsix"]);
    }
    private static void AddInfrastructureStages(List<FeatureStageDefinition>Stages){
        AddDeferred(Stages,"foundation.d3d12-compute","D3D12 compute and readback",FeatureStagePack.Rendering,FeatureStageMode.Visual,["F-L0-008"],"Known compute kernel output must match the CPU vector and debug validation must remain clean.");
        AddDeferred(Stages,"foundation.deterministic-jobs","Deterministic job runtime",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L3-019"],"Single-thread and scheduled execution must produce identical state and event hashes.");
        AddDeferred(Stages,"foundation.extended-ecs-mask","Extended static ECS mask",FeatureStagePack.Foundation,FeatureStageMode.Headless,[ExtendedEcsMaskFeatureId],"1-64 components retain the single-mask generated path; 65-128 use two generator-time masks; 129 emits an explicit diagnostic.");
        AddDeferred(Stages,"foundation.simulation-adapters","Simulation adapter boundaries",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L4-003"],"Provider conformance vectors pass without exposing provider types above L4.");
        AddDeferred(Stages,"foundation.field-artifacts","Field and graph artifacts",FeatureStagePack.Foundation,FeatureStageMode.Headless,["F-L6-003"],"Version, content hash, dependency hash, units, and provenance round-trip exactly.");
        AddDeferred(Stages,"deformable.pose-bridge","Animation physics pose bridge",FeatureStagePack.Deformable,FeatureStageMode.Visual,["F-L5-032"],"Animation-to-physics and physics-to-animation handoff occurs only at the declared tick boundary.");
        AddDeferred(Stages,"fluid.volume-renderer","Volume and field renderer",FeatureStagePack.Fluid,FeatureStageMode.Visual,["F-L5-033"],"WARP readback matches the reference density and transfer-function image tolerance.");
        AddDeferred(Stages,"coupling.thermal-field","Thermal field domain",FeatureStagePack.Coupling,FeatureStageMode.Visual,["F-L5-034"],"Diffusion reference case remains within the declared energy and L2 error budgets.");
    }
    private static void AddDeferredSimulationStages(List<FeatureStageDefinition>Stages){
        AddDeferred(Stages,"rigid.joints","Joint constraints",FeatureStagePack.Rigid,FeatureStageMode.Visual,["F-L5-004"],"Pendulum and locked-joint constraint drift remain within the deterministic error budget.");
        AddDeferred(Stages,"rigid.ragdoll","Ragdoll",FeatureStagePack.Rigid,FeatureStageMode.Visual,["F-L5-005"],"Pose handoff, joint limits, and final state hash match the reference run.");
        AddDeferred(Stages,"rigid.character-controller","Character controller",FeatureStagePack.Rigid,FeatureStageMode.Visual,["F-L5-006"],"Sweep movement traverses the reference stair and slope course without penetration.");
        AddDeferred(Stages,"rigid.vehicle","Vehicle",FeatureStagePack.Rigid,FeatureStageMode.Visual,["F-L5-007"],"Reference acceleration, braking, suspension travel, and lap state remain within tolerance.");
        AddDeferred(Stages,"rigid.dynamic-ccd","Dynamic-dynamic CCD",FeatureStagePack.Rigid,FeatureStageMode.Visual,["F-L5-008"],"Opposing high-speed bodies produce a TOI contact without tunneling.");
        AddDeferred(Stages,"rigid.parallel-solver","Parallel rigid solver",FeatureStagePack.Rigid,FeatureStageMode.Headless,["F-L5-009"],"Graph-colored execution matches the single-thread state and event hashes.");
        AddDeferred(Stages,"rigid.gpu-solver","GPU rigid solver",FeatureStagePack.Rigid,FeatureStageMode.Visual,["F-L5-010"],"WARP compute output matches the CPU reference within the backend-specific tolerance.");
        AddDeferred(Stages,"rigid.rollback","Physics rollback",FeatureStagePack.Rigid,FeatureStageMode.Headless,["F-L5-011"],"Snapshot restore and command replay reproduce the original state and event hashes.");
        AddDeferred(Stages,"deformable.xpbd","PBD and XPBD",FeatureStagePack.Deformable,FeatureStageMode.Visual,["F-L5-012"],"Distance-constraint convergence and energy behavior match the reference vectors.");
        AddDeferred(Stages,"deformable.cloth","Cloth",FeatureStagePack.Deformable,FeatureStageMode.Visual,["F-L5-013"],"Pinned cloth settles without inversion and matches reference vertex tolerances.");
        AddDeferred(Stages,"deformable.rope","Rope",FeatureStagePack.Deformable,FeatureStageMode.Visual,["F-L5-014"],"Endpoint constraints, segment lengths, and collision events remain within tolerance.");
        AddDeferred(Stages,"deformable.soft-body","Soft body",FeatureStagePack.Deformable,FeatureStageMode.Visual,["F-L5-015"],"Volume preservation and collision recovery match the selected backend reference.");
        AddDeferred(Stages,"deformable.fem","Finite element method",FeatureStagePack.Deformable,FeatureStageMode.Headless,["F-L5-016"],"Canonical beam displacement and stress stay within documented discretization error.");
        AddDeferred(Stages,"deformable.mpm","Material point method",FeatureStagePack.Deformable,FeatureStageMode.Visual,["F-L5-017"],"Mass and momentum budgets close for the reference transfer cycle.");
        AddDeferred(Stages,"fluid.sph-pbf","SPH and PBF",FeatureStagePack.Fluid,FeatureStageMode.Visual,["F-L5-018"],"Density error, mass conservation, and boundary impulse remain within tolerance.");
        AddDeferred(Stages,"fluid.flip-apic","FLIP and APIC",FeatureStagePack.Fluid,FeatureStageMode.Visual,["F-L5-019"],"Particle-grid transfer preserves mass and bounded kinetic energy.");
        AddDeferred(Stages,"fluid.eulerian-grid","Eulerian grid fluid",FeatureStagePack.Fluid,FeatureStageMode.Visual,["F-L5-020"],"Projection reduces divergence and conserves scalar mass within tolerance.");
        AddDeferred(Stages,"destruction.runtime-topology","Runtime Voronoi and Boolean destruction",FeatureStagePack.Destruction,FeatureStageMode.Visual,["F-L5-021"],"Fragments form a closed deterministic partition with stable IDs and valid cooked collision.");
        AddDeferred(Stages,"destruction.voxel-sdf","Voxel and SDF destruction",FeatureStagePack.Destruction,FeatureStageMode.Visual,["F-L5-022"],"Reference cut updates only affected chunks and extracted surface hashes remain deterministic.");
        AddDeferred(Stages,"celestial.kepler","Kepler orbit",FeatureStagePack.Celestial,FeatureStageMode.Visual,["F-L5-023"],"One-period orbit closes within the declared position and energy error budgets.");
        AddDeferred(Stages,"celestial.direct-nbody","Direct N-body",FeatureStagePack.Celestial,FeatureStageMode.Visual,["F-L5-024"],"Two-body and three-body references remain within energy and momentum budgets.");
        AddDeferred(Stages,"celestial.barnes-hut","Barnes-Hut gravity",FeatureStagePack.Celestial,FeatureStageMode.Visual,["F-L5-025"],"Approximate forces stay within theta-dependent error against direct N-body.");
        AddDeferred(Stages,"celestial.fmm","Fast multipole gravity",FeatureStagePack.Celestial,FeatureStageMode.Headless,["F-L5-026"],"Multipole field samples stay within order-dependent error against direct N-body.");
        AddDeferred(Stages,"celestial.hierarchical-frames","Hierarchical celestial coordinates",FeatureStagePack.Celestial,FeatureStageMode.Visual,["F-L5-027"],"Round-trip frame transforms and rebasing preserve declared precision at every scale.");
        AddDeferred(Stages,"probability.gameplay","Gameplay probability domain",FeatureStagePack.ProbabilityQuantum,FeatureStageMode.Visual,["F-L5-028"],"Seeded discrete observations reproduce the golden event sequence and distribution checks.");
        AddDeferred(Stages,"probability.wavefunction-grid","Wavefunction grid",FeatureStagePack.ProbabilityQuantum,FeatureStageMode.Visual,["F-L5-029"],"Norm, phase evolution, and analytic reference error stay within the declared budget.");
        AddDeferred(Stages,"probability.monte-carlo-quantum","Monte Carlo scientific quantum domain",FeatureStagePack.ProbabilityQuantum,FeatureStageMode.Headless,["F-L5-030"],"Seeded estimates report provenance and confidence intervals covering the analytic reference.");
        AddDeferred(Stages,"coupling.multidomain","Cross-domain coupling",FeatureStagePack.Coupling,FeatureStageMode.Visual,["F-L5-031"],"One-tick bridges preserve ownership, ordering, units, and declared conservation budgets.");
    }
    private static void Add(List<FeatureDefinition>Features,string Id,string Name,string Layer,FeatureLifecycleStatus Status)=>Features.Add(new FeatureDefinition(Id,Name,Layer,Status));
    private static FeatureStageCommand Test(string Project,string?Filter=null){
        var Arguments=new List<string>{"test",Project,"--configuration","{configuration}","--nologo"};
        if(!string.IsNullOrWhiteSpace(Filter)){ Arguments.AddRange(["--filter",Filter]); }
        return new FeatureStageCommand("dotnet",Arguments);
    }
    private static FeatureStageCommand Scene(string Project,string Id)=>new("dotnet",["run","--project",Project,"--configuration","{configuration}","--","run",Id]);
    private static void AddExecutable(List<FeatureStageDefinition>Stages,string Id,string Name,FeatureStagePack Pack,FeatureStageMode Mode,IReadOnlyList<string>FeatureIds,IReadOnlyList<FeatureStageCommand>Commands,IReadOnlyList<string>InputPaths,string Oracle,int TimeoutSeconds,IReadOnlyList<string>Profiles,FeatureAdapterRequirement AdapterRequirement=FeatureAdapterRequirement.None)=>Stages.Add(new FeatureStageDefinition(Id,Name,Pack,Mode,FeatureStageAvailability.Executable,AdapterRequirement,FeatureIds,Profiles,5489,600,TimeoutSeconds,Oracle,Commands,InputPaths,SceneProject(Mode,Pack),SceneId(Mode,Id)));
    private static void AddDeferred(List<FeatureStageDefinition>Stages,string Id,string Name,FeatureStagePack Pack,FeatureStageMode Mode,IReadOnlyList<string>FeatureIds,string Oracle)=>Stages.Add(new FeatureStageDefinition(Id,Name,Pack,Mode,FeatureStageAvailability.NotImplemented,Mode==FeatureStageMode.Visual?FeatureAdapterRequirement.WarpRequiredHardwareOptional:FeatureAdapterRequirement.None,FeatureIds,["full","stress"],5489,600,1800,Oracle,[],[],SceneProject(Mode,Pack),SceneId(Mode,Id)));
    private static string?SceneProject(FeatureStageMode Mode,FeatureStagePack Pack)=>Mode==FeatureStageMode.Visual?$"stages/CSharpGameEngine.FeatureStages.{Pack}/CSharpGameEngine.FeatureStages.{Pack}.csproj":null;
    private static string?SceneId(FeatureStageMode Mode,string Id)=>Mode==FeatureStageMode.Visual?Id:null;
}
