$ErrorActionPreference = 'Stop'

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$dotnetExe = Join-Path $projectRoot '.dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnetExe)) {
    $dotnetExe = 'dotnet'
}
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.dotnet\cli-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

$appProject = Join-Path $projectRoot 'IPhoneBridge.Windows\IPhoneBridge.Windows.csproj'
$setupProject = Join-Path $PSScriptRoot 'IPhoneBridge.Setup\IPhoneBridge.Setup.csproj'
$workRoot = Join-Path $projectRoot '.installer-work'
$releaseDirectory = Join-Path $projectRoot 'release'

New-Item -ItemType Directory -Force -Path $workRoot, $releaseDirectory | Out-Null

$noticeOutputPath = Join-Path $workRoot 'THIRD-PARTY-NOTICES.txt'
$nugetRoots = @(
    (Join-Path $env:DOTNET_CLI_HOME '.nuget\packages'),
    (Join-Path $projectRoot '.nuget\packages')
)
$legalPackages = @(
    'qrcoder',
    'system.drawing.common',
    'microsoft.win32.systemevents',
    'microsoft.netcore.app.runtime.win-x64',
    'microsoft.aspnetcore.app.runtime.win-x64',
    'microsoft.windowsdesktop.app.runtime.win-x64'
)
$noticeSections = [System.Collections.Generic.List[string]]::new()
foreach ($packageName in $legalPackages) {
    $packageVersionDirectory = $null
    foreach ($nugetRoot in $nugetRoots) {
        $packagePath = Join-Path $nugetRoot $packageName
        if (-not (Test-Path -LiteralPath $packagePath -PathType Container)) { continue }
        $packageVersionDirectory = Get-ChildItem -LiteralPath $packagePath -Directory |
            Sort-Object { try { [version]$_.Name } catch { [version]'0.0' } } -Descending |
            Select-Object -First 1
        if ($packageVersionDirectory) { break }
    }
    if (-not $packageVersionDirectory) {
        throw "Could not locate the restored license package: $packageName"
    }

    $legalFiles = Get-ChildItem -LiteralPath $packageVersionDirectory.FullName -File |
        Where-Object { $_.Name -match '^(LICENSE|THIRD-PARTY-NOTICES)' } |
        Sort-Object Name
    if (-not $legalFiles) {
        throw "No license or third-party notices were found for $packageName."
    }
    foreach ($legalFile in $legalFiles) {
        $noticeSections.Add("===== $packageName $($packageVersionDirectory.Name) / $($legalFile.Name) =====")
        $noticeSections.Add((Get-Content -LiteralPath $legalFile.FullName -Raw))
        $noticeSections.Add('')
    }
}
[System.IO.File]::WriteAllText($noticeOutputPath, ($noticeSections -join [Environment]::NewLine), [System.Text.Encoding]::UTF8)

function Publish-InstallerVariant {
    param(
        [Parameter(Mandatory)] [string] $Variant,
        [Parameter(Mandatory)] [bool] $SelfContained,
        [Parameter(Mandatory)] [string] $RuntimeRequirement,
        [Parameter(Mandatory)] [string] $InstallerFileName
    )

    $appPublishDirectory = Join-Path $workRoot "app-$Variant"
    $setupPublishDirectory = Join-Path $workRoot "setup-$Variant"
    $runtimeRequirementPath = Join-Path $workRoot "runtime-requirement-$Variant.txt"
    $appPayloadPath = Join-Path $appPublishDirectory 'IPhoneBridge.Windows.exe'
    $installerPath = Join-Path $releaseDirectory $InstallerFileName
    New-Item -ItemType Directory -Force -Path $appPublishDirectory, $setupPublishDirectory | Out-Null
    [System.IO.File]::WriteAllText($runtimeRequirementPath, $RuntimeRequirement, [System.Text.Encoding]::UTF8)

    & $dotnetExe publish $appProject -c Release -r win-x64 "-p:SelfContained=$($SelfContained.ToString().ToLowerInvariant())" `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        "-p:EnableCompressionInSingleFile=$($SelfContained.ToString().ToLowerInvariant())" `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $appPublishDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "The $Variant Windows application publish failed; this installer was not generated."
    }
    if (-not (Test-Path -LiteralPath $appPayloadPath -PathType Leaf)) {
        throw "Required $Variant application payload was not found: $appPayloadPath"
    }

    & $dotnetExe publish $setupProject -c Release -r win-x64 --self-contained true `
        "-p:AppPayloadPath=$appPayloadPath" `
        "-p:AppNoticesPath=$noticeOutputPath" `
        "-p:RuntimeRequirementPath=$runtimeRequirementPath" `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $setupPublishDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "The $Variant setup application publish failed; this installer was not generated."
    }

    $setupExe = Join-Path $setupPublishDirectory 'IPhoneBridge.Setup.exe'
    if (-not (Test-Path -LiteralPath $setupExe -PathType Leaf)) {
        throw "Required $Variant setup output was not found: $setupExe"
    }

    Copy-Item -LiteralPath $setupExe -Destination $installerPath -Force
    $installer = Get-Item -LiteralPath $installerPath
    $appPayload = Get-Item -LiteralPath $appPayloadPath
    Write-Output "$Variant app: $([Math]::Round($appPayload.Length / 1MB, 1)) MB"
    Write-Output "$Variant installer: $($installer.FullName)"
    Write-Output "$Variant installer size: $([Math]::Round($installer.Length / 1MB, 1)) MB"
}

Publish-InstallerVariant -Variant 'full' -SelfContained $true -RuntimeRequirement 'self-contained' -InstallerFileName 'IPhoneBridge-Setup-x64.exe'
Publish-InstallerVariant -Variant 'light' -SelfContained $false -RuntimeRequirement 'framework-dependent' -InstallerFileName 'IPhoneBridge-Setup-x64-Light.exe'
