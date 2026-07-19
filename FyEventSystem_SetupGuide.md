# Fy Event System — Project & Package Setup Guide

Step-by-step setup for the new **Fy Event System** package, from a freshly created Unity
project up to the point where the package compiles and correctly references **Fy Service Locator**
(`com.fy.services`). This guide stops **before** any event-service logic is written — the goal here
is only a clean, referenced, compiling scaffold.

---

## Naming used in this guide

| Thing | Value |
|-------|-------|
| Package id | `com.fy.eventsystem` |
| Display name | `Fy Event System` |
| Runtime assembly / namespace | `Fy.EventSystem` |
| Editor assembly | `Fy.EventSystem.Editor` |
| Tests assembly | `Fy.EventSystem.RuntimeTests` |
| Samples assembly | `Fy.EventSystem.Examples` |

> If you prefer a different name (e.g. `Fy.Events` / `com.fy.events`), do a find-and-replace on
> `Fy.EventSystem` and `com.fy.eventsystem` throughout this guide before starting.

---

## Assumptions

- You already created a new **Unity 6000.0** project. This guide calls its root `A:\UnityProjects\Events\`
  (adjust paths to your actual folder name).
- The **ServiceLocator** repo is checked out locally at `A:\UnityProjects\ServiceLocator\`
  (so `com.fy.services` lives at `A:\UnityProjects\ServiceLocator\Packages\com.fy.services\`).
- The **ScriptableSettings** repo is checked out locally at `A:\UnityProjects\ScriptableSettings\`
  (so `com.fy.scriptablesettings` lives at
  `A:\UnityProjects\ScriptableSettings\Packages\com.fy.scriptablesettings\`). It is a dependency
  because the event service exposes its runtime options through an
  `EventSettings : ScriptableSettings` asset, mirroring Coimbra's `EventSettings`.
- This new project will become its **own git repo**. Fy.Services and Fy.ScriptableSettings are
  **referenced**, never copied into this repo.

---

## Step 0 — Initialize the repo and Unity `.gitignore`

From the project root (`A:\UnityProjects\Events\`):

```bash
git init
```

Create `A:\UnityProjects\Events\.gitignore` (essential Unity ignores — Fy.Services is pulled in on
import, so it must **not** be committed here):

```gitignore
[Ll]ibrary/
[Tt]emp/
[Oo]bj/
[Bb]uild/
[Bb]uilds/
[Ll]ogs/
[Uu]serSettings/
.vs/
.idea/
*.csproj
*.sln
```

---

## Step 1 — Reference Fy.Services and Fy.ScriptableSettings as local packages

Open `A:\UnityProjects\Events\Packages\manifest.json` and add both `com.fy.*` lines to
`dependencies` (absolute `file:` paths shown; relative paths from the `Packages/` folder also work):

```json
{
  "dependencies": {
    "com.fy.scriptablesettings": "file:A:/UnityProjects/ScriptableSettings/Packages/com.fy.scriptablesettings",
    "com.fy.services": "file:A:/UnityProjects/ServiceLocator/Packages/com.fy.services",

    "com.unity.modules.unitywebrequest": "1.0.0"
  }
}
```

**Alternative (UI):** Package Manager → **+** → *Add package from disk…* → select each package's
`package.json`. This writes the same `file:` lines for you.

### Verify

1. Return focus to Unity; let it resolve packages.
2. Open **Window → Package Manager**. Under **In Project** (or **Local**) you should see both
   **Fy Service Locator** and **Fy Scriptable Settings**. No red errors in the Console.

If Package Manager shows a resolution error, a `file:` path is wrong — recheck it.

---

## Step 2 — Create the package folder skeleton

Create this structure under `A:\UnityProjects\Events\Packages\`:

```
com.fy.eventsystem\
├── package.json
├── CHANGELOG.md                      (optional now)
├── LICENSE.md                        (optional now)
├── README.md                         (optional now)
├── Fy.EventSystem\                   ← runtime assembly
│   ├── Fy.EventSystem.asmdef
│   ├── AssemblyInfo.cs
│   └── Core\                         ← implementation goes here later
├── Fy.EventSystem.Editor\            ← editor-only assembly
│   └── Fy.EventSystem.Editor.asmdef
└── Fy.EventSystem.RuntimeTests\      ← play/edit-mode tests
    └── Fy.EventSystem.RuntimeTests.asmdef
```

> Create the files with your editor (outside Unity is fine). Do **not** hand-create `.meta` files —
> Unity generates them the next time it has focus.

---

## Step 3 — `package.json`

`Packages\com.fy.eventsystem\package.json`:

```json
{
  "name": "com.fy.eventsystem",
  "displayName": "Fy Event System",
  "version": "0.1.0",
  "unity": "6000.0",
  "description": "Strongly-typed, allocation-light event service built on top of the Fy Service Locator.",
  "author": {
    "name": "vgArchives"
  },
  "license": "MIT",
  "dependencies": {
    "com.fy.services": "0.1.0",
    "com.fy.scriptablesettings": "0.1.0"
  },
  "samples": [
    {
      "displayName": "Event Examples",
      "description": "Examples covering event definitions, listeners, and invocation.",
      "path": "Samples~/Examples"
    }
  ]
}
```

> The `dependencies` entry declares the relationship for distribution. During local development the
> actual resolution comes from the `file:` line in Step 1 — both are expected to coexist.

---

## Step 4 — Runtime assembly (`Fy.EventSystem`)

`Packages\com.fy.eventsystem\Fy.EventSystem\Fy.EventSystem.asmdef`:

```json
{
    "name": "Fy.EventSystem",
    "rootNamespace": "Fy.EventSystem",
    "references": [
        "Fy.Services",
        "Fy.ScriptableSettings"
    ],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

> The `"references"` entries are the **assembly-level** dependencies — `Fy.Services` lets the compiler
> see `IService`; `Fy.ScriptableSettings` lets it see `ScriptableSettings`/`ScriptableSettingsRegistry`
> for the `EventSettings` asset. The packages being installed (Step 1) is **not** enough on its own;
> asmdef→asmdef references are never automatic.

`Packages\com.fy.eventsystem\Fy.EventSystem\AssemblyInfo.cs`:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Fy.EventSystem.RuntimeTests")]
[assembly: InternalsVisibleTo("Fy.EventSystem.Editor")]
```

> Same pattern as Fy.Services: the runtime exposes its `internal` members to its own tests and editor
> assemblies only. No cross-package `InternalsVisibleTo` is needed — Fy.EventSystem consumes only the
> **public** API of Fy.Services.

---

## Step 5 — Editor assembly (`Fy.EventSystem.Editor`)

`Packages\com.fy.eventsystem\Fy.EventSystem.Editor\Fy.EventSystem.Editor.asmdef`:

```json
{
    "name": "Fy.EventSystem.Editor",
    "rootNamespace": "Fy.EventSystem.Editor",
    "references": [
        "Fy.EventSystem",
        "Fy.Services"
    ],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

> `Fy.Services` is listed explicitly because asmdef references are **not transitive** — editor drawers
> that touch `IService`/locator types need their own direct reference even though they also reference
> `Fy.EventSystem`.

---

## Step 6 — Tests assembly (`Fy.EventSystem.RuntimeTests`)

`Packages\com.fy.eventsystem\Fy.EventSystem.RuntimeTests\Fy.EventSystem.RuntimeTests.asmdef`:

```json
{
    "name": "Fy.EventSystem.RuntimeTests",
    "rootNamespace": "Fy.EventSystem.RuntimeTests",
    "references": [
        "Fy.EventSystem",
        "Fy.Services",
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [
        "nunit.framework.dll"
    ],
    "autoReferenced": false,
    "defineConstraints": [
        "UNITY_INCLUDE_TESTS"
    ],
    "versionDefines": [],
    "noEngineReferences": false
}
```

> This mirrors `Fy.Services.RuntimeTests` exactly: NUnit + the Unity Test Runner references, gated by
> `UNITY_INCLUDE_TESTS` so it only compiles when tests are enabled.

---

## Step 7 — Prove the reference resolves (first real file)

Add a minimal seam file so Unity actually compiles a type that depends on Fy.Services. This is the one
real line of coupling we discussed — the event-service interface extends the locator's `IService`.

`Packages\com.fy.eventsystem\Fy.EventSystem\Core\IEventService.cs`:

```csharp
using Fy.Services;

namespace Fy.EventSystem
{
    /// <summary>
    /// Strongly-typed event service. Placeholder — the real API is added next.
    /// </summary>
    public interface IEventService : IService
    {
    }
}
```

If this compiles with no errors, the package is correctly wired: `using Fy.Services;` resolves and
`IService` is visible across the package boundary.

> Leave this file in place — it's the genuine starting point of the implementation, not throwaway.

---

## Step 8 — Let Unity compile & generate metas

1. Focus the Unity Editor. It will import the new package, compile all three assemblies, and generate
   `.meta` files.
2. Open the **Console** — it must be clean (no compile errors).
3. Open **Window → Package Manager → In Project** — **Fy Event System** should now appear alongside
   **Fy Service Locator**.

---

## Step 9 — Commit the scaffold

```bash
git add .
git commit -m "chore: scaffold Fy Event System package and reference Fy.Services"
```

What gets committed: the Unity project + `Packages/com.fy.eventsystem`. What does **not**:
Fy.Services' source (it lives in its own repo) and `Library/` (git-ignored).

---

## Done — verification checklist

- [ ] `manifest.json` has the `file:` references to `com.fy.services` and `com.fy.scriptablesettings`,
      and Package Manager shows both.
- [ ] `com.fy.eventsystem/package.json` exists with the `com.fy.services` and
      `com.fy.scriptablesettings` dependencies declared.
- [ ] Three asmdefs exist; the runtime one references `Fy.Services` and `Fy.ScriptableSettings`.
- [ ] `AssemblyInfo.cs` exposes internals to the Editor + RuntimeTests assemblies.
- [ ] `IEventService : IService` compiles — the cross-package reference is proven.
- [ ] Console is clean; both packages show under Package Manager → In Project.
- [ ] Initial scaffold committed to the new repo.

Once every box is checked, the package is ready for the implementation phase (the actual
`EventSystem`, event handles, listeners, and invocation).

---

## When it's time to release (later, not now)

Swap the `manifest.json` lines from the local `file:` paths to pinned git URLs so consumers don't
need your local checkouts:

```json
"com.fy.services": "https://github.com/vgArchives/ServiceLocator.git?path=/Packages/com.fy.services#v0.1.0",
"com.fy.scriptablesettings": "https://github.com/vgArchives/ScriptableSettings.git?path=/Packages/com.fy.scriptablesettings#v0.1.0"
```

The `?path=` is required because each package lives in a sub-folder of its repo; `#v0.1.0` pins the
version. (Adjust the ScriptableSettings repo URL if it differs.)
