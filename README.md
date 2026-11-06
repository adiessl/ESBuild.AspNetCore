# ESBuild.AspNetCore

[![CI](https://github.com/adiessl/ESBuild.AspNetCore/actions/workflows/ci.yml/badge.svg)](https://github.com/adiessl/ESBuild.AspNetCore/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/ESBuild.AspNetCore.svg)](https://www.nuget.org/packages/ESBuild.AspNetCore)

`ESBuild.AspNetCore` is a build-time TypeScript bundling package for ASP.NET Core projects powered by [esbuild](https://esbuild.github.io/) ([GitHub](https://github.com/evanw/esbuild)). It was inspired by [`AspNetCore.SassCompiler`](https://github.com/koenvzeijl/AspNetCore.SassCompiler), with the same goal of making asset compilation work as a NuGet-powered MSBuild integration instead of a separate Node-based toolchain step.

This repository is currently 100% AI-generated code. Because of that, no pull request will be accepted without prior approval from the maintainer. If you want to contribute, open an issue first and wait for confirmation before investing time in a PR.

## What it does

- Reads configuration exclusively from `esbuild.json`
- Runs `esbuild` during `dotnet build` and `dotnet publish`
- Ships platform-specific `esbuild` binaries inside the NuGet package
- Adds generated files back into the ASP.NET Core static file/publish pipeline
- Supports Debug/Release defaults and configuration-specific per-bundle overrides

## What it does not do

- It does not read `appsettings.json`
- It does not require changes to `Program.cs`
- It does not provide watch mode, hot reload integration, or runtime bundling
- It does not require Node.js or a global `esbuild` installation

## Installation

Add the package to an ASP.NET Core project:

```bash
dotnet add package ESBuild.AspNetCore
```

Then create an `esbuild.json` file in the project root.

## Minimal example

`esbuild.json`

```json
{
  "Bundles": [
    {
      "EntryPoint": "Scripts/site.ts",
      "Output": "wwwroot/js/site.js"
    }
  ],
  "Configurations": {
    "Debug": {
      "Defaults": {
        "Minify": false,
        "Sourcemap": true
      }
    },
    "Release": {
      "Defaults": {
        "Minify": true,
        "Sourcemap": false
      }
    }
  }
}
```

After that, `dotnet build` will generate `wwwroot/js/site.js`.

## Configuration model

Top-level shape:

```json
{
  "Defaults": {},
  "Bundles": [],
  "Configurations": {}
}
```

### `Defaults`

Supported default fields:

- `Bundle`
- `Minify`
- `Sourcemap`
- `Target`
- `Format`
- `Platform`

Built-in package defaults:

- `Bundle: true`
- `Minify: false`
- `Sourcemap: true`
- `Target: "es2020"`
- `Format: "iife"`
- `Platform: "browser"`

### `Bundles`

Each bundle supports:

- `EntryPoint`
- `Output`
- `Outdir`
- `Optional`
- `Splitting`
- `Bundle`
- `Minify`
- `Sourcemap`
- `Target`
- `Format`
- `Platform`
- `External`
- `Define`
- `Alias`
- `Loader`
- `PublicPath`

Rules:

- Each bundle must define `EntryPoint`
- Each bundle must define exactly one of `Output` or `Outdir`
- `Splitting` requires `Outdir`
- `Splitting` requires `Format: "esm"`
- `Bundle: false` rejects nonempty `External` and `Alias`, `Splitting: true`, and `Format` set directly on the bundle (including configuration-specific bundle overrides)
- With `Bundle: false`, `Format` inherited from `Defaults` is ignored
- `Optional: true` skips the bundle if the entry point file does not exist

### `Configurations`

Each configuration can contain:

- `Defaults`
- `Bundles`

`Configurations.<name>.Defaults` overrides root defaults for that build configuration.

`Configurations.<name>.Bundles` applies per-bundle overrides matched by `EntryPoint`. Override entries must define `EntryPoint`, and that `EntryPoint` must match exactly one root bundle.

## Example: minifying classic global scripts

Set `Bundle: false` to process each entry without bundling modules. This is useful for classic browser scripts, such as JavaScript emitted by `tsc --outFile` from TypeScript namespaces, loaded with plain `<script>` tags. Their namespace variables and top-level functions can then keep their original global names, including functions called only through Blazor JS interop, other scripts, or server-generated JavaScript.

```json
{
  "Defaults": {
    "Bundle": false
  },
  "Bundles": [
    {
      "EntryPoint": "Scripts/classic.js",
      "Output": "wwwroot/js/classic.min.js"
    }
  ],
  "Configurations": {
    "Debug": {
      "Defaults": {
        "Minify": false,
        "Sourcemap": true
      }
    },
    "Release": {
      "Defaults": {
        "Minify": true,
        "Sourcemap": false
      }
    }
  }
}
```

Without bundling, the package omits both `--bundle` and `--format`. Omitting the format is essential to preserve top-level names during minification; see [esbuild's minification documentation](https://esbuild.github.io/api/#minify). The input must itself be a classic script; disabling bundling does not turn module imports or exports into globals or combine dependencies into the output.

`Target`, `Platform`, `Minify`, `Sourcemap`, `Define`, `Loader`, and `PublicPath` still apply, as do `Output` (or `Outdir`) and the usual diagnostics. Generated files retain the same up-to-date checks, cleaning, and static web asset/publish integration.

`External`, `Alias`, and code splitting require bundling and are rejected when enabled with `Bundle: false`. A `Format` set directly on a non-bundled entry or its configuration-specific bundle override is also rejected. A `Format` inherited from root or configuration `Defaults`, including the built-in `iife`, is ignored, so shared defaults can serve bundled and non-bundled entries. Existing configurations keep bundling by default.

## Example: configuration-specific bundle override

This allows Debug and Release to output different files:

```json
{
  "Bundles": [
    {
      "EntryPoint": "Scripts/site.ts",
      "Output": "wwwroot/js/site.js"
    }
  ],
  "Configurations": {
    "Debug": {
      "Defaults": {
        "Minify": false,
        "Sourcemap": true
      }
    },
    "Release": {
      "Defaults": {
        "Minify": true,
        "Sourcemap": false
      },
      "Bundles": [
        {
          "EntryPoint": "Scripts/site.ts",
          "Output": "wwwroot/js/site.release.js"
        }
      ]
    }
  }
}
```

## Example: splitting with `Outdir`

```json
{
  "Bundles": [
    {
      "EntryPoint": "Scripts/site.ts",
      "Outdir": "wwwroot/js",
      "Splitting": true,
      "Format": "esm"
    }
  ]
}
```

When splitting is enabled, the package tracks emitted chunk files using esbuild metafile output and persists the discovered output set under `obj/ESBuild.AspNetCore`.

## Merge behavior

Effective bundle settings are resolved in this order:

1. Package defaults
2. Root `Defaults`
3. Configuration `Defaults`
4. Root bundle values
5. Configuration-specific bundle override values

This order also applies to `Bundle`. Explicit bundle values take precedence over configuration defaults.

If a configuration-specific bundle override specifies `Output`, it replaces `Outdir`. If it specifies `Outdir`, it replaces `Output`.

## Supported platforms

The package currently ships vendored `esbuild` binaries for:

- Windows x64
- Windows arm64
- Linux x64
- Linux arm64
- macOS x64
- macOS arm64

## Diagnostics

Typical failure cases:

- missing `EntryPoint`
- missing both `Output` and `Outdir`
- defining both `Output` and `Outdir`
- unsupported `Format`
- unsupported `Platform`
- `Splitting` used without `Outdir`
- `Splitting` used with a format other than `esm`
- `External`, `Alias`, `Splitting`, or an explicit bundle `Format` used with `Bundle: false`
- invalid configuration-specific bundle override matching

Builds fail with explicit error messages for invalid configuration.

esbuild warnings and errors are emitted as structured MSBuild diagnostics. Visual Studio and other MSBuild-based tools can therefore associate them with the originating TypeScript file and display their line, column, severity, and `ES####` diagnostic code in the IDE.

For example:

```text
Scripts/site.ts(1,21): warning ES0015: Duplicate key "a" in object literal
```

Warning codes are assigned by esbuild. Many warning types have a specific code, while generic errors can use `ES0000`.

### Warning policies

esbuild diagnostics originate from an MSBuild task instead of the C# compiler. Consequently, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` by itself does not promote esbuild warnings to errors.

To treat all MSBuild task warnings, including esbuild warnings, as errors:

```xml
<PropertyGroup>
  <MSBuildTreatWarningsAsErrors>true</MSBuildTreatWarningsAsErrors>
</PropertyGroup>
```

To treat selected warning codes as errors:

```xml
<PropertyGroup>
  <WarningsAsErrors>$(WarningsAsErrors);ES0015</WarningsAsErrors>
</PropertyGroup>
```

To suppress selected warning codes:

```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);ES0015</NoWarn>
</PropertyGroup>
```

The explicit MSBuild equivalents for the last two settings are `MSBuildWarningsAsErrors` and `MSBuildWarningsAsMessages`. The .NET SDK maps `WarningsAsErrors` and `NoWarn` to those MSBuild properties.

`.editorconfig` entries such as `dotnet_diagnostic.ES0015.severity = none` do not affect esbuild diagnostics because esbuild is an external build tool, not a Roslyn analyzer.

## Repository notes

- This project is intentionally build-time only
- Configuration comes only from `esbuild.json`
- `appsettings.json` configuration is ignored
- Generated files are included in normal ASP.NET Core content/publish output

## Contributing

Open an issue before opening a PR. Unapproved pull requests should be assumed likely to be closed without merge, especially while the repository is still stabilizing its AI-generated foundation.
