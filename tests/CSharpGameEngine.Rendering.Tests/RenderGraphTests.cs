namespace CSharpGameEngine.Rendering.Tests;

public sealed class RenderGraphTests {
    private sealed class Color;
    private sealed class Depth;

    [Fact]
    public void Compile_DerivesRawWarAndWawDependencies(){
        RenderGraphBuilder Builder=new();
        var Color=Builder.CreateTransient<Color>("Color",RenderResourceDescriptor.Texture(1280,720,"rgba16f"));
        RenderPassHandle Clear=Builder.AddPass("Clear",Pass=>Pass.Write(Color));
        RenderPassHandle Sample=Builder.AddPass("Sample",Pass=>Pass.Read(Color));
        RenderPassHandle Composite=Builder.AddPass("Composite",Pass=>Pass.Write(Color));

        RenderGraph Graph=Builder.Compile();

        Assert.Equal([Clear.Index],Graph.Passes.Single(Pass=>Pass.Index==Sample.Index).Dependencies);
        Assert.Equal(
            [Clear.Index,Sample.Index],
            Graph.Passes.Single(Pass=>Pass.Index==Composite.Index).Dependencies);
        Assert.Equal(["Clear","Sample","Composite"],Graph.Passes.Select(static Pass=>Pass.Name));
        Assert.Equal(
            [
                (RenderResourceState.Undefined,RenderResourceState.Write),
                (RenderResourceState.Write,RenderResourceState.Read),
                (RenderResourceState.Read,RenderResourceState.Write),
            ],
            Graph.Transitions.Select(static Transition=>(Transition.Before,Transition.After)));
    }

    [Fact]
    public void Compile_RejectsTransientReadBeforeFirstWrite(){
        RenderGraphBuilder Builder=new();
        var Color=Builder.CreateTransient<Color>("Color",RenderResourceDescriptor.Texture(1,1,"rgba8"));
        Builder.AddPass("Invalid",Pass=>Pass.Read(Color));

        RenderGraphValidationException Exception=Assert.Throws<RenderGraphValidationException>(Builder.Compile);

        Assert.Contains("before its first write",Exception.Message,StringComparison.Ordinal);
    }

    [Fact]
    public void ImportedResource_IsInitializedAndNeverAliased(){
        RenderGraphBuilder Builder=new();
        var BackBuffer=Builder.Import<Color>("BackBuffer",RenderResourceDescriptor.Texture(1920,1080,"rgba8"));
        Builder.AddPass("Present",Pass=>Pass.Read(BackBuffer));

        RenderGraph Graph=Builder.Compile();

        RenderResourcePlan Resource=Assert.Single(Graph.Resources);
        Assert.True(Resource.Imported);
        Assert.Equal(-1,Resource.AliasSlot);
    }

    [Fact]
    public void Compile_ReusesOnlyCompatibleNonOverlappingTransientMemory(){
        RenderGraphBuilder Builder=new();
        RenderResourceDescriptor Descriptor=RenderResourceDescriptor.Texture(512,512,"rgba16f");
        var First=Builder.CreateTransient<Color>("First",Descriptor);
        var Overlapping=Builder.CreateTransient<Color>("Overlapping",Descriptor);
        var Reusable=Builder.CreateTransient<Color>("Reusable",Descriptor);
        var Different=Builder.CreateTransient<Depth>("Different",RenderResourceDescriptor.Texture(512,512,"d32"));
        Builder.AddPass("FirstWrite",Pass=>Pass.Write(First));
        Builder.AddPass("FirstReadAndOverlapWrite",Pass=>{
            Pass.Read(First);
            Pass.Write(Overlapping);
        });
        Builder.AddPass("Later",Pass=>{
            Pass.Write(Reusable);
            Pass.Write(Different);
        });

        RenderGraph Graph=Builder.Compile();
        RenderResourcePlan FirstPlan=Graph.Resources[First.Index];
        RenderResourcePlan OverlapPlan=Graph.Resources[Overlapping.Index];
        RenderResourcePlan ReusablePlan=Graph.Resources[Reusable.Index];
        RenderResourcePlan DifferentPlan=Graph.Resources[Different.Index];

        Assert.NotEqual(FirstPlan.AliasSlot,OverlapPlan.AliasSlot);
        Assert.Equal(FirstPlan.AliasSlot,ReusablePlan.AliasSlot);
        Assert.NotEqual(ReusablePlan.AliasSlot,DifferentPlan.AliasSlot);
        Assert.True(FirstPlan.LastUse<ReusablePlan.FirstUse);
    }

    [Fact]
    public void Compile_RejectsExplicitDependencyCycle(){
        RenderGraphBuilder Builder=new();
        var Color=Builder.CreateTransient<Color>("Color",RenderResourceDescriptor.Texture(1,1,"rgba8"));
        RenderPassHandle Writer=Builder.AddPass("Writer",Pass=>Pass.Write(Color));
        RenderPassHandle Reader=Builder.AddPass("Reader",Pass=>Pass.Read(Color));
        Builder.AddDependency(Reader,Writer);

        RenderGraphValidationException Exception=Assert.Throws<RenderGraphValidationException>(Builder.Compile);

        Assert.Contains("cycle",Exception.Message,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Compile_DerivesCrossQueueOwnershipTransfer(){
        RenderGraphBuilder Builder=new();
        var Buffer=Builder.CreateTransient<Color>("Buffer",RenderResourceDescriptor.Buffer(256));
        RenderPassHandle Compute=Builder.AddPass(
            "Compute",RenderQueue.Compute,Pass=>Pass.Write(Buffer));
        RenderPassHandle Copy=Builder.AddPass(
            "Readback",RenderQueue.Copy,Pass=>Pass.Read(Buffer));

        RenderGraph Graph=Builder.Compile();
        RenderTransitionPlan Transfer=Assert.Single(
            Graph.Transitions,static Transition=>Transition.QueueOwnershipTransfer);

        Assert.Equal(Compute.Index,Assert.Single(
            Graph.Passes,Pass=>Pass.Index==Copy.Index).Dependencies[0]);
        Assert.Equal(RenderQueue.Compute,Transfer.SourceQueue);
        Assert.Equal(RenderQueue.Copy,Transfer.DestinationQueue);
        Assert.Equal(Copy.Index,Transfer.PassIndex);
    }

    [Fact]
    public void HandlesAndPassBuildersCannotEscapeTheirOwnerOrDeclaration(){
        RenderGraphBuilder FirstBuilder=new();
        RenderGraphBuilder SecondBuilder=new();
        var Foreign=FirstBuilder.CreateTransient<Color>("Foreign",RenderResourceDescriptor.Buffer(64));
        RenderPassBuilder? Escaped=null;
        FirstBuilder.AddPass("Owner",Pass=>{
            Escaped=Pass;
            Pass.Write(Foreign);
        });

        Assert.Throws<ArgumentException>(()=>SecondBuilder.AddPass("Invalid",Pass=>Pass.Read(Foreign)));
        Assert.Throws<InvalidOperationException>(()=>Escaped!.Read(Foreign));
    }
}
