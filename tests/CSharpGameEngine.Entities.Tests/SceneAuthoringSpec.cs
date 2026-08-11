namespace CSharpGameEngine.Entities.Tests;

public class SceneAuthoringSpec {
    [Fact]
    public void SceneNodeId_Equality_IsValueBased(){
        SceneNodeId A=new(42UL);
        SceneNodeId B=new(42UL);
        SceneNodeId C=new(43UL);

        Assert.Equal(A,B);
        Assert.NotEqual(A,C);
    }

    [Fact]
    public void SceneNodeSymbol_Equality_IsValueBased(){
        SceneNodeSymbol A=new(new SceneNodeId(1UL),"Root");
        SceneNodeSymbol B=new(new SceneNodeId(1UL),"Root");
        SceneNodeSymbol C=new(new SceneNodeId(1UL),"Player");

        Assert.Equal(A,B);
        Assert.NotEqual(A,C);
    }

    [Fact]
    public void Parent_CanBeAttachedToEntity_AndReadBack(){
        World world=new();

        Entity Root=world.Create(new Position());
        Entity Child=world.Create(new Position(),new Parent { Value=Root });

        Assert.Equal(Root,world.Archetype2.GetParentRefUnchecked(0).Value);
        Assert.Equal(Child,world.Archetype2.GetEntityRefUnchecked(0));
    }

    [Fact]
    public void SceneNodeTag_CanBeUsedAsSoleArchetypeComponent_ForEmptyNodes(){
        World world=new();

        Entity entity=world.Create(new SceneNodeTag());

        Assert.Equal(1,world.Archetype3.Count);
        Assert.Equal(entity,world.Archetype3.GetEntityRefUnchecked(0));
    }

    [Fact]
    public void SceneBuilder_Create_InvokesDefinitionCallback(){
        SceneBuilder Builder=new();
        var Invoked=false;

        Builder.Create(new SceneNodeSymbol(new SceneNodeId(1UL),"Root"),_=>Invoked=true);

        Assert.True(Invoked);
    }

    [Fact]
    public void SceneBuilder_Create_NullDefinition_Throws(){
        SceneBuilder Builder=new();

        Assert.Throws<ArgumentNullException>(
            ()=>Builder.Create(new SceneNodeSymbol(new SceneNodeId(1UL),"Root"),null!));
    }

    [Fact]
    public void SceneNodeBuilder_Symbol_ReturnsConstructorValue(){
        SceneNodeSymbol Symbol=new(new SceneNodeId(7UL),"Root");
        SceneNodeSymbol?Observed=null;

        new SceneBuilder().Create(Symbol,Node=>Observed=Node.Symbol);

        Assert.Equal(Symbol,Observed);
    }

    [Fact]
    public void SceneNodeBuilder_Create_InvokesChildDefinitionCallback_WithChildSymbol(){
        SceneBuilder Builder=new();
        SceneNodeSymbol ChildSymbol=new(new SceneNodeId(2UL),"Child");
        SceneNodeSymbol?Observed=null;

        Builder.Create(new SceneNodeSymbol(new SceneNodeId(1UL),"Root"),Node=>{
            Node.Create(ChildSymbol,Child=>Observed=Child.Symbol);
        });

        Assert.Equal(ChildSymbol,Observed);
    }

    [Fact]
    public void SceneNodeBuilder_Create_NullDefinition_Throws(){
        SceneBuilder Builder=new();

        Assert.Throws<ArgumentNullException>(()=>Builder.Create(
            new SceneNodeSymbol(new SceneNodeId(1UL),"Root"),
            Node=>Node.Create(new SceneNodeSymbol(new SceneNodeId(2UL),"Child"),null!)));
    }

    [Fact]
    public void SceneNodeBuilder_Set_DoesNotThrow(){
        SceneBuilder Builder=new();

        Exception?exception=Record.Exception(()=>
            Builder.Create(new SceneNodeSymbol(new SceneNodeId(1UL),"Root"),Node=>Node.Set(new Position())));

        Assert.Null(exception);
    }
}

