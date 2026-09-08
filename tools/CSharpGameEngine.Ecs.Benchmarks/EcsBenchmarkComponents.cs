using CSharpGameEngine.Entities;

namespace CSharpGameEngine.Ecs.Benchmarks;

public struct C0 { public int Value; }
#if !ECS_COMPONENT_COUNT_1
public struct C1 { public int Value; }
public struct C2 { public int Value; }
public struct C3 { public int Value; }
public struct C4 { public int Value; }
public struct C5 { public int Value; }
public struct C6 { public int Value; }
public struct C7 { public int Value; }
public struct C8 { public int Value; }
public struct C9 { public int Value; }
public struct C10 { public int Value; }
public struct C11 { public int Value; }
public struct C12 { public int Value; }
public struct C13 { public int Value; }
public struct C14 { public int Value; }
public struct C15 { public int Value; }
public struct C16 { public int Value; }
public struct C17 { public int Value; }
public struct C18 { public int Value; }
public struct C19 { public int Value; }
public struct C20 { public int Value; }
public struct C21 { public int Value; }
public struct C22 { public int Value; }
public struct C23 { public int Value; }
public struct C24 { public int Value; }
public struct C25 { public int Value; }
public struct C26 { public int Value; }
public struct C27 { public int Value; }
public struct C28 { public int Value; }
public struct C29 { public int Value; }
public struct C30 { public int Value; }
public struct C31 { public int Value; }
public struct C32 { public int Value; }
public struct C33 { public int Value; }
public struct C34 { public int Value; }
public struct C35 { public int Value; }
public struct C36 { public int Value; }
public struct C37 { public int Value; }
public struct C38 { public int Value; }
public struct C39 { public int Value; }
public struct C40 { public int Value; }
public struct C41 { public int Value; }
public struct C42 { public int Value; }
public struct C43 { public int Value; }
public struct C44 { public int Value; }
public struct C45 { public int Value; }
public struct C46 { public int Value; }
public struct C47 { public int Value; }
public struct C48 { public int Value; }
public struct C49 { public int Value; }
public struct C50 { public int Value; }
public struct C51 { public int Value; }
public struct C52 { public int Value; }
public struct C53 { public int Value; }
public struct C54 { public int Value; }
public struct C55 { public int Value; }
public struct C56 { public int Value; }
public struct C57 { public int Value; }
public struct C58 { public int Value; }
public struct C59 { public int Value; }
public struct C60 { public int Value; }
public struct C61 { public int Value; }
public struct C62 { public int Value; }
public struct C63 { public int Value; }
#endif
#if ECS_COMPONENT_COUNT_65 || ECS_COMPONENT_COUNT_128
public struct C64 { public int Value; }
#endif
#if ECS_COMPONENT_COUNT_128
public struct C65 { public int Value; }
public struct C66 { public int Value; }
public struct C67 { public int Value; }
public struct C68 { public int Value; }
public struct C69 { public int Value; }
public struct C70 { public int Value; }
public struct C71 { public int Value; }
public struct C72 { public int Value; }
public struct C73 { public int Value; }
public struct C74 { public int Value; }
public struct C75 { public int Value; }
public struct C76 { public int Value; }
public struct C77 { public int Value; }
public struct C78 { public int Value; }
public struct C79 { public int Value; }
public struct C80 { public int Value; }
public struct C81 { public int Value; }
public struct C82 { public int Value; }
public struct C83 { public int Value; }
public struct C84 { public int Value; }
public struct C85 { public int Value; }
public struct C86 { public int Value; }
public struct C87 { public int Value; }
public struct C88 { public int Value; }
public struct C89 { public int Value; }
public struct C90 { public int Value; }
public struct C91 { public int Value; }
public struct C92 { public int Value; }
public struct C93 { public int Value; }
public struct C94 { public int Value; }
public struct C95 { public int Value; }
public struct C96 { public int Value; }
public struct C97 { public int Value; }
public struct C98 { public int Value; }
public struct C99 { public int Value; }
public struct C100 { public int Value; }
public struct C101 { public int Value; }
public struct C102 { public int Value; }
public struct C103 { public int Value; }
public struct C104 { public int Value; }
public struct C105 { public int Value; }
public struct C106 { public int Value; }
public struct C107 { public int Value; }
public struct C108 { public int Value; }
public struct C109 { public int Value; }
public struct C110 { public int Value; }
public struct C111 { public int Value; }
public struct C112 { public int Value; }
public struct C113 { public int Value; }
public struct C114 { public int Value; }
public struct C115 { public int Value; }
public struct C116 { public int Value; }
public struct C117 { public int Value; }
public struct C118 { public int Value; }
public struct C119 { public int Value; }
public struct C120 { public int Value; }
public struct C121 { public int Value; }
public struct C122 { public int Value; }
public struct C123 { public int Value; }
public struct C124 { public int Value; }
public struct C125 { public int Value; }
public struct C126 { public int Value; }
public struct C127 { public int Value; }
#endif

internal struct BenchmarkShape:ISystem {
#if ECS_COMPONENT_COUNT_1
    public void Spawn(ref C0 C0) { }
#elif ECS_COMPONENT_COUNT_64
    public void Spawn(ref C0 C0,ref C1 C1,ref C2 C2,ref C3 C3,ref C4 C4,ref C5 C5,ref C6 C6,ref C7 C7,ref C8 C8,ref C9 C9,ref C10 C10,ref C11 C11,ref C12 C12,ref C13 C13,ref C14 C14,ref C15 C15,ref C16 C16,ref C17 C17,ref C18 C18,ref C19 C19,ref C20 C20,ref C21 C21,ref C22 C22,ref C23 C23,ref C24 C24,ref C25 C25,ref C26 C26,ref C27 C27,ref C28 C28,ref C29 C29,ref C30 C30,ref C31 C31,ref C32 C32,ref C33 C33,ref C34 C34,ref C35 C35,ref C36 C36,ref C37 C37,ref C38 C38,ref C39 C39,ref C40 C40,ref C41 C41,ref C42 C42,ref C43 C43,ref C44 C44,ref C45 C45,ref C46 C46,ref C47 C47,ref C48 C48,ref C49 C49,ref C50 C50,ref C51 C51,ref C52 C52,ref C53 C53,ref C54 C54,ref C55 C55,ref C56 C56,ref C57 C57,ref C58 C58,ref C59 C59,ref C60 C60,ref C61 C61,ref C62 C62,ref C63 C63) { }
#elif ECS_COMPONENT_COUNT_65
    public void Spawn(ref C0 C0,ref C1 C1,ref C2 C2,ref C3 C3,ref C4 C4,ref C5 C5,ref C6 C6,ref C7 C7,ref C8 C8,ref C9 C9,ref C10 C10,ref C11 C11,ref C12 C12,ref C13 C13,ref C14 C14,ref C15 C15,ref C16 C16,ref C17 C17,ref C18 C18,ref C19 C19,ref C20 C20,ref C21 C21,ref C22 C22,ref C23 C23,ref C24 C24,ref C25 C25,ref C26 C26,ref C27 C27,ref C28 C28,ref C29 C29,ref C30 C30,ref C31 C31,ref C32 C32,ref C33 C33,ref C34 C34,ref C35 C35,ref C36 C36,ref C37 C37,ref C38 C38,ref C39 C39,ref C40 C40,ref C41 C41,ref C42 C42,ref C43 C43,ref C44 C44,ref C45 C45,ref C46 C46,ref C47 C47,ref C48 C48,ref C49 C49,ref C50 C50,ref C51 C51,ref C52 C52,ref C53 C53,ref C54 C54,ref C55 C55,ref C56 C56,ref C57 C57,ref C58 C58,ref C59 C59,ref C60 C60,ref C61 C61,ref C62 C62,ref C63 C63,ref C64 C64) { }
#elif ECS_COMPONENT_COUNT_128
    public void Spawn(ref C0 C0,ref C1 C1,ref C2 C2,ref C3 C3,ref C4 C4,ref C5 C5,ref C6 C6,ref C7 C7,ref C8 C8,ref C9 C9,ref C10 C10,ref C11 C11,ref C12 C12,ref C13 C13,ref C14 C14,ref C15 C15,ref C16 C16,ref C17 C17,ref C18 C18,ref C19 C19,ref C20 C20,ref C21 C21,ref C22 C22,ref C23 C23,ref C24 C24,ref C25 C25,ref C26 C26,ref C27 C27,ref C28 C28,ref C29 C29,ref C30 C30,ref C31 C31,ref C32 C32,ref C33 C33,ref C34 C34,ref C35 C35,ref C36 C36,ref C37 C37,ref C38 C38,ref C39 C39,ref C40 C40,ref C41 C41,ref C42 C42,ref C43 C43,ref C44 C44,ref C45 C45,ref C46 C46,ref C47 C47,ref C48 C48,ref C49 C49,ref C50 C50,ref C51 C51,ref C52 C52,ref C53 C53,ref C54 C54,ref C55 C55,ref C56 C56,ref C57 C57,ref C58 C58,ref C59 C59,ref C60 C60,ref C61 C61,ref C62 C62,ref C63 C63,ref C64 C64,ref C65 C65,ref C66 C66,ref C67 C67,ref C68 C68,ref C69 C69,ref C70 C70,ref C71 C71,ref C72 C72,ref C73 C73,ref C74 C74,ref C75 C75,ref C76 C76,ref C77 C77,ref C78 C78,ref C79 C79,ref C80 C80,ref C81 C81,ref C82 C82,ref C83 C83,ref C84 C84,ref C85 C85,ref C86 C86,ref C87 C87,ref C88 C88,ref C89 C89,ref C90 C90,ref C91 C91,ref C92 C92,ref C93 C93,ref C94 C94,ref C95 C95,ref C96 C96,ref C97 C97,ref C98 C98,ref C99 C99,ref C100 C100,ref C101 C101,ref C102 C102,ref C103 C103,ref C104 C104,ref C105 C105,ref C106 C106,ref C107 C107,ref C108 C108,ref C109 C109,ref C110 C110,ref C111 C111,ref C112 C112,ref C113 C113,ref C114 C114,ref C115 C115,ref C116 C116,ref C117 C117,ref C118 C118,ref C119 C119,ref C120 C120,ref C121 C121,ref C122 C122,ref C123 C123,ref C124 C124,ref C125 C125,ref C126 C126,ref C127 C127) { }
#endif
}

internal struct HotSystem:ISystem {
    public int Calls;
#if ECS_COMPONENT_COUNT_1
    public bool Update(ref C0 Component,ref State State){ Component.Value++;this.Calls++;return true; }
#elif ECS_COMPONENT_COUNT_64
    public bool Update(ref C63 Component,ref State State){ Component.Value++;this.Calls++;return true; }
#elif ECS_COMPONENT_COUNT_65
    public bool Update(ref C64 Component,ref State State){ Component.Value++;this.Calls++;return true; }
#elif ECS_COMPONENT_COUNT_128
    public bool Update(ref C127 Component,ref State State){ Component.Value++;this.Calls++;return true; }
#endif
}

internal static class BenchmarkWorldFactory {
    public static Entity Create(World World,int InitialValue){
#if ECS_COMPONENT_COUNT_1
        return World.Create(new C0 { Value=InitialValue });
#elif ECS_COMPONENT_COUNT_64
        return World.Create(new C0 { Value=0 },new C1 { Value=0 },new C2 { Value=0 },new C3 { Value=0 },new C4 { Value=0 },new C5 { Value=0 },new C6 { Value=0 },new C7 { Value=0 },new C8 { Value=0 },new C9 { Value=0 },new C10 { Value=0 },new C11 { Value=0 },new C12 { Value=0 },new C13 { Value=0 },new C14 { Value=0 },new C15 { Value=0 },new C16 { Value=0 },new C17 { Value=0 },new C18 { Value=0 },new C19 { Value=0 },new C20 { Value=0 },new C21 { Value=0 },new C22 { Value=0 },new C23 { Value=0 },new C24 { Value=0 },new C25 { Value=0 },new C26 { Value=0 },new C27 { Value=0 },new C28 { Value=0 },new C29 { Value=0 },new C30 { Value=0 },new C31 { Value=0 },new C32 { Value=0 },new C33 { Value=0 },new C34 { Value=0 },new C35 { Value=0 },new C36 { Value=0 },new C37 { Value=0 },new C38 { Value=0 },new C39 { Value=0 },new C40 { Value=0 },new C41 { Value=0 },new C42 { Value=0 },new C43 { Value=0 },new C44 { Value=0 },new C45 { Value=0 },new C46 { Value=0 },new C47 { Value=0 },new C48 { Value=0 },new C49 { Value=0 },new C50 { Value=0 },new C51 { Value=0 },new C52 { Value=0 },new C53 { Value=0 },new C54 { Value=0 },new C55 { Value=0 },new C56 { Value=0 },new C57 { Value=0 },new C58 { Value=0 },new C59 { Value=0 },new C60 { Value=0 },new C61 { Value=0 },new C62 { Value=0 },new C63 { Value=InitialValue });
#elif ECS_COMPONENT_COUNT_65
        return World.Create(new C0 { Value=0 },new C1 { Value=0 },new C2 { Value=0 },new C3 { Value=0 },new C4 { Value=0 },new C5 { Value=0 },new C6 { Value=0 },new C7 { Value=0 },new C8 { Value=0 },new C9 { Value=0 },new C10 { Value=0 },new C11 { Value=0 },new C12 { Value=0 },new C13 { Value=0 },new C14 { Value=0 },new C15 { Value=0 },new C16 { Value=0 },new C17 { Value=0 },new C18 { Value=0 },new C19 { Value=0 },new C20 { Value=0 },new C21 { Value=0 },new C22 { Value=0 },new C23 { Value=0 },new C24 { Value=0 },new C25 { Value=0 },new C26 { Value=0 },new C27 { Value=0 },new C28 { Value=0 },new C29 { Value=0 },new C30 { Value=0 },new C31 { Value=0 },new C32 { Value=0 },new C33 { Value=0 },new C34 { Value=0 },new C35 { Value=0 },new C36 { Value=0 },new C37 { Value=0 },new C38 { Value=0 },new C39 { Value=0 },new C40 { Value=0 },new C41 { Value=0 },new C42 { Value=0 },new C43 { Value=0 },new C44 { Value=0 },new C45 { Value=0 },new C46 { Value=0 },new C47 { Value=0 },new C48 { Value=0 },new C49 { Value=0 },new C50 { Value=0 },new C51 { Value=0 },new C52 { Value=0 },new C53 { Value=0 },new C54 { Value=0 },new C55 { Value=0 },new C56 { Value=0 },new C57 { Value=0 },new C58 { Value=0 },new C59 { Value=0 },new C60 { Value=0 },new C61 { Value=0 },new C62 { Value=0 },new C63 { Value=0 },new C64 { Value=InitialValue });
#elif ECS_COMPONENT_COUNT_128
        return World.Create(new C0 { Value=0 },new C1 { Value=0 },new C2 { Value=0 },new C3 { Value=0 },new C4 { Value=0 },new C5 { Value=0 },new C6 { Value=0 },new C7 { Value=0 },new C8 { Value=0 },new C9 { Value=0 },new C10 { Value=0 },new C11 { Value=0 },new C12 { Value=0 },new C13 { Value=0 },new C14 { Value=0 },new C15 { Value=0 },new C16 { Value=0 },new C17 { Value=0 },new C18 { Value=0 },new C19 { Value=0 },new C20 { Value=0 },new C21 { Value=0 },new C22 { Value=0 },new C23 { Value=0 },new C24 { Value=0 },new C25 { Value=0 },new C26 { Value=0 },new C27 { Value=0 },new C28 { Value=0 },new C29 { Value=0 },new C30 { Value=0 },new C31 { Value=0 },new C32 { Value=0 },new C33 { Value=0 },new C34 { Value=0 },new C35 { Value=0 },new C36 { Value=0 },new C37 { Value=0 },new C38 { Value=0 },new C39 { Value=0 },new C40 { Value=0 },new C41 { Value=0 },new C42 { Value=0 },new C43 { Value=0 },new C44 { Value=0 },new C45 { Value=0 },new C46 { Value=0 },new C47 { Value=0 },new C48 { Value=0 },new C49 { Value=0 },new C50 { Value=0 },new C51 { Value=0 },new C52 { Value=0 },new C53 { Value=0 },new C54 { Value=0 },new C55 { Value=0 },new C56 { Value=0 },new C57 { Value=0 },new C58 { Value=0 },new C59 { Value=0 },new C60 { Value=0 },new C61 { Value=0 },new C62 { Value=0 },new C63 { Value=0 },new C64 { Value=0 },new C65 { Value=0 },new C66 { Value=0 },new C67 { Value=0 },new C68 { Value=0 },new C69 { Value=0 },new C70 { Value=0 },new C71 { Value=0 },new C72 { Value=0 },new C73 { Value=0 },new C74 { Value=0 },new C75 { Value=0 },new C76 { Value=0 },new C77 { Value=0 },new C78 { Value=0 },new C79 { Value=0 },new C80 { Value=0 },new C81 { Value=0 },new C82 { Value=0 },new C83 { Value=0 },new C84 { Value=0 },new C85 { Value=0 },new C86 { Value=0 },new C87 { Value=0 },new C88 { Value=0 },new C89 { Value=0 },new C90 { Value=0 },new C91 { Value=0 },new C92 { Value=0 },new C93 { Value=0 },new C94 { Value=0 },new C95 { Value=0 },new C96 { Value=0 },new C97 { Value=0 },new C98 { Value=0 },new C99 { Value=0 },new C100 { Value=0 },new C101 { Value=0 },new C102 { Value=0 },new C103 { Value=0 },new C104 { Value=0 },new C105 { Value=0 },new C106 { Value=0 },new C107 { Value=0 },new C108 { Value=0 },new C109 { Value=0 },new C110 { Value=0 },new C111 { Value=0 },new C112 { Value=0 },new C113 { Value=0 },new C114 { Value=0 },new C115 { Value=0 },new C116 { Value=0 },new C117 { Value=0 },new C118 { Value=0 },new C119 { Value=0 },new C120 { Value=0 },new C121 { Value=0 },new C122 { Value=0 },new C123 { Value=0 },new C124 { Value=0 },new C125 { Value=0 },new C126 { Value=0 },new C127 { Value=InitialValue });
#endif
    }

    public static int ReadHot(World World,Entity Entity){
#if ECS_COMPONENT_COUNT_1
        return World.TryGetComponent(Entity,out C0 Value)?Value.Value:int.MinValue;
#elif ECS_COMPONENT_COUNT_64
        return World.TryGetComponent(Entity,out C63 Value)?Value.Value:int.MinValue;
#elif ECS_COMPONENT_COUNT_65
        return World.TryGetComponent(Entity,out C64 Value)?Value.Value:int.MinValue;
#elif ECS_COMPONENT_COUNT_128
        return World.TryGetComponent(Entity,out C127 Value)?Value.Value:int.MinValue;
#endif
    }
}

