# Building from source

[Русский](BUILD.md) | [English](BUILD.en.md)

The patcher is written in C# / WPF for .NET Framework 4.7.2 and distributed under MIT. Application sources are in `src/TronEvolutionPatcher`; NUnit tests are in `tests/TronEvolutionPatcher.Tests`.

## Requirements

- Windows.
- A .NET SDK capable of building WPF projects and the .NET Framework 4.7.2 Developer Pack.
- .NET Framework 4.7.2 or a compatible runtime to run the application and tests.
- NuGet access for the initial test dependency restore.

You can also open the solution in Visual Studio with the .NET desktop development components. Python is not needed to build or run the C# patcher.

## Build and test

From the repository root:

```powershell
dotnet build .\TronEvolutionPatcher.sln -c Release
dotnet test .\TronEvolutionPatcher.sln -c Release --no-build --no-restore
```

Or run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Build.ps1 -Test
```

Output: `src/TronEvolutionPatcher/bin/Release/net472/TronEvolutionPatcher.exe`.

Tests use synthetic PE files, temporary directories and fake process starters. An installed game is not required. These tests do not replace checking the game workflow on a separate game copy.

## Assets and distribution

`src/TronEvolutionPatcher/Assets/vk09.dll` is included as a prebuilt third-party DXVK library. It is required for Vulkan switching; building DXVK itself is outside this solution. See [ASSETS.md](ASSETS.md) for provenance, SHA-256 and license information.

The icon, logo and complete license notices are embedded in the EXE. The minimal user package contains only:

```text
TronEvolutionPatcher.exe
Assets/vk09.dll
```

Preserve the relative directory layout. Build output, test results and local IDE settings are excluded from the source repository.

## Application layout

- `Views`, `ViewModels`, `Resources`: WPF interface, commands and styles.
- `Services`: game discovery, settings, renderer switching, profile migration and launch.
- `Patching`: PE validation, binary patches and file rollback.
- `Models`: settings and operation results.

Further implementation notes: [renderer](RENDERER_SWITCH.md), [INI](SETTINGS_WRITE.md), [portable profile](PORTABLE_PATCHES.md), [DXVK and launch](DXVK_AND_LAUNCH.md), [interface](WPF_INTERFACE.md).
