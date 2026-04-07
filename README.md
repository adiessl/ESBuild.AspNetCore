# AspNetCore.Bundling.ESBuild

`AspNetCore.Bundling.ESBuild` is a build-time TypeScript bundling package for ASP.NET Core projects. It was inspired by [`AspNetCore.SassCompiler`](https://github.com/koenvzeijl/AspNetCore.SassCompiler), with the same goal of making asset compilation work as a NuGet-powered MSBuild integration instead of a separate Node-based toolchain step.

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
dotnet add package AspNetCore.Bundling.ESBuild
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

- `Minify`
- `Sourcemap`
- `Target`
- `Format`
- `Platform`

Built-in package defaults:

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
- `Optional: true` skips the bundle if the entry point file does not exist

### `Configurations`

Each configuration can contain:

- `Defaults`
- `Bundles`

`Configurations.<name>.Defaults` overrides root defaults for that build configuration.

`Configurations.<name>.Bundles` applies per-bundle overrides matched by `EntryPoint`. Override entries must define `EntryPoint`, and that `EntryPoint` must match exactly one root bundle.

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

When splitting is enabled, the package tracks emitted chunk files using esbuild metafile output and persists the discovered output set under `obj/AspNetCore.Bundling.ESBuild`.

## Merge behavior

Effective bundle settings are resolved in this order:

1. Package defaults
2. Root `Defaults`
3. Root bundle values
4. Configuration `Defaults`
5. Configuration-specific bundle override values

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
- invalid configuration-specific bundle override matching

Builds fail with explicit error messages for invalid configuration.

## Repository notes

- This project is intentionally build-time only
- Configuration comes only from `esbuild.json`
- `appsettings.json` configuration is ignored
- Generated files are included in normal ASP.NET Core content/publish output

## Contributing

Open an issue before opening a PR. Unapproved pull requests should be assumed likely to be closed without merge, especially while the repository is still stabilizing its AI-generated foundation.
