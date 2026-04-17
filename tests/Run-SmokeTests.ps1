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

    $commandText = "dotnet $($Arguments -join ' ')"
    Write-Host "Running: $commandText"
    Write-Host "Working directory: $WorkingDirectory"

    Push-Location $WorkingDirectory
    try {
        $output = & dotnet @Arguments 2>&1
        $commandSucceeded = $?
        $exitCode = Get-LastExitCode -CommandSucceeded:$commandSucceeded
    }
    finally {
        Pop-Location
        $env:NUGET_PACKAGES = $originalNuGetPackages
    }

    $outputText = [string]::Join([Environment]::NewLine, $output)

    if (-not $ExpectFailure -and $exitCode -ne 0) {
        Write-Host "dotnet command failed with exit code $exitCode."
        Write-Host '--- dotnet output start ---'
        Write-Host $outputText
        Write-Host '--- dotnet output end ---'
        throw "dotnet $($Arguments -join ' ') failed with exit code $exitCode.`n$outputText"
    }

    if ($ExpectFailure -and $exitCode -eq 0) {
        throw "dotnet $($Arguments -join ' ') succeeded unexpectedly.`n$outputText"
    }

    return $outputText
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

function Get-LastExitCode {
    param(
        [Parameter(Mandatory = $true)]
        [bool]$CommandSucceeded
    )

    $exitCodeVariable = Get-Variable -Name LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue
    if ($null -eq $exitCodeVariable) {
        return $(if ($CommandSucceeded) { 0 } else { 1 })
    }

    return [int]$exitCodeVariable.Value
}

function Ensure-ExecutablePermission {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if ($IsWindows) {
        return
    }

    $chmodOutput = & chmod +x $Path 2>&1
    $commandSucceeded = $?
    $exitCode = Get-LastExitCode -CommandSucceeded:$commandSucceeded

    Assert-True ($exitCode -eq 0) "Failed to mark '$Path' as executable.`n$chmodOutput"
}

function Get-LineBreakCount {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Content
    )

    return ([regex]::Matches($Content, '\r?\n')).Count
}

function Normalize-PathForComparison {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $normalized = $Path -replace '\\', '/'
    $normalized = $normalized.TrimEnd('/')
    return $normalized.ToLowerInvariant()
}

function Get-ManifestContentRoots {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Manifest
    )

    $contentRoots = @()

    if ($null -ne $Manifest.PSObject.Properties['DiscoveryPatterns']) {
        $contentRoots += @($Manifest.DiscoveryPatterns | ForEach-Object { $_.ContentRoot })
    }

    if ($null -ne $Manifest.PSObject.Properties['ContentRoots']) {
        $contentRoots += @($Manifest.ContentRoots)
    }

    return @($contentRoots | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

function New-TempCopy {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourceDirectory,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $root = Join-Path ([System.IO.Path]::GetTempPath()) ('ESBuild.AspNetCore.Tests.' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $root | Out-Null

    $target = Join-Path $root $Name
    Copy-Item -Path $SourceDirectory -Destination $target -Recurse

    if (-not $IsWindows) {
        $realPathOutput = & realpath $target 2>&1
        $commandSucceeded = $?
        $exitCode = Get-LastExitCode -CommandSucceeded:$commandSucceeded
        if ($exitCode -eq 0) {
            $realPath = [string]::Join([Environment]::NewLine, @($realPathOutput)).Trim()
            if (-not [string]::IsNullOrWhiteSpace($realPath)) {
                return $realPath
            }
        }
    }

    return (Resolve-Path -LiteralPath $target).Path
}

function Get-EsbuildRuntimePath {
    $repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
    $runtimesRoot = Join-Path $repoRoot 'src/ESBuild.AspNetCore/runtimes'
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
    Ensure-ExecutablePermission -Path $runtimePath

    $versionOutput = & $runtimePath --version 2>&1
    $commandSucceeded = $?
    $exitCode = Get-LastExitCode -CommandSucceeded:$commandSucceeded
    $version = [string]::Join([Environment]::NewLine, @($versionOutput))

    Assert-True ($exitCode -eq 0) "Running '$runtimePath --version' failed with exit code $exitCode.`n$version"
    Assert-True ($version.Trim() -eq $UpstreamVersion) "Expected runtime version '$UpstreamVersion' but got '$version'."
}

function Test-BasicWebApp {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Configuration
    )

    $source = Join-Path $PSScriptRoot 'ESBuild.AspNetCore.IntegrationTests/TestAssets/BasicWebApp'
    $workingDirectory = New-TempCopy -SourceDirectory $source -Name "BasicWebApp-$Configuration"
    $projectPath = Join-Path $workingDirectory 'BasicWebApp.csproj'

    Invoke-DotNet -Arguments @(
        'build',
        $projectPath,
        '-c', $Configuration,
        "-p:ESBuildAspNetCorePackageVersion=$PackageVersion",
        "-p:RestoreAdditionalProjectSources=$RestoreSources",
        '-p:RestoreIgnoreFailedSources=true'
    ) -WorkingDirectory $workingDirectory | Out-Null

    $scriptPath = Join-Path $workingDirectory 'wwwroot' 'js' 'site.js'
    $mapPath = Join-Path $workingDirectory 'wwwroot' 'js' 'site.js.map'
    $optionalPath = Join-Path $workingDirectory 'wwwroot' 'js' 'missing-optional.js'

    Assert-True (Test-Path $scriptPath) "Expected bundled script to exist: $scriptPath"
    Assert-True (-not (Test-Path $optionalPath)) "Optional bundle output should not exist: $optionalPath"

    $scriptContent = Get-Content $scriptPath -Raw
    Assert-True ($scriptContent.Contains('Hello from ESBuild.AspNetCore')) "Bundled output does not contain the expected content."
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
    $source = Join-Path $PSScriptRoot 'ESBuild.AspNetCore.IntegrationTests/TestAssets/BasicWebApp'
    $workingDirectory = New-TempCopy -SourceDirectory $source -Name 'BasicWebApp-ExternalWorkingDirectory'
    $projectPath = Join-Path $workingDirectory 'BasicWebApp.csproj'
    $invocationDirectory = Split-Path -Parent $workingDirectory

    Invoke-DotNet -Arguments @(
        'build',
        $projectPath,
        '-c', 'Debug',
        "-p:ESBuildAspNetCorePackageVersion=$PackageVersion",
        "-p:RestoreAdditionalProjectSources=$RestoreSources",
        '-p:RestoreIgnoreFailedSources=true'
    ) -WorkingDirectory $invocationDirectory | Out-Null

    $scriptPath = Join-Path $workingDirectory 'wwwroot' 'js' 'site.js'
    $mapPath = Join-Path $workingDirectory 'wwwroot' 'js' 'site.js.map'

    Assert-True (Test-Path $scriptPath) "Expected bundled script to exist when building outside the project directory: $scriptPath"
    Assert-True (Test-Path $mapPath) "Expected sourcemap to exist when building outside the project directory: $mapPath"
}

function Test-BasicWebAppClean {
    $source = Join-Path $PSScriptRoot 'ESBuild.AspNetCore.IntegrationTests/TestAssets/BasicWebApp'
    $workingDirectory = New-TempCopy -SourceDirectory $source -Name 'BasicWebApp-Clean'
    $projectPath = Join-Path $workingDirectory 'BasicWebApp.csproj'
    $scriptPath = Join-Path $workingDirectory 'wwwroot' 'js' 'site.js'
    $mapPath = Join-Path $workingDirectory 'wwwroot' 'js' 'site.js.map'

    Invoke-DotNet -Arguments @(
        'build',
        $projectPath,
        '-c', 'Debug',
        "-p:ESBuildAspNetCorePackageVersion=$PackageVersion",
        "-p:RestoreAdditionalProjectSources=$RestoreSources",
        '-p:RestoreIgnoreFailedSources=true'
    ) -WorkingDirectory $workingDirectory | Out-Null

    Assert-True (Test-Path $scriptPath) "Expected bundled script to exist before clean: $scriptPath"
    Assert-True (Test-Path $mapPath) "Expected sourcemap to exist before clean: $mapPath"

    Invoke-DotNet -Arguments @(
        'clean',
        $projectPath,
        '-c', 'Debug',
        "-p:ESBuildAspNetCorePackageVersion=$PackageVersion",
        "-p:RestoreAdditionalProjectSources=$RestoreSources",
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

    $source = Join-Path $PSScriptRoot 'ESBuild.AspNetCore.IntegrationTests/TestAssets/BasicWebApp'
    $workingDirectory = New-TempCopy -SourceDirectory $source -Name "BasicWebAppPublish-$Configuration"
    $projectPath = Join-Path $workingDirectory 'BasicWebApp.csproj'
    $publishDirectory = Join-Path $workingDirectory 'publish'

    Invoke-DotNet -Arguments @(
        'publish',
        $projectPath,
        '-c', $Configuration,
        '-o', $publishDirectory,
        "-p:ESBuildAspNetCorePackageVersion=$PackageVersion",
        "-p:RestoreAdditionalProjectSources=$RestoreSources",
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
    $source = Join-Path $PSScriptRoot 'ESBuild.AspNetCore.IntegrationTests/TestAssets/MultiTargetWebApp'
    $workingDirectory = New-TempCopy -SourceDirectory $source -Name 'MultiTargetWebApp'
    $projectPath = Join-Path $workingDirectory 'MultiTargetWebApp.csproj'

    $output = Invoke-DotNet -Arguments @(
        'build',
        $projectPath,
        '-c', 'Debug',
        '-v:n',
        "-p:ESBuildAspNetCorePackageVersion=$PackageVersion",
        "-p:RestoreAdditionalProjectSources=$RestoreSources",
        '-p:RestoreIgnoreFailedSources=true'
    ) -WorkingDirectory $workingDirectory

    $scriptPath = Join-Path $workingDirectory 'wwwroot' 'js' 'site.js'
    $net8OutputPath = Join-Path $workingDirectory 'bin/Debug/net8.0/MultiTargetWebApp.dll'
    $net6OutputPath = Join-Path $workingDirectory 'bin/Debug/net6.0/MultiTargetWebApp.dll'
    $bundleInvocationCount = ([regex]::Matches($output, [regex]::Escape('Building TypeScript:'))).Count

    Assert-True (Test-Path $scriptPath) "Expected bundled script to exist for multitarget build: $scriptPath"
    Assert-True (Test-Path $net8OutputPath) "Expected net8.0 build output to exist: $net8OutputPath"
    Assert-True (Test-Path $net6OutputPath) "Expected net6.0 build output to exist: $net6OutputPath"
    Assert-True ($bundleInvocationCount -eq 1) "Expected multi-target build to invoke esbuild once, but found $bundleInvocationCount invocations.`n$output"
}

function Test-SplittingWebApp {
    $source = Join-Path $PSScriptRoot 'ESBuild.AspNetCore.IntegrationTests/TestAssets/SplittingWebApp'
    $workingDirectory = New-TempCopy -SourceDirectory $source -Name 'SplittingWebApp'
    $projectPath = Join-Path $workingDirectory 'SplittingWebApp.csproj'

    Invoke-DotNet -Arguments @(
        'build',
        $projectPath,
        '-c', 'Debug',
        "-p:ESBuildAspNetCorePackageVersion=$PackageVersion",
        "-p:RestoreAdditionalProjectSources=$RestoreSources",
        '-p:RestoreIgnoreFailedSources=true'
    ) -WorkingDirectory $workingDirectory | Out-Null

    $outputDirectory = Join-Path $workingDirectory 'wwwroot' 'js'
    $sitePath = Join-Path $outputDirectory 'site.js'
    $siteMapPath = Join-Path $outputDirectory 'site.js.map'
    $jsFiles = @(Get-ChildItem -Path $outputDirectory -Filter '*.js' -File)
    $chunkFiles = @($jsFiles | Where-Object { $_.Name -ne 'site.js' })
    $manifestDirectory = Join-Path $workingDirectory 'obj' 'ESBuild.AspNetCore'
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
    $source = Join-Path $PSScriptRoot 'ESBuild.AspNetCore.IntegrationTests/TestAssets/ConfigurationOverrideWebApp'
    $workingDirectory = New-TempCopy -SourceDirectory $source -Name 'ConfigurationOverrideWebApp'
    $projectPath = Join-Path $workingDirectory 'ConfigurationOverrideWebApp.csproj'

    Invoke-DotNet -Arguments @(
        'build',
        $projectPath,
        '-c', 'Release',
        "-p:ESBuildAspNetCorePackageVersion=$PackageVersion",
        "-p:RestoreAdditionalProjectSources=$RestoreSources",
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
    $source = Join-Path $PSScriptRoot 'ESBuild.AspNetCore.IntegrationTests/TestAssets/InvalidConfigWebApp'
    $workingDirectory = New-TempCopy -SourceDirectory $source -Name 'InvalidConfigWebApp'
    $projectPath = Join-Path $workingDirectory 'InvalidConfigWebApp.csproj'

    $output = Invoke-DotNet -Arguments @(
        'build',
        $projectPath,
        '-c', 'Debug',
        "-p:ESBuildAspNetCorePackageVersion=$PackageVersion",
        "-p:RestoreAdditionalProjectSources=$RestoreSources",
        '-p:RestoreIgnoreFailedSources=true'
    ) -WorkingDirectory $workingDirectory -ExpectFailure

    Assert-True ($output.Contains("Bundle 'Scripts/site.ts' uses unsupported esbuild format 'not-a-real-format'")) 'Expected invalid config build output to mention the unsupported format.'
}

function Test-BasicRcl {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Configuration
    )

    $source = Join-Path $PSScriptRoot 'ESBuild.AspNetCore.IntegrationTests/TestAssets/BasicRcl'
    $workingDirectory = New-TempCopy -SourceDirectory $source -Name "BasicRcl-$Configuration"
    $projectPath = Join-Path $workingDirectory 'BasicRcl.csproj'

    Invoke-DotNet -Arguments @(
        'build',
        $projectPath,
        '-c', $Configuration,
        "-p:ESBuildAspNetCorePackageVersion=$PackageVersion",
        "-p:RestoreAdditionalProjectSources=$RestoreSources",
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

    $buildManifest = Get-Content $buildManifestPath -Raw | ConvertFrom-Json
    $developmentManifest = Get-Content $developmentManifestPath -Raw | ConvertFrom-Json
    $expectedWwwroot = Normalize-PathForComparison -Path (Join-Path $workingDirectory 'wwwroot')

    $buildManifestRoots = @(Get-ManifestContentRoots -Manifest $buildManifest | ForEach-Object { Normalize-PathForComparison -Path $_ })
    $developmentManifestRoots = @(Get-ManifestContentRoots -Manifest $developmentManifest | ForEach-Object { Normalize-PathForComparison -Path $_ })
    $buildPatterns = @($buildManifest.DiscoveryPatterns | ForEach-Object { $_.Pattern })

    Assert-True ($buildManifestRoots -icontains $expectedWwwroot) 'Expected the static web assets build manifest to point at the RCL wwwroot content root.'
    Assert-True ($buildPatterns -contains '**') 'Expected the static web assets build manifest to include the recursive discovery pattern for wwwroot.'
    Assert-True ($developmentManifestRoots -icontains $expectedWwwroot) 'Expected the static web assets development manifest to point at the RCL wwwroot content root.'
}

function Test-RclHostAppPublish {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Configuration
    )

    $source = Join-Path $PSScriptRoot 'ESBuild.AspNetCore.IntegrationTests/TestAssets/RclHostApp'
    $workingRoot = New-TempCopy -SourceDirectory $source -Name "RclHostApp-$Configuration"
    $hostProjectPath = Join-Path $workingRoot 'HostApp/HostApp.csproj'
    $publishDirectory = Join-Path $workingRoot 'publish'
    $rclProjectPath = Join-Path $workingRoot 'BasicRcl/BasicRcl.csproj'

    Write-Host "RclHostApp working root: $workingRoot"
    Write-Host "Host project path: $hostProjectPath"
    Write-Host "RCL project path: $rclProjectPath"

    Invoke-DotNet -Arguments @(
        'publish',
        $hostProjectPath,
        '-c', $Configuration,
        '-o', $publishDirectory,
        "-p:ESBuildAspNetCorePackageVersion=$PackageVersion",
        "-p:RestoreAdditionalProjectSources=$RestoreSources",
        '-p:RestoreIgnoreFailedSources=true'
    ) -WorkingDirectory $workingRoot | Out-Null

    $rclBundlePath = Join-Path $workingRoot 'BasicRcl/wwwroot/js/library.js'
    $rclMapPath = Join-Path $workingRoot 'BasicRcl/wwwroot/js/library.js.map'
    $hostPublishManifestPath = Join-Path $workingRoot "HostApp/obj/$Configuration/net8.0/staticwebassets.publish.json"
    $publishedEndpointsManifestPath = Join-Path $publishDirectory 'HostApp.staticwebassets.endpoints.json'
    $publishedRclScriptPath = Join-Path $publishDirectory 'wwwroot/_content/BasicRcl/js/library.js'
    $publishedIncorrectRclScriptPath = Join-Path $publishDirectory 'wwwroot/js/library.js'

    Assert-True (Test-Path $rclBundlePath) "Expected referenced RCL bundle to exist before host publish: $rclBundlePath"
    Assert-True (Test-Path $hostPublishManifestPath) "Expected host publish static web assets manifest to exist: $hostPublishManifestPath"
    Assert-True (Test-Path $publishedEndpointsManifestPath) "Expected published endpoints manifest to exist: $publishedEndpointsManifestPath"
    Assert-True (Test-Path $publishedRclScriptPath) "Expected published RCL bundle to exist under static web assets path: $publishedRclScriptPath"
    Assert-True (-not (Test-Path $publishedIncorrectRclScriptPath)) "Did not expect published RCL bundle at host content path: $publishedIncorrectRclScriptPath"

    $rclBundleContent = Get-Content $rclBundlePath -Raw
    Assert-True ($rclBundleContent.Contains('Hosted BasicRcl says hello')) 'Expected referenced RCL bundled output to contain the expected content.'

    $hostPublishManifest = Get-Content $hostPublishManifestPath -Raw | ConvertFrom-Json
    $expectedRclWwwroot = Normalize-PathForComparison -Path (Join-Path $workingRoot 'BasicRcl/wwwroot')

    $hostPublishRoots = @(Get-ManifestContentRoots -Manifest $hostPublishManifest | ForEach-Object { Normalize-PathForComparison -Path $_ })
    $hostPublishBasePaths = @($hostPublishManifest.DiscoveryPatterns | ForEach-Object { $_.BasePath })
    $hostPublishPatterns = @($hostPublishManifest.DiscoveryPatterns | ForEach-Object { $_.Pattern })

    Assert-True ($hostPublishRoots -icontains $expectedRclWwwroot) 'Expected the host publish manifest to point at the referenced RCL wwwroot content root.'
    Assert-True ($hostPublishBasePaths -contains '_content/BasicRcl') 'Expected the host publish manifest to use the _content/BasicRcl base path.'
    Assert-True ($hostPublishPatterns -contains '**') 'Expected the host publish manifest to include the recursive discovery pattern for the RCL content root.'

    if ($Configuration -eq 'Debug') {
        Assert-True (Test-Path $rclMapPath) "Expected referenced RCL sourcemap to exist before host publish: $rclMapPath"
    }
    else {
        Assert-True (-not (Test-Path $rclMapPath)) "Did not expect referenced RCL sourcemap to exist before host publish: $rclMapPath"
    }
}

$ResolvedPackageFeed = [System.IO.Path]::GetFullPath($PackageFeed)
Assert-True (Test-Path $ResolvedPackageFeed) "Package feed directory does not exist: $ResolvedPackageFeed"
$RestoreSources = $ResolvedPackageFeed

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

