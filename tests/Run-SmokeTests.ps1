param(
    [Parameter(Mandatory = $true)]
    [string]$PackageVersion,

    [Parameter(Mandatory = $true)]
    [string]$UpstreamVersion,

    [Parameter(Mandatory = $true)]
    [string]$PackageFeed
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$ResolvedPackageFeed = $null
$RestoreSources = $null

function Invoke-DotNet {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [Parameter(Mandatory = $true)]
        [string]$WorkingDirectory,

        [switch]$ExpectFailure
    )

    $originalNuGetPackages = $env:NUGET_PACKAGES
    $nugetPackages = Join-Path $WorkingDirectory '.nuget/packages'
    New-Item -ItemType Directory -Path $nugetPackages -Force | Out-Null
    $env:NUGET_PACKAGES = $nugetPackages

    Push-Location $WorkingDirectory
    try {
        $output = & dotnet @Arguments 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
        $env:NUGET_PACKAGES = $originalNuGetPackages
    }

    if (-not $ExpectFailure -and $exitCode -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $exitCode.`n$output"
    }

    if ($ExpectFailure -and $exitCode -eq 0) {
        throw "dotnet $($Arguments -join ' ') succeeded unexpectedly.`n$output"
    }

    return [string]::Join([Environment]::NewLine, $output)
}

function Assert-True {
    param(
        [Parameter(Mandatory = $true)]
        [bool]$Condition,

        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Get-LineBreakCount {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Content
    )

    return ([regex]::Matches($Content, '\r?\n')).Count
}

function New-TempCopy {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourceDirectory,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $root = Join-Path ([System.IO.Path]::GetTempPath()) ('AspNetCore.Bundling.ESBuild.Tests.' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $root | Out-Null

    $target = Join-Path $root $Name
    Copy-Item -Path $SourceDirectory -Destination $target -Recurse
    return $target
}

function Get-EsbuildRuntimePath {
    $repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
    $runtimesRoot = Join-Path $repoRoot 'src/AspNetCore.Bundling.ESBuild/runtimes'
    $architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()

    if ($IsWindows) {
        return Join-Path $runtimesRoot $(if ($architecture -eq 'arm64') { 'win-arm64/esbuild.exe' } else { 'win-x64/esbuild.exe' })
    }

    if ($IsMacOS) {
        return Join-Path $runtimesRoot $(if ($architecture -eq 'arm64') { 'osx-arm64/esbuild' } else { 'osx-x64/esbuild' })
    }

    return Join-Path $runtimesRoot $(if ($architecture -eq 'arm64') { 'linux-arm64/esbuild' } else { 'linux-x64/esbuild' })
}

function Test-RuntimeBinary {
    $runtimePath = [System.IO.Path]::GetFullPath((Get-EsbuildRuntimePath))
    Assert-True (Test-Path $runtimePath) "Runtime binary not found: $runtimePath"

    $version = & $runtimePath --version
    $exitCode = $LASTEXITCODE

    Assert-True ($exitCode -eq 0) "Running '$runtimePath --version' failed with exit code $exitCode."
    Assert-True ($version.Trim() -eq $UpstreamVersion) "Expected runtime version '$UpstreamVersion' but got '$version'."
}

function Test-BasicWebApp {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Configuration
    )

    $source = Join-Path $PSScriptRoot 'AspNetCore.Bundling.ESBuild.IntegrationTests/TestAssets/BasicWebApp'
    $workingDirectory = New-TempCopy -SourceDirectory $source -Name "BasicWebApp-$Configuration"
    $projectPath = Join-Path $workingDirectory 'BasicWebApp.csproj'

    Invoke-DotNet -Arguments @(
        'build',
        $projectPath,
        '-c', $Configuration,
        "-p:AspNetCoreBundlingESBuildPackageVersion=$PackageVersion",
        "-p:RestoreSources=$RestoreSources",
        '-p:RestoreIgnoreFailedSources=true'
    ) -WorkingDirectory $workingDirectory | Out-Null

    $scriptPath = Join-Path $workingDirectory 'wwwroot' 'js' 'site.js'
    $mapPath = Join-Path $workingDirectory 'wwwroot' 'js' 'site.js.map'
    $optionalPath = Join-Path $workingDirectory 'wwwroot' 'js' 'missing-optional.js'

    Assert-True (Test-Path $scriptPath) "Expected bundled script to exist: $scriptPath"
    Assert-True (-not (Test-Path $optionalPath)) "Optional bundle output should not exist: $optionalPath"

    $scriptContent = Get-Content $scriptPath -Raw
    Assert-True ($scriptContent.Contains('Hello from AspNetCore.Bundling.ESBuild')) "Bundled output does not contain the expected content."
    $lineBreakCount = Get-LineBreakCount -Content $scriptContent

    if ($Configuration -eq 'Debug') {
        Assert-True (Test-Path $mapPath) "Expected sourcemap for Debug build: $mapPath"
        Assert-True ($lineBreakCount -ge 1) 'Expected Debug output to contain line breaks.'
    }
    else {
        Assert-True (-not (Test-Path $mapPath)) "Did not expect sourcemap for Release build: $mapPath"
        Assert-True ($lineBreakCount -le 1) "Expected Release output to be minified to a single line or near-single line, but found $lineBreakCount line breaks."
    }
}

function Test-BasicWebAppFromDifferentWorkingDirectory {
    $source = Join-Path $PSScriptRoot 'AspNetCore.Bundling.ESBuild.IntegrationTests/TestAssets/BasicWebApp'
    $workingDirectory = New-TempCopy -SourceDirectory $source -Name 'BasicWebApp-ExternalWorkingDirectory'
    $projectPath = Join-Path $workingDirectory 'BasicWebApp.csproj'
    $invocationDirectory = Split-Path -Parent $workingDirectory

    Invoke-DotNet -Arguments @(
        'build',
        $projectPath,
        '-c', 'Debug',
        "-p:AspNetCoreBundlingESBuildPackageVersion=$PackageVersion",
        "-p:RestoreSources=$RestoreSources",
        '-p:RestoreIgnoreFailedSources=true'
    ) -WorkingDirectory $invocationDirectory | Out-Null

    $scriptPath = Join-Path $workingDirectory 'wwwroot' 'js' 'site.js'
    $mapPath = Join-Path $workingDirectory 'wwwroot' 'js' 'site.js.map'

    Assert-True (Test-Path $scriptPath) "Expected bundled script to exist when building outside the project directory: $scriptPath"
    Assert-True (Test-Path $mapPath) "Expected sourcemap to exist when building outside the project directory: $mapPath"
}

function Test-BasicWebAppClean {
    $source = Join-Path $PSScriptRoot 'AspNetCore.Bundling.ESBuild.IntegrationTests/TestAssets/BasicWebApp'
    $workingDirectory = New-TempCopy -SourceDirectory $source -Name 'BasicWebApp-Clean'
    $projectPath = Join-Path $workingDirectory 'BasicWebApp.csproj'
    $scriptPath = Join-Path $workingDirectory 'wwwroot' 'js' 'site.js'
    $mapPath = Join-Path $workingDirectory 'wwwroot' 'js' 'site.js.map'

    Invoke-DotNet -Arguments @(
        'build',
        $projectPath,
        '-c', 'Debug',
        "-p:AspNetCoreBundlingESBuildPackageVersion=$PackageVersion",
        "-p:RestoreSources=$RestoreSources",
        '-p:RestoreIgnoreFailedSources=true'
    ) -WorkingDirectory $workingDirectory | Out-Null

    Assert-True (Test-Path $scriptPath) "Expected bundled script to exist before clean: $scriptPath"
    Assert-True (Test-Path $mapPath) "Expected sourcemap to exist before clean: $mapPath"

    Invoke-DotNet -Arguments @(
        'clean',
        $projectPath,
        '-c', 'Debug',
        "-p:AspNetCoreBundlingESBuildPackageVersion=$PackageVersion",
        "-p:RestoreSources=$RestoreSources",
        '-p:RestoreIgnoreFailedSources=true'
    ) -WorkingDirectory $workingDirectory | Out-Null

    Assert-True (-not (Test-Path $scriptPath)) "Expected bundled script to be removed by dotnet clean: $scriptPath"
    Assert-True (-not (Test-Path $mapPath)) "Expected sourcemap to be removed by dotnet clean: $mapPath"
}

function Test-BasicWebAppPublish {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Configuration
    )

    $source = Join-Path $PSScriptRoot 'AspNetCore.Bundling.ESBuild.IntegrationTests/TestAssets/BasicWebApp'
    $workingDirectory = New-TempCopy -SourceDirectory $source -Name "BasicWebAppPublish-$Configuration"
    $projectPath = Join-Path $workingDirectory 'BasicWebApp.csproj'
    $publishDirectory = Join-Path $workingDirectory 'publish'

    Invoke-DotNet -Arguments @(
        'publish',
        $projectPath,
        '-c', $Configuration,
        '-o', $publishDirectory,
        "-p:AspNetCoreBundlingESBuildPackageVersion=$PackageVersion",
        "-p:RestoreSources=$RestoreSources",
        '-p:RestoreIgnoreFailedSources=true'
    ) -WorkingDirectory $workingDirectory | Out-Null

    $publishedScriptPath = Join-Path $publishDirectory 'wwwroot/js/site.js'
    $publishedMapPath = Join-Path $publishDirectory 'wwwroot/js/site.js.map'

    Assert-True (Test-Path $publishedScriptPath) "Expected published bundled script to exist: $publishedScriptPath"

    if ($Configuration -eq 'Debug') {
        Assert-True (Test-Path $publishedMapPath) "Expected published sourcemap for Debug build: $publishedMapPath"
    }
    else {
        Assert-True (-not (Test-Path $publishedMapPath)) "Did not expect published sourcemap for Release build: $publishedMapPath"
    }
}

function Test-MultiTargetWebApp {
    $source = Join-Path $PSScriptRoot 'AspNetCore.Bundling.ESBuild.IntegrationTests/TestAssets/MultiTargetWebApp'
    $workingDirectory = New-TempCopy -SourceDirectory $source -Name 'MultiTargetWebApp'
    $projectPath = Join-Path $workingDirectory 'MultiTargetWebApp.csproj'

    $output = Invoke-DotNet -Arguments @(
        'build',
        $projectPath,
        '-c', 'Debug',
        '-v:n',
        "-p:AspNetCoreBundlingESBuildPackageVersion=$PackageVersion",
        "-p:RestoreSources=$RestoreSources",
        '-p:RestoreIgnoreFailedSources=true'
    ) -WorkingDirectory $workingDirectory

    $scriptPath = Join-Path $workingDirectory 'wwwroot' 'js' 'site.js'
    $net8OutputPath = Join-Path $workingDirectory 'bin/Debug/net8.0/MultiTargetWebApp.dll'
    $net6OutputPath = Join-Path $workingDirectory 'bin/Debug/net6.0/MultiTargetWebApp.dll'
    $bundleInvocationCount = ([regex]::Matches($output, [regex]::Escape('Bundling TypeScript:'))).Count

    Assert-True (Test-Path $scriptPath) "Expected bundled script to exist for multitarget build: $scriptPath"
    Assert-True (Test-Path $net8OutputPath) "Expected net8.0 build output to exist: $net8OutputPath"
    Assert-True (Test-Path $net6OutputPath) "Expected net6.0 build output to exist: $net6OutputPath"
    Assert-True ($bundleInvocationCount -eq 1) "Expected multi-target build to invoke esbuild once, but found $bundleInvocationCount invocations.`n$output"
}

function Test-SplittingWebApp {
    $source = Join-Path $PSScriptRoot 'AspNetCore.Bundling.ESBuild.IntegrationTests/TestAssets/SplittingWebApp'
    $workingDirectory = New-TempCopy -SourceDirectory $source -Name 'SplittingWebApp'
    $projectPath = Join-Path $workingDirectory 'SplittingWebApp.csproj'

    Invoke-DotNet -Arguments @(
        'build',
        $projectPath,
        '-c', 'Debug',
        "-p:AspNetCoreBundlingESBuildPackageVersion=$PackageVersion",
        "-p:RestoreSources=$RestoreSources",
        '-p:RestoreIgnoreFailedSources=true'
    ) -WorkingDirectory $workingDirectory | Out-Null

    $outputDirectory = Join-Path $workingDirectory 'wwwroot' 'js'
    $sitePath = Join-Path $outputDirectory 'site.js'
    $siteMapPath = Join-Path $outputDirectory 'site.js.map'
    $jsFiles = @(Get-ChildItem -Path $outputDirectory -Filter '*.js' -File)
    $chunkFiles = @($jsFiles | Where-Object { $_.Name -ne 'site.js' })
    $manifestDirectory = Join-Path $workingDirectory 'obj' 'AspNetCore.Bundling.ESBuild'
    $manifestFile = @(Get-ChildItem -Path $manifestDirectory -Filter '*.outputs.json' -File | Select-Object -First 1)

    Assert-True (Test-Path $sitePath) "Expected split bundle entry output to exist: $sitePath"
    Assert-True (Test-Path $siteMapPath) "Expected split bundle sourcemap to exist: $siteMapPath"
    Assert-True ($chunkFiles.Count -ge 1) "Expected at least one split chunk output in: $outputDirectory"
    Assert-True ($manifestFile.Count -eq 1) "Expected exactly one stored output manifest in: $manifestDirectory"

    $manifestContent = Get-Content $manifestFile[0].FullName -Raw
    Assert-True ($manifestContent.Contains('site.js')) 'Expected output manifest to include the entry output.'

    foreach ($chunkFile in $chunkFiles) {
        Assert-True ($manifestContent.Contains($chunkFile.Name)) "Expected output manifest to include split chunk '$($chunkFile.Name)'."
    }
}

function Test-ConfigurationOverrideWebApp {
    $source = Join-Path $PSScriptRoot 'AspNetCore.Bundling.ESBuild.IntegrationTests/TestAssets/ConfigurationOverrideWebApp'
    $workingDirectory = New-TempCopy -SourceDirectory $source -Name 'ConfigurationOverrideWebApp'
    $projectPath = Join-Path $workingDirectory 'ConfigurationOverrideWebApp.csproj'

    Invoke-DotNet -Arguments @(
        'build',
        $projectPath,
        '-c', 'Release',
        "-p:AspNetCoreBundlingESBuildPackageVersion=$PackageVersion",
        "-p:RestoreSources=$RestoreSources",
        '-p:RestoreIgnoreFailedSources=true'
    ) -WorkingDirectory $workingDirectory | Out-Null

    $releasePath = Join-Path $workingDirectory 'wwwroot' 'js' 'site.release.js'
    $defaultPath = Join-Path $workingDirectory 'wwwroot' 'js' 'site.js'
    $releaseMapPath = Join-Path $workingDirectory 'wwwroot' 'js' 'site.release.js.map'

    Assert-True (Test-Path $releasePath) "Expected release override output to exist: $releasePath"
    Assert-True (-not (Test-Path $defaultPath)) "Did not expect the default bundle output to exist for Release: $defaultPath"
    Assert-True (-not (Test-Path $releaseMapPath)) "Did not expect a release sourcemap for the override output: $releaseMapPath"
}

function Test-InvalidConfigWebApp {
    $source = Join-Path $PSScriptRoot 'AspNetCore.Bundling.ESBuild.IntegrationTests/TestAssets/InvalidConfigWebApp'
    $workingDirectory = New-TempCopy -SourceDirectory $source -Name 'InvalidConfigWebApp'
    $projectPath = Join-Path $workingDirectory 'InvalidConfigWebApp.csproj'

    $output = Invoke-DotNet -Arguments @(
        'build',
        $projectPath,
        '-c', 'Debug',
        "-p:AspNetCoreBundlingESBuildPackageVersion=$PackageVersion",
        "-p:RestoreSources=$RestoreSources",
        '-p:RestoreIgnoreFailedSources=true'
    ) -WorkingDirectory $workingDirectory -ExpectFailure

    Assert-True ($output.Contains("Bundle 'Scripts/site.ts' uses unsupported esbuild format 'not-a-real-format'")) 'Expected invalid config build output to mention the unsupported format.'
}

function Test-BasicRcl {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Configuration
    )

    $source = Join-Path $PSScriptRoot 'AspNetCore.Bundling.ESBuild.IntegrationTests/TestAssets/BasicRcl'
    $workingDirectory = New-TempCopy -SourceDirectory $source -Name "BasicRcl-$Configuration"
    $projectPath = Join-Path $workingDirectory 'BasicRcl.csproj'

    Invoke-DotNet -Arguments @(
        'build',
        $projectPath,
        '-c', $Configuration,
        "-p:AspNetCoreBundlingESBuildPackageVersion=$PackageVersion",
        "-p:RestoreSources=$RestoreSources",
        '-p:RestoreIgnoreFailedSources=true'
    ) -WorkingDirectory $workingDirectory | Out-Null

    $scriptPath = Join-Path $workingDirectory 'wwwroot' 'js' 'library.js'
    Assert-True (Test-Path $scriptPath) "Expected RCL bundled script to exist: $scriptPath"

    $scriptContent = Get-Content $scriptPath -Raw
    Assert-True ($scriptContent.Contains('BasicRcl says hello')) 'Expected RCL bundled output to contain the expected content.'

    $buildManifestPath = Join-Path $workingDirectory "obj/$Configuration/net8.0/staticwebassets.build.json"
    $developmentManifestPath = Join-Path $workingDirectory "obj/$Configuration/net8.0/staticwebassets.development.json"

    Assert-True (Test-Path $buildManifestPath) "Expected static web assets build manifest: $buildManifestPath"
    Assert-True (Test-Path $developmentManifestPath) "Expected static web assets development manifest: $developmentManifestPath"

    $buildManifest = Get-Content $buildManifestPath -Raw
    $developmentManifest = Get-Content $developmentManifestPath -Raw
    $normalizedWwwroot = ($workingDirectory.Replace('\', '/') + '/wwwroot/')

    Assert-True ($buildManifest.Contains($normalizedWwwroot)) 'Expected the static web assets build manifest to point at the RCL wwwroot content root.'
    Assert-True ($buildManifest.Contains('"Pattern":"**"')) 'Expected the static web assets build manifest to include the recursive discovery pattern for wwwroot.'
    Assert-True ($developmentManifest.Contains($normalizedWwwroot)) 'Expected the static web assets development manifest to point at the RCL wwwroot content root.'
}

function Test-RclHostAppPublish {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Configuration
    )

    $source = Join-Path $PSScriptRoot 'AspNetCore.Bundling.ESBuild.IntegrationTests/TestAssets/RclHostApp'
    $workingRoot = New-TempCopy -SourceDirectory $source -Name "RclHostApp-$Configuration"
    $hostProjectPath = Join-Path $workingRoot 'HostApp/HostApp.csproj'
    $publishDirectory = Join-Path $workingRoot 'publish'

    Invoke-DotNet -Arguments @(
        'publish',
        $hostProjectPath,
        '-c', $Configuration,
        '-o', $publishDirectory,
        "-p:AspNetCoreBundlingESBuildPackageVersion=$PackageVersion",
        "-p:RestoreSources=$RestoreSources",
        '-p:RestoreIgnoreFailedSources=true'
    ) -WorkingDirectory $workingRoot | Out-Null

    $rclBundlePath = Join-Path $workingRoot 'BasicRcl/wwwroot/js/library.js'
    $rclMapPath = Join-Path $workingRoot 'BasicRcl/wwwroot/js/library.js.map'
    $hostPublishManifestPath = Join-Path $workingRoot "HostApp/obj/$Configuration/net8.0/staticwebassets.publish.json"
    $publishedEndpointsManifestPath = Join-Path $publishDirectory 'HostApp.staticwebassets.endpoints.json'

    Assert-True (Test-Path $rclBundlePath) "Expected referenced RCL bundle to exist before host publish: $rclBundlePath"
    Assert-True (Test-Path $hostPublishManifestPath) "Expected host publish static web assets manifest to exist: $hostPublishManifestPath"
    Assert-True (Test-Path $publishedEndpointsManifestPath) "Expected published endpoints manifest to exist: $publishedEndpointsManifestPath"

    $rclBundleContent = Get-Content $rclBundlePath -Raw
    Assert-True ($rclBundleContent.Contains('Hosted BasicRcl says hello')) 'Expected referenced RCL bundled output to contain the expected content.'

    $hostPublishManifest = Get-Content $hostPublishManifestPath -Raw
    $normalizedRclWwwroot = ($workingRoot.Replace('\', '/') + '/BasicRcl/wwwroot/')

    Assert-True ($hostPublishManifest.Contains($normalizedRclWwwroot)) 'Expected the host publish manifest to point at the referenced RCL wwwroot content root.'
    Assert-True ($hostPublishManifest.Contains('"_content/BasicRcl"')) 'Expected the host publish manifest to use the _content/BasicRcl base path.'
    Assert-True ($hostPublishManifest.Contains('"Pattern":"**"')) 'Expected the host publish manifest to include the recursive discovery pattern for the RCL content root.'

    if ($Configuration -eq 'Debug') {
        Assert-True (Test-Path $rclMapPath) "Expected referenced RCL sourcemap to exist before host publish: $rclMapPath"
    }
    else {
        Assert-True (-not (Test-Path $rclMapPath)) "Did not expect referenced RCL sourcemap to exist before host publish: $rclMapPath"
    }
}

$ResolvedPackageFeed = [System.IO.Path]::GetFullPath($PackageFeed)
Assert-True (Test-Path $ResolvedPackageFeed) "Package feed directory does not exist: $ResolvedPackageFeed"
$RestoreSources = "$ResolvedPackageFeed;https://api.nuget.org/v3/index.json"

Test-RuntimeBinary
Test-BasicWebApp -Configuration 'Debug'
Test-BasicWebApp -Configuration 'Release'
Test-BasicWebAppFromDifferentWorkingDirectory
Test-BasicWebAppClean
Test-BasicWebAppPublish -Configuration 'Debug'
Test-BasicWebAppPublish -Configuration 'Release'
Test-MultiTargetWebApp
Test-SplittingWebApp
Test-ConfigurationOverrideWebApp
Test-InvalidConfigWebApp
Test-BasicRcl -Configuration 'Debug'
Test-BasicRcl -Configuration 'Release'
Test-RclHostAppPublish -Configuration 'Debug'
Test-RclHostAppPublish -Configuration 'Release'

Write-Host "Smoke tests completed successfully."
