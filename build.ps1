$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$version = "1.0.0-rc.2"
$appProject = ".\src\AudioLimits.App\AudioLimits.App.csproj"
$launcherProject = ".\src\AudioLimits.Launcher\AudioLimits.Launcher.csproj"
$testProject = ".\tests\AudioLimits.Core.Tests\AudioLimits.Core.Tests.csproj"
$installerScript = Join-Path $PSScriptRoot "installer\AudioLimits.iss"

$publish = Join-Path $PSScriptRoot "publish"
$releaseRoot = Join-Path $PSScriptRoot "release"
$artifactsRoot = Join-Path $PSScriptRoot "artifacts"
$appPayload = Join-Path $artifactsRoot "app-payload"
$launcherPayload = Join-Path $artifactsRoot "launcher"
$distributionRoot = Join-Path $artifactsRoot "distribution"
$distributionApp = Join-Path $distributionRoot "Audio Limits"
$zipStage = Join-Path $artifactsRoot "zip-stage"
$zipStageApp = Join-Path $zipStage "Audio Limits"
$reportPath = Join-Path $artifactsRoot "RELEASE_REPORT.txt"
$setupPath = Join-Path $releaseRoot "AudioLimits-Setup.exe"
$zipPath = Join-Path $releaseRoot "AudioLimits-$version-x64.zip"

function Stop-AudioLimitsProcesses {
    $processes = @(
        Get-Process -Name "AudioLimits" -ErrorAction SilentlyContinue
        Get-Process -Name "AudioLimits.App" -ErrorAction SilentlyContinue
    ) | Where-Object { $_ -ne $null } | Sort-Object Id -Unique

    if (-not $processes) {
        Write-Host "No running Audio Limits process found."
        return
    }

    Write-Host "Closing running Audio Limits process(es) before build/publish..."
    foreach ($process in $processes) {
        try {
            Stop-Process -Id $process.Id -Force -ErrorAction Stop
        }
        catch {
            throw "Could not close $($process.ProcessName).exe (PID $($process.Id)). Close it manually and run the build again. $($_.Exception.Message)"
        }
    }

    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    do {
        $remaining = @(
            Get-Process -Name "AudioLimits" -ErrorAction SilentlyContinue
            Get-Process -Name "AudioLimits.App" -ErrorAction SilentlyContinue
        ) | Where-Object { $_ -ne $null }
        if (-not $remaining) { break }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)

    if ($remaining) {
        throw "Audio Limits did not exit within 5 seconds. Close it manually and run the build again."
    }

    Write-Host "Audio Limits closed."
}

function Remove-DirectoryWithRetry([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return }

    for ($attempt = 1; $attempt -le 5; $attempt++) {
        try {
            Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
            return
        }
        catch {
            if ($attempt -eq 5) { throw }
            Start-Sleep -Milliseconds (150 * $attempt)
        }
    }
}

function Format-Bytes([long]$Bytes) {
    if ($Bytes -ge 1GB) { return ("{0:N2} GB" -f ($Bytes / 1GB)) }
    if ($Bytes -ge 1MB) { return ("{0:N1} MB" -f ($Bytes / 1MB)) }
    if ($Bytes -ge 1KB) { return ("{0:N1} KB" -f ($Bytes / 1KB)) }
    return "$Bytes B"
}

function Get-DirectorySize([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return [long]0 }
    $measure = Get-ChildItem -LiteralPath $Path -File -Recurse -Force | Measure-Object -Property Length -Sum
    if ($null -eq $measure.Sum) { return [long]0 }
    return [long]$measure.Sum
}

function Find-InnoCompiler {
    $candidates = @(
        (Join-Path $env:ProgramFiles "Inno Setup 7\ISCC.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 7\ISCC.exe"),
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 7\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe")
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }

    return $candidates | Select-Object -First 1
}

function Ensure-InnoCompiler {
    $compiler = Find-InnoCompiler
    if ($compiler) { return $compiler }

    $winget = Get-Command winget.exe -ErrorAction SilentlyContinue
    if (-not $winget) {
        Write-Warning "Inno Setup is not installed and winget is unavailable. Release packaging requires Inno Setup 6 or newer."
        return $null
    }

    Write-Host ""
    Write-Host "Inno Setup is not installed. Installing the build-only compiler with winget..."
    & $winget.Source install --id JRSoftware.InnoSetup -e -s winget --silent --accept-package-agreements --accept-source-agreements | Out-Host
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "winget could not install Inno Setup."
        return $null
    }

    $compiler = Find-InnoCompiler
    if (-not $compiler) {
        Write-Warning "Inno Setup installation completed but ISCC.exe was not found in a standard location."
        return $null
    }

    return $compiler
}

function Publish-Project {
    param(
        [string]$Project,
        [string]$OutputPath,
        [string[]]$Properties
    )

    Remove-DirectoryWithRetry $OutputPath
    New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null

    $arguments = @(
        "publish", $Project,
        "-c", "Release",
        "-r", "win-x64",
        "-o", $OutputPath,
        "--nologo"
    )

    foreach ($property in $Properties) {
        $arguments += "-p:$property"
    }

    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $Project -> $OutputPath" }
}

function Copy-DirectoryContents([string]$Source, [string]$Destination) {
    if (-not (Test-Path -LiteralPath $Source)) { throw "Source directory not found: $Source" }
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    Get-ChildItem -LiteralPath $Source -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $Destination -Recurse -Force
    }
}

Write-Host "Audio Limits $version GitHub release-candidate build"
Write-Host ""
Write-Host "This build creates exactly two user-facing release assets:"
Write-Host "  1) AudioLimits-Setup.exe (recommended normal installation)"
Write-Host "  2) AudioLimits-$version-x64.zip (extract-and-run, no installer registration)"
Write-Host ""

Stop-AudioLimitsProcesses
Remove-DirectoryWithRetry $publish
Remove-DirectoryWithRetry $releaseRoot
Remove-DirectoryWithRetry $artifactsRoot
New-Item -ItemType Directory -Path $publish -Force | Out-Null
New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
New-Item -ItemType Directory -Path $artifactsRoot -Force | Out-Null

Write-Host ""
Write-Host "Restoring solution..."
dotnet restore .\AudioLimits.sln
if ($LASTEXITCODE -ne 0) { throw "Restore failed." }

Write-Host ""
Write-Host "Running preserved core tests..."
dotnet test $testProject -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw "Tests failed." }

Write-Host ""
Write-Host "Building WinUI application..."
dotnet build $appProject -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw "WinUI build failed." }

Write-Host ""
Write-Host "Building self-contained prerequisite launcher..."
dotnet build $launcherProject -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw "Launcher build failed." }

Write-Host ""
Write-Host "Publishing framework-dependent WinUI application payload..."
Publish-Project -Project $appProject -OutputPath $appPayload -Properties @(
    "WindowsAppSDKSelfContained=false",
    "WindowsAppSdkBootstrapInitialize=true",
    "SelfContained=false",
    "PublishSingleFile=false",
    "IncludeAllContentForSelfExtract=false",
    "EnableCompressionInSingleFile=false",
    "PublishTrimmed=false",
    "DebugType=None",
    "DebugSymbols=false"
)

Write-Host ""
Write-Host "Publishing self-contained bootstrap launcher..."
Publish-Project -Project $launcherProject -OutputPath $launcherPayload -Properties @(
    "SelfContained=true",
    "PublishSingleFile=true",
    "PublishTrimmed=true",
    "TrimMode=full",
    "EnableCompressionInSingleFile=true",
    "DebugType=None",
    "DebugSymbols=false",
    "InvariantGlobalization=true"
)

$launcherExe = Join-Path $launcherPayload "AudioLimits.exe"
$appExe = Join-Path $appPayload "AudioLimits.App.exe"
if (-not (Test-Path -LiteralPath $launcherExe)) { throw "Bootstrap launcher output was not found: $launcherExe" }
if (-not (Test-Path -LiteralPath $appExe)) { throw "WinUI app output was not found: $appExe" }

Write-Host ""
Write-Host "Assembling canonical application layout..."
Remove-DirectoryWithRetry $distributionRoot
New-Item -ItemType Directory -Path $distributionApp -Force | Out-Null
$appSubdirectory = Join-Path $distributionApp "app"
New-Item -ItemType Directory -Path $appSubdirectory -Force | Out-Null
Copy-DirectoryContents $appPayload $appSubdirectory
Copy-Item -LiteralPath $launcherExe -Destination (Join-Path $distributionApp "AudioLimits.exe") -Force

# publish\ is an exact copy of the folder users receive after Setup or inside the ZIP.
# Root is intentionally user-facing; implementation/runtime files live under .\app\.
Copy-DirectoryContents $distributionApp $publish

Write-Host ""
Write-Host "Creating no-install x64 ZIP..."
Add-Type -AssemblyName System.IO.Compression.FileSystem
Remove-DirectoryWithRetry $zipStage
New-Item -ItemType Directory -Path $zipStage -Force | Out-Null
Copy-DirectoryContents $distributionApp $zipStageApp
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $zipStage,
    $zipPath,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $false)
if (-not (Test-Path -LiteralPath $zipPath)) { throw "ZIP packaging failed: $zipPath" }

$runtimeConfigPath = Join-Path $appPayload "AudioLimits.App.runtimeconfig.json"
$runtimeConfigText = if (Test-Path -LiteralPath $runtimeConfigPath) { Get-Content -LiteralPath $runtimeConfigPath -Raw } else { "(not found)" }

$innoCompiler = Ensure-InnoCompiler
if (-not $innoCompiler) {
    throw "Inno Setup is required to produce the recommended AudioLimits-Setup.exe release asset."
}

Write-Host ""
Write-Host "Compiling AudioLimits-Setup.exe with Inno Setup..."
& $innoCompiler /Qp $installerScript
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed." }
if (-not (Test-Path -LiteralPath $setupPath)) { throw "Inno Setup reported success but AudioLimits-Setup.exe was not found." }

$launcherBytes = (Get-Item -LiteralPath $launcherExe).Length
$appPayloadBytes = Get-DirectorySize $appPayload
$distributionBytes = Get-DirectorySize $distributionApp
$setupBytes = (Get-Item -LiteralPath $setupPath).Length
$zipBytes = (Get-Item -LiteralPath $zipPath).Length

$setupHash = (Get-FileHash $setupPath -Algorithm SHA256).Hash
$zipHash = (Get-FileHash $zipPath -Algorithm SHA256).Hash
$launcherHash = (Get-FileHash $launcherExe -Algorithm SHA256).Hash

$report = @(
    "Audio Limits $version release candidate",
    "Generated: $([DateTime]::Now.ToString('yyyy-MM-dd HH:mm:ss'))",
    "",
    "User-facing GitHub release assets",
    "---------------------------------",
    "Setup:       $setupPath",
    "Setup size:  $(Format-Bytes $setupBytes) ($setupBytes bytes)",
    "Setup SHA:   $setupHash",
    "",
    "No-install:  $zipPath",
    "ZIP size:    $(Format-Bytes $zipBytes) ($zipBytes bytes)",
    "ZIP SHA:     $zipHash",
    "",
    "Canonical application folder",
    "----------------------------",
    "Path:        $distributionApp",
    "Folder size: $(Format-Bytes $distributionBytes) ($distributionBytes bytes)",
    "Root entry:  $(Join-Path $distributionApp 'AudioLimits.exe')",
    "App host:    $(Join-Path $distributionApp 'app\AudioLimits.App.exe')",
    "",
    "Bootstrap launcher",
    "------------------",
    "Size:       $(Format-Bytes $launcherBytes) ($launcherBytes bytes)",
    "SHA-256:    $launcherHash",
    "Policy:     trimmed/compressed self-contained .NET launcher; no WinUI dependency",
    "",
    "Framework-dependent WinUI payload",
    "---------------------------------",
    "Folder size: $(Format-Bytes $appPayloadBytes) ($appPayloadBytes bytes)",
    "",
    "Runtime config emitted by the WinUI publish",
    "-------------------------------------------",
    $runtimeConfigText,
    "",
    "Release policy",
    "--------------",
    "- AudioLimits-Setup.exe is the recommended normal Windows installation.",
    "- AudioLimits-$version-x64.zip is the only alternative public binary: extract the complete Audio Limits folder and run its root AudioLimits.exe.",
    "- The ZIP is intentionally not called portable; settings remain in the normal per-user Audio Limits settings location and Start with Windows can still create normal user integration.",
    "- Root AudioLimits.exe checks .NET 8 Desktop Runtime x64, Visual C++ v14 x64 (14.50+), and Windows App Runtime 2.3.1 and can offer to acquire missing components from Microsoft.",
    "- The healthy-machine and relocated-folder launcher paths have been real-Windows tested. Clean-machine missing-prerequisite recovery is deliberately still marked unverified until a VM test is performed.",
    "- Equalizer APO remains an app-level interactive dependency because playback-device selection requires user interaction."
)
Set-Content -LiteralPath $reportPath -Value $report -Encoding UTF8

Write-Host ""
Write-Host "==================== $version OUTPUTS ===================="
Write-Host "Setup:              $setupPath"
Write-Host "Setup size:         $(Format-Bytes $setupBytes)"
Write-Host "No-install ZIP:     $zipPath"
Write-Host "ZIP size:           $(Format-Bytes $zipBytes)"
Write-Host "Canonical test dir: $publish"
Write-Host "Build report:       $reportPath"
Write-Host "==========================================================="
Write-Host ""
Write-Host "The release folder contains only the two user-facing GitHub assets."
