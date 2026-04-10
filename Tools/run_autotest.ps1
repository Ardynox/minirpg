param(
	[Parameter(Mandatory = $true)]
	[string]$GodotExe,

	[switch]$Headless,

	[string]$Scenarios,

	[double]$StepDelay
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $GodotExe)) {
	throw "Godot executable not found: $GodotExe"
}

$projectPath = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$arguments = [System.Collections.Generic.List[string]]::new()

if ($Headless) {
	[void]$arguments.Add('--headless')
}

[void]$arguments.Add('--path')
[void]$arguments.Add($projectPath)
[void]$arguments.Add('--')
[void]$arguments.Add('--autotest')

if (-not [string]::IsNullOrWhiteSpace($Scenarios)) {
	[void]$arguments.Add("--autotest-scenarios=$Scenarios")
}

if ($PSBoundParameters.ContainsKey('StepDelay')) {
	$invariantDelay = $StepDelay.ToString([System.Globalization.CultureInfo]::InvariantCulture)
	[void]$arguments.Add("--autotest-step-delay=$invariantDelay")
}

& $GodotExe @arguments
$exitCode = $LASTEXITCODE

$logPath = Join-Path $env:APPDATA 'Godot\app_userdata\MiniRPG\test_results.log'
Write-Host "AutoTest log: $logPath"

exit $exitCode
