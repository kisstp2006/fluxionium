# Fluxionium

Public repository for this C# game engine experiment: a small **Godot-style** runtime (Veldrid + SDL2), scene tree, `project.godot` import, Lua scripting, and a sample project under `samples/godot_proj`.

## Requirements

- [.NET SDK 10](https://dotnet.microsoft.com/) (see `global.json` for the pinned SDK line; `rollForward` allows newer patch/feature bands).

## Build

```powershell
dotnet build
```

## Run the Godot-style sample

```powershell
dotnet run --project src/SimplestEngine.Runtime/SimplestEngine.Runtime.csproj -- samples/godot_proj
```

With the sample loaded, use **A/D**, **arrow up/down**, and **Space** (jump) when the game window has focus.

## Layout

- `SimplestEngine.slnx` — solution
- `src/` — engine projects (runtime, platform input, rendering, resources, Lua bridge, etc.)
- `samples/` — demo content including `project.godot` and `main.tscn`
