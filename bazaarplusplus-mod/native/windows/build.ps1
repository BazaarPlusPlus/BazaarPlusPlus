param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'build'),
    [switch]$SkipSmoke
)

$ErrorActionPreference = 'Stop'

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path -LiteralPath $vswhere)) {
    throw 'Visual Studio Installer vswhere.exe was not found.'
}

$install = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if ([string]::IsNullOrWhiteSpace($install)) {
    throw 'Visual Studio C++ build tools were not found.'
}

$vcvars = Join-Path $install 'VC\Auxiliary\Build\vcvars64.bat'
$sourceDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$buildDir = $OutputDirectory
New-Item -ItemType Directory -Force -Path $buildDir | Out-Null

$command = 'call "{0}" && cl /nologo /std:c++17 /utf-8 /EHsc /W4 /WX /O2 /MT /LD /DNOMINMAX /DUNICODE /D_UNICODE "{1}\BppReplayMediaFoundation.cpp" /link /Brepro /OUT:"{2}\GfxPluginBppReplayMediaFoundation.dll" /IMPLIB:"{2}\BppReplayMediaFoundation.lib" d3d11.lib dxgi.lib evr.lib mfplat.lib mfreadwrite.lib mfuuid.lib ole32.lib shlwapi.lib' -f $vcvars, $sourceDir, $buildDir
cmd.exe /d /s /c $command
if ($LASTEXITCODE -ne 0) {
    throw "Native Windows replay plugin build failed with exit code $LASTEXITCODE."
}

$binary = Join-Path $buildDir 'GfxPluginBppReplayMediaFoundation.dll'
$headers = cmd.exe /d /s /c ('call "{0}" && dumpbin /headers "{1}"' -f $vcvars, $binary)
if ($LASTEXITCODE -ne 0 -or ($headers -join "`n") -notmatch 'machine \(x64\)') {
    throw 'Native Windows replay plugin is not an x64 PE image.'
}

$catalog = Get-Content -Raw -LiteralPath (Join-Path $sourceDir '..\artifacts.json') | ConvertFrom-Json
$artifactPolicy = $catalog.platforms.windows.artifacts | Where-Object { $_.id -eq 'windows-replay' }
$requiredExports = if ([string]::IsNullOrWhiteSpace($env:BPP_NATIVE_REQUIRED_EXPORTS)) {
    @($artifactPolicy.requiredExports)
} else {
    @($env:BPP_NATIVE_REQUIRED_EXPORTS -split '\r?\n' | Where-Object { $_ })
}
if ($requiredExports.Count -eq 0) {
    throw 'BPP_NATIVE_REQUIRED_EXPORTS is required.'
}
$exportOutput = cmd.exe /d /s /c ('call "{0}" && dumpbin /exports "{1}"' -f $vcvars, $binary)
if ($LASTEXITCODE -ne 0) {
    throw 'Could not inspect native Windows exports.'
}
$actualExports = @(
    $exportOutput | ForEach-Object {
        if ($_ -match '^\s+\d+\s+[0-9A-Fa-f]+\s+[0-9A-Fa-f]+\s+(\S+)\s*$') {
            $Matches[1]
        }
    } | Sort-Object -Unique
)
$exportDiff = @(Compare-Object ($requiredExports | Sort-Object -Unique) $actualExports)
if ($exportDiff.Count -ne 0) {
    throw "Native Windows export set does not match the ABI contract:`n$($exportDiff | Out-String)"
}

$allowedDependencies = if ([string]::IsNullOrWhiteSpace($env:BPP_NATIVE_ALLOWED_DEPENDENCIES)) {
    @($artifactPolicy.allowedDependencies)
} else {
    @($env:BPP_NATIVE_ALLOWED_DEPENDENCIES -split '\r?\n' | Where-Object { $_ })
}
if ($allowedDependencies.Count -eq 0) {
    throw 'BPP_NATIVE_ALLOWED_DEPENDENCIES is required.'
}
$dependencyOutput = cmd.exe /d /s /c ('call "{0}" && dumpbin /dependents "{1}"' -f $vcvars, $binary)
if ($LASTEXITCODE -ne 0) {
    throw 'Could not inspect native Windows dependencies.'
}
$actualDependencies = @(
    $dependencyOutput | ForEach-Object {
        if ($_ -match '^\s+([A-Za-z0-9._-]+\.dll)\s*$') {
            $Matches[1]
        }
    } | Sort-Object -Unique
)
$unexpectedDependencies = @(
    $actualDependencies | Where-Object {
        $dependency = $_
        -not ($allowedDependencies | Where-Object { $_ -ieq $dependency })
    }
)
if ($unexpectedDependencies.Count -ne 0) {
    throw "Native Windows plugin has unreviewed dependencies: $($unexpectedDependencies -join ', ')"
}

$securityModulePath = Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Security\Microsoft.PowerShell.Security.psd1'
Import-Module -Name $securityModulePath -ErrorAction Stop
$signature = Get-AuthenticodeSignature -LiteralPath $binary
if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::NotSigned) {
    throw "Windows producer output must be unsigned, got $($signature.Status)."
}

if (-not $SkipSmoke) {
    & (Join-Path $sourceDir 'test.ps1') -OutputDirectory $buildDir -VcvarsPath $vcvars -SkipNativeBuild
}

Write-Host "built and verified $binary"
