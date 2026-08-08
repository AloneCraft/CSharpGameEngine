internal partial struct System0:ISystem{
    public bool Update(Entity Entity,ref Component0 Component0,ref State State){
        var Component1=new Component1();
        State.World.MoveArchetype(Entity, Component0, Component1,ref State);
        return true;
    }
    public bool Update(Entity Entity,ref Component1 Component1,ref State State){
        var Component0=new Component0();
        State.World.MoveArchetype(Entity, Component0, Component1,ref State);
        return true;
    }
    public bool Update(Entity Entity,ref Component0 Component0,ref Component1 Component1,ref State State){
        State.World.MoveArchetype(Entity, Component0, Component1,ref State);
        return true;
    }
    public bool Update(Entity Entity,ref Component2 Component2,ref State State){
        //Component2を利用した任意の処理
        return true;
    }
}
internal partial struct System1:ISystem{
    internal bool Update(ref Component0 Component0,ref State State){
        return true;
    }
    internal bool Update(ref Component1 Component1,ref State State){
        return true;
    }
    internal bool Update(ref Component0 Component0,ref Component1 Component1,ref State State){
        return true;
    }
}
