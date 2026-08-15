param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'build'),
    [string]$VcvarsPath,
    [switch]$SkipNativeBuild
)

$ErrorActionPreference = 'Stop'

if (-not $SkipNativeBuild) {
    & (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $OutputDirectory -SkipSmoke
}

$vcvars = $VcvarsPath
if ([string]::IsNullOrWhiteSpace($vcvars)) {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    $install = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    $vcvars = Join-Path $install 'VC\Auxiliary\Build\vcvars64.bat'
}
$buildDir = $OutputDirectory
$command = 'call "{0}" && cl /nologo /std:c++17 /utf-8 /EHsc /W4 /WX /O2 /MT /DNOMINMAX /DUNICODE /D_UNICODE "{1}\BppReplayMediaFoundationSmoke.cpp" /link /OUT:"{2}\BppReplayMediaFoundationSmoke.exe" /LIBPATH:"{2}" BppReplayMediaFoundation.lib d3d11.lib' -f $vcvars, $PSScriptRoot, $buildDir
cmd.exe /d /s /c $command
if ($LASTEXITCODE -ne 0) {
    throw "Native smoke test build failed with exit code $LASTEXITCODE."
}

& (Join-Path $buildDir 'BppReplayMediaFoundationSmoke.exe')
if ($LASTEXITCODE -ne 0) {
    throw "Native smoke test failed with exit code $LASTEXITCODE."
}
