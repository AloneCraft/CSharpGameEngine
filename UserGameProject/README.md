# UserGameProject

This folder simulates an end user's game project - the kind of project that will eventually
consume `CSharpGameEngine` through a Visual Studio extension.

As of ADR-0012, this is a real `UserGameProject.csproj` (net10.0, no project references yet -
engine integration is a future milestone; see `Docs/Decisions/ADR-0004-Gltf-Model-Viewer-Vertical-Slice.md`
for the original rationale for keeping it a plain folder before that). `CSharpGameEngine.VSExtension`
automatically opens this project when Visual Studio is launched with no solution/project already
open (ADR-0012) - it never interrupts an already-open solution (e.g. this engine's own
`CSharpGameEngine.slnx`).

## Layout
```
UserGameProject/
  UserGameProject.csproj
  Assets/
    Fab/        Place your own purchased/downloaded Fab (https://www.fab.com) assets here -
                also the default destination for the VSIX's Fab download watcher (ADR-0011).
```

## `.meta` files (ADR-0012)

`CSharpGameEngine.VSExtension` automatically generates a `<file>.meta` sidecar (containing a
stable GUID, Unity/UE-style) next to every `.glb`/`.gltf`/`.fbx` file under `Assets/`. These
`.meta` files are excluded from Solution Explorer (see `UserGameProject.csproj`) so only the
representative asset file is shown - the `.meta` exists on disk but is not meant to be edited
by hand.

## Important: Fab content is not engine content

Assets downloaded from Fab are commercial, licensed content owned by their respective creators.
**Do not commit or redistribute anything placed under `Assets/Fab/`** - it is for local use only.
If this repository is ever put under version control, `Assets/Fab/*` (other than its own README)
should be excluded (e.g. via `.gitignore`).
