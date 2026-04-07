using AspNetCore.Bundling.ESBuild.Tasks;

namespace AspNetCore.Bundling.ESBuild.Tasks.Tests;

public sealed class ConfigLoadingTests
{
    [Fact]
    public void Load_UsesDefaultsAndConfigurationOverrides()
    {
        var path = CreateConfigFile(
            """
            {
              "Defaults": {
                "Minify": false,
                "Sourcemap": true,
                "Target": "es2020",
                "Format": "iife",
                "Platform": "browser"
              },
              "Bundles": [
                {
                  "EntryPoint": "Scripts/site.ts",
                  "Output": "wwwroot/js/site.js",
                  "Optional": true,
                  "Minify": false,
                  "Sourcemap": true,
                  "Target": "es2018",
                  "Format": "esm",
                  "Platform": "node",
                  "External": ["jquery"],
                  "Define": {
                    "process.env.MODE": "\"debug\""
                  },
                  "Alias": {
                    "@shared": "./Scripts/shared"
                  },
                  "Loader": {
                    ".svg": "text"
                  },
                  "PublicPath": "/assets"
                }
              ],
              "Configurations": {
                "Release": {
                  "Defaults": {
                    "Minify": true,
                    "Sourcemap": false
                  }
                }
              }
            }
            """);

        try
        {
            var bundles = EsbuildConfigLoader.Load(path, "Release");

            var bundle = Assert.Single(bundles);
            Assert.Equal("Scripts/site.ts", bundle.EntryPoint);
            Assert.Equal("wwwroot/js/site.js", bundle.Output);
            Assert.True(bundle.Optional);
            Assert.False(bundle.Minify);
            Assert.True(bundle.Sourcemap);
            Assert.Equal("es2018", bundle.Target);
            Assert.Equal("esm", bundle.Format);
            Assert.Equal("node", bundle.Platform);
            Assert.Equal(["jquery"], bundle.External);
            Assert.Equal("\"debug\"", bundle.Define["process.env.MODE"]);
            Assert.Equal("./Scripts/shared", bundle.Alias["@shared"]);
            Assert.Equal("text", bundle.Loader[".svg"]);
            Assert.Equal("/assets", bundle.PublicPath);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_NormalizesPathSeparators()
    {
        var path = CreateConfigFile(
            """
            {
              "Bundles": [
                {
                  "EntryPoint": "Scripts\\site.ts",
                  "Output": "wwwroot\\js\\site.js"
                }
              ]
            }
            """);

        try
        {
            var bundle = Assert.Single(EsbuildConfigLoader.Load(path, "Debug"));
            Assert.Equal("Scripts/site.ts", bundle.EntryPoint);
            Assert.Equal("wwwroot/js/site.js", bundle.Output);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_AppliesConfigurationSpecificBundleOverrides()
    {
        var path = CreateConfigFile(
            """
            {
              "Defaults": {
                "Minify": false,
                "Sourcemap": true,
                "Target": "es2020",
                "Format": "iife",
                "Platform": "browser"
              },
              "Bundles": [
                {
                  "EntryPoint": "Scripts/site.ts",
                  "Output": "wwwroot/js/site.js",
                  "Minify": false,
                  "Sourcemap": true,
                  "Define": {
                    "process.env.MODE": "\"debug\""
                  },
                  "PublicPath": "/debug"
                }
              ],
              "Configurations": {
                "Release": {
                  "Defaults": {
                    "Minify": true,
                    "Sourcemap": false
                  },
                  "Bundles": [
                    {
                      "EntryPoint": "Scripts/site.ts",
                      "Output": "wwwroot/js/site.release.js",
                      "Minify": true,
                      "Sourcemap": false,
                      "Define": {
                        "process.env.MODE": "\"release\""
                      },
                      "PublicPath": "/release"
                    }
                  ]
                }
              }
            }
            """);

        try
        {
            var bundle = Assert.Single(EsbuildConfigLoader.Load(path, "Release"));
            Assert.Equal("wwwroot/js/site.release.js", bundle.Output);
            Assert.True(bundle.Minify);
            Assert.False(bundle.Sourcemap);
            Assert.Equal("\"release\"", bundle.Define["process.env.MODE"]);
            Assert.Equal("/release", bundle.PublicPath);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_AllowsCommentsAndTrailingCommas()
    {
        var path = CreateConfigFile(
            """
            {
              // Root defaults should tolerate comments.
              "Defaults": {
                "Minify": true,
              },
              "Bundles": [
                {
                  "EntryPoint": "Scripts/site.ts",
                  "Output": "wwwroot/js/site.js",
                },
              ],
            }
            """);

        try
        {
            var bundle = Assert.Single(EsbuildConfigLoader.Load(path, "Debug"));
            Assert.True(bundle.Minify);
            Assert.True(bundle.Sourcemap);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_ThrowsWhenBundlesAreMissing()
    {
        var path = CreateConfigFile(
            """
            {
              "Defaults": {
                "Format": "iife"
              }
            }
            """);

        try
        {
            var exception = Assert.Throws<EsbuildConfigException>(() => EsbuildConfigLoader.Load(path, "Debug"));
            Assert.Equal("esbuild.json must contain at least one bundle in 'Bundles'.", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_ThrowsWhenConfigurationBundleOverrideEntryPointIsMissing()
    {
        var path = CreateConfigFile(
            """
            {
              "Bundles": [
                {
                  "EntryPoint": "Scripts/site.ts",
                  "Output": "wwwroot/js/site.js"
                }
              ],
              "Configurations": {
                "Release": {
                  "Bundles": [
                    {
                      "Output": "wwwroot/js/site.release.js"
                    }
                  ]
                }
              }
            }
            """);

        try
        {
            var exception = Assert.Throws<EsbuildConfigException>(() => EsbuildConfigLoader.Load(path, "Release"));
            Assert.Equal("Every bundle override in configuration 'Release' must define 'EntryPoint'.", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_ThrowsWhenConfigurationBundleOverrideTargetsUnknownEntryPoint()
    {
        var path = CreateConfigFile(
            """
            {
              "Bundles": [
                {
                  "EntryPoint": "Scripts/site.ts",
                  "Output": "wwwroot/js/site.js"
                }
              ],
              "Configurations": {
                "Release": {
                  "Bundles": [
                    {
                      "EntryPoint": "Scripts/other.ts",
                      "Output": "wwwroot/js/other.js"
                    }
                  ]
                }
              }
            }
            """);

        try
        {
            var exception = Assert.Throws<EsbuildConfigException>(() => EsbuildConfigLoader.Load(path, "Release"));
            Assert.Equal("Configuration 'Release' defines a bundle override for unknown entry point 'Scripts/other.ts'.", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_ThrowsWhenConfigurationBundleOverrideIsDuplicated()
    {
        var path = CreateConfigFile(
            """
            {
              "Bundles": [
                {
                  "EntryPoint": "Scripts/site.ts",
                  "Output": "wwwroot/js/site.js"
                }
              ],
              "Configurations": {
                "Release": {
                  "Bundles": [
                    {
                      "EntryPoint": "Scripts/site.ts",
                      "Output": "wwwroot/js/site.release.js"
                    },
                    {
                      "EntryPoint": "Scripts/site.ts",
                      "Output": "wwwroot/js/site.release.2.js"
                    }
                  ]
                }
              }
            }
            """);

        try
        {
            var exception = Assert.Throws<EsbuildConfigException>(() => EsbuildConfigLoader.Load(path, "Release"));
            Assert.Equal("Configuration 'Release' defines multiple bundle overrides for 'Scripts/site.ts'.", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_ThrowsWhenEntryPointIsMissing()
    {
        var path = CreateConfigFile(
            """
            {
              "Bundles": [
                {
                  "Output": "wwwroot/js/site.js"
                }
              ]
            }
            """);

        try
        {
            var exception = Assert.Throws<EsbuildConfigException>(() => EsbuildConfigLoader.Load(path, "Debug"));
            Assert.Equal("Every bundle in esbuild.json must define 'EntryPoint'.", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_ThrowsWhenOutputIsMissing()
    {
        var path = CreateConfigFile(
            """
            {
              "Bundles": [
                {
                  "EntryPoint": "Scripts/site.ts"
                }
              ]
            }
            """);

        try
        {
            var exception = Assert.Throws<EsbuildConfigException>(() => EsbuildConfigLoader.Load(path, "Debug"));
            Assert.Equal("Bundle 'Scripts/site.ts' must define either 'Output' or 'Outdir'.", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_AllowsOutdirAndSplitting()
    {
        var path = CreateConfigFile(
            """
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
            """);

        try
        {
            var bundle = Assert.Single(EsbuildConfigLoader.Load(path, "Debug"));
            Assert.Null(bundle.Output);
            Assert.Equal("wwwroot/js", bundle.Outdir);
            Assert.True(bundle.Splitting);
            Assert.Equal("esm", bundle.Format);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_ThrowsWhenOutputAndOutdirAreBothSpecified()
    {
        var path = CreateConfigFile(
            """
            {
              "Bundles": [
                {
                  "EntryPoint": "Scripts/site.ts",
                  "Output": "wwwroot/js/site.js",
                  "Outdir": "wwwroot/js"
                }
              ]
            }
            """);

        try
        {
            var exception = Assert.Throws<EsbuildConfigException>(() => EsbuildConfigLoader.Load(path, "Debug"));
            Assert.Equal("Bundle 'Scripts/site.ts' cannot define both 'Output' and 'Outdir'.", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_ThrowsWhenSplittingUsesOutput()
    {
        var path = CreateConfigFile(
            """
            {
              "Bundles": [
                {
                  "EntryPoint": "Scripts/site.ts",
                  "Output": "wwwroot/js/site.js",
                  "Splitting": true,
                  "Format": "esm"
                }
              ]
            }
            """);

        try
        {
            var exception = Assert.Throws<EsbuildConfigException>(() => EsbuildConfigLoader.Load(path, "Debug"));
            Assert.Equal("Bundle 'Scripts/site.ts' cannot enable 'Splitting' when using 'Output'. Use 'Outdir' instead.", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_ThrowsWhenSplittingFormatIsNotEsm()
    {
        var path = CreateConfigFile(
            """
            {
              "Bundles": [
                {
                  "EntryPoint": "Scripts/site.ts",
                  "Outdir": "wwwroot/js",
                  "Splitting": true,
                  "Format": "iife"
                }
              ]
            }
            """);

        try
        {
            var exception = Assert.Throws<EsbuildConfigException>(() => EsbuildConfigLoader.Load(path, "Debug"));
            Assert.Equal("Bundle 'Scripts/site.ts' cannot enable 'Splitting' unless 'Format' is 'esm'.", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_ThrowsForUnsupportedBundleFormat()
    {
        var path = CreateConfigFile(
            """
            {
              "Bundles": [
                {
                  "EntryPoint": "Scripts/site.ts",
                  "Output": "wwwroot/js/site.js",
                  "Format": "invalid"
                }
              ]
            }
            """);

        try
        {
            var exception = Assert.Throws<EsbuildConfigException>(() => EsbuildConfigLoader.Load(path, "Debug"));
            Assert.Equal("Bundle 'Scripts/site.ts' uses unsupported esbuild format 'invalid'. Supported values: iife, cjs, esm.", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_ThrowsForUnsupportedBundlePlatform()
    {
        var path = CreateConfigFile(
            """
            {
              "Bundles": [
                {
                  "EntryPoint": "Scripts/site.ts",
                  "Output": "wwwroot/js/site.js",
                  "Platform": "weird"
                }
              ]
            }
            """);

        try
        {
            var exception = Assert.Throws<EsbuildConfigException>(() => EsbuildConfigLoader.Load(path, "Debug"));
            Assert.Equal("Bundle 'Scripts/site.ts' uses unsupported esbuild platform 'weird'. Supported values: browser, node, neutral.", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_ThrowsForEmptyAliasValue()
    {
        var path = CreateConfigFile(
            """
            {
              "Bundles": [
                {
                  "EntryPoint": "Scripts/site.ts",
                  "Output": "wwwroot/js/site.js",
                  "Alias": {
                    "@shared": "   "
                  }
                }
              ]
            }
            """);

        try
        {
            var exception = Assert.Throws<EsbuildConfigException>(() => EsbuildConfigLoader.Load(path, "Debug"));
            Assert.Equal("Bundle 'Scripts/site.ts' contains an empty 'Alias' value for '@shared'.", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_UsesDefaultsWhenBundleOverridesAreAbsent()
    {
        var path = CreateConfigFile(
            """
            {
              "Defaults": {
                "Minify": false,
                "Sourcemap": true,
                "Target": "es2020",
                "Format": "iife",
                "Platform": "browser"
              },
              "Bundles": [
                {
                  "EntryPoint": "Scripts/site.ts",
                  "Output": "wwwroot/js/site.js"
                }
              ],
              "Configurations": {
                "Release": {
                  "Defaults": {
                    "Minify": true,
                    "Sourcemap": false
                  }
                }
              }
            }
            """);

        try
        {
            var bundle = Assert.Single(EsbuildConfigLoader.Load(path, "Release"));
            Assert.True(bundle.Minify);
            Assert.False(bundle.Sourcemap);
            Assert.Equal("es2020", bundle.Target);
            Assert.Equal("iife", bundle.Format);
            Assert.Equal("browser", bundle.Platform);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateConfigFile(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }
}
