<#
.SYNOPSIS
Configures NuGet.Client build environment.

.PARAMETER CleanCache
Cleans NuGet packages cache before build

.PARAMETER Force
Switch to force installation of required tools.

.PARAMETER CI
Indicates the build script is invoked from CI

.EXAMPLE
.\configure.ps1 -cc -v
Clean repo build environment configuration

.EXAMPLE
.\configure.ps1 -v
Incremental install of build tools
#>
[CmdletBinding(SupportsShouldProcess=$True)]
Param (
    [Alias('cc')]
    [switch]$CleanCache,
    [Alias('f')]
    [switch]$Force,
    [switch]$CI
)

. "$PSScriptRoot\build\common.ps1"

Trace-Log "Configuring NuGet.Client build environment"

Update-SubModules -Force:$Force

Trace-Log "Installing NuGet.exe"
Install-NuGet -Force:$Force

Trace-Log "Installing .NET CLI"
Install-DotnetCLI -Force:$Force

$ConfigureObject = @{
    BuildTools = @{}
    Toolsets = @{}
}

if ($CleanCache) {
    Clear-PackageCache
}

Function Initialize-BuildToolset {
    param([int]$ToolsetVersion)
    $CommonToolsVar = "Env:VS${ToolsetVersion}0COMNTOOLS"
    if (Test-Path $CommonToolsVar) {
        $CommonToolsValue = gci $CommonToolsVar | select -expand value -ea Ignore
        Verbose-Log "Using environment variable `"$CommonToolsVar`" = `"$CommonToolsValue`""
        $ToolsetObject = @{
            VisualStudioInstallDir = [System.IO.Path]::GetFullPath((Join-Path $CommonToolsValue '..\IDE'))
        }
    }

    if (-not $ToolsetObject) {
        $VisualStudioRegistryKey = "HKCU:\SOFTWARE\Microsoft\VisualStudio\${ToolsetVersion}.0_Config"
        if (Test-Path $VisualStudioRegistryKey) {
            Verbose-Log "Retrieving Visual Studio installation path from registry '$VisualStudioRegistryKey'"
            $ToolsetObject = @{
                VisualStudioInstallDir = gp $VisualStudioRegistryKey | select -expand InstallDir -ea Ignore
            }
        }
    }

    if (-not $ToolsetObject) {
        $WillowInstance = Get-ChildItem $env:ProgramData\Microsoft\VisualStudio\Packages\_Instances -filter state.json -recurse |
            sort LastWriteTime |
            select -last 1 |
            Get-Content -raw |
            ConvertFrom-Json

        if ($WillowInstance) {
            Verbose-Log "Using willow instance '$($WillowInstance.installationName)' installation path"
            $ToolsetObject = @{
                VisualStudioInstallDir = [System.IO.Path]::GetFullPath((Join-Path $WillowInstance.installationPath Common7\IDE\))
            }
        }
    }

    if (-not $ToolsetObject) {
        $DefaultInstallDir = Join-Path $env:ProgramFiles "Microsoft Visual Studio ${ToolsetVersion}.0\Common7\IDE\"
        if (Test-Path $DefaultInstallDir) {
            Verbose-Log "Using default location of Visual Studio installation path"
            $ToolsetObject = @{
                $VisualStudioInstallDir = $DefaultInstallDir
            }
        }
    }

    # return toolset build configuration object
    $ToolsetObject
}

$MSBuildDefaultRoot = Join-Path ${env:ProgramFiles(x86)} MSBuild
$MSBuildRelativePath = 'bin\msbuild.exe'

Trace-Log "Validating VS14 toolset installation"
$vs14 = Initialize-BuildToolset 14
if ($vs14) {
    $ConfigureObject.Toolsets.Add('vs14', $vs14)
    $MSBuildExe = Join-Path $MSBuildDefaultRoot "14.0\${MSBuildRelativePath}"
}

Trace-Log "Validating VS15 toolset installation"
$vs15 = Initialize-BuildToolset 15
if ($vs15) {
    $ConfigureObject.Toolsets.Add('vs15', $vs15)
    $WillowMSBuild = Join-Path $vs15.VisualStudioInstallDir ..\..\MSBuild
    $MSBuildExe = switch (Test-Path $WillowMSBuild) {
        $True { Join-Path $WillowMSBuild "15.0\${MSBuildRelativePath}" }
        $False { Join-Path $$MSBuildDefaultRoot "15.0\${MSBuildRelativePath}" }
    }

    # Hack VSSDK path
    $VSToolsPath = Join-Path $MSBuildDefaultRoot 'Microsoft\VisualStudio\v15.0'
    $Targets = Join-Path $VSToolsPath 'VSSDK\Microsoft.VsSDK.targets'
    if (-not (Test-Path $Targets)) {
        Warning-Log "VSSDK is not found at default location '$VSToolsPath'. Attempting to override."
        # Attempting to fix VS SDK path for VS15 willow install builds
        # as MSBUILD failes to resolve it correctly
        $VSToolsPath = Join-Path $vs15.VisualStudioInstallDir '..\..\MSBuild\Microsoft\VisualStudio\v15.0' -Resolve
        $ConfigureObject.Add('EnvVars', @{ VSToolsPath = $VSToolsPath })
    }
}

if ($MSBuildExe) {
    $MSBuildExe = [System.IO.Path]::GetFullPath($MSBuildExe)
    $MSBuildVersion = & $MSBuildExe '/version' '/nologo'
    Trace-Log "Using MSBUILD version $MSBuildVersion '$MSBuildExe'"
    $ConfigureObject.BuildTools.Add('MSBuildExe', $MSBuildExe)
}

New-Item $Artifacts -ItemType Directory -ea Ignore | Out-Null
$ConfigureObject | ConvertTo-Json | Set-Content $ConfigureJson

Trace-Log "Configuration data has been written to '$ConfigureJson'"