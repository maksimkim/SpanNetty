<#
.SYNOPSIS
This is a Powershell script to bootstrap a Fake build.
.DESCRIPTION
This Powershell script will download NuGet if missing, restore NuGet tools (including Fake)
and execute your Fake build script with the parameters you provide.
.PARAMETER Target
The build script target to run.
.PARAMETER Configuration
The build configuration to use.
.PARAMETER Verbosity
Specifies the amount of information to be displayed.
.PARAMETER WhatIf
Performs a dry run of the build script.
No tasks will be executed.
.PARAMETER ScriptArgs
Remaining arguments are added here.
#>

[CmdletBinding()]
Param(
    [string]$Target = "All",
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Debug",
    [ValidateSet("Quiet", "Minimal", "Normal", "Verbose", "Diagnostic")]
    [string]$Verbosity = "Verbose",
    [switch]$WhatIf,
    [Parameter(Position=0,Mandatory=$false,ValueFromRemainingArguments=$true)]
    [string[]]$ScriptArgs
)

$FakeVersion = "6.1.4"

$IncrementalistVersion = "0.8.0";

# Make sure tools folder exists
$PSScriptRoot = Split-Path $MyInvocation.MyCommand.Path -Parent
$ToolPath = Join-Path $PSScriptRoot "tools"
if (!(Test-Path $ToolPath)) {
    Write-Verbose "Creating tools directory..."
    New-Item -Path $ToolPath -Type directory | out-null
}

###########################################################################
# INSTALL FAKE
###########################################################################
# Make sure Fake has been installed.

$FakeToolPath = Join-Path $ToolPath "fake"
$FakeExePath = Join-Path $FakeToolPath "fake.exe"
if (!(Test-Path $FakeExePath)) {
    Write-Host "Installing fake-cli..."
    dotnet tool install fake-cli --version $FakeVersion --tool-path "$FakeToolPath"
    if ($LASTEXITCODE -ne 0) {
        Throw "An error occurred while installing fake-cli."
    }
}

###########################################################################
# Incrementalist
###########################################################################

# Make sure the Incrementalist has been installed
if (Get-Command incrementalist -ErrorAction SilentlyContinue) {
    Write-Host "Found Incrementalist. Skipping install."
}
else{
    $IncrementalistFolder = Join-Path $ToolPath "incrementalist"
    Write-Host "Incrementalist not found. Installing to ... $IncrementalistFolder"
    dotnet tool install Incrementalist.Cmd --version $IncrementalistVersion --tool-path "$IncrementalistFolder"
}

###########################################################################
# RUN BUILD SCRIPT
###########################################################################

# Use first positional argument as target if provided
if ($ScriptArgs -and $ScriptArgs.Length -gt 0) {
    $Target = $ScriptArgs[0]
}

# Start Fake
Write-Host "Running build script..."
$env:configuration = $Configuration
& $FakeExePath run "buildNetstandard.fsx" -t $Target
 
exit $LASTEXITCODE