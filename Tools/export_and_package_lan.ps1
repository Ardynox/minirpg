param(
	[string]$GodotBinDir = 'D:\Godot\godot\bin',
	[string]$GodotExe,
	[string]$ClientDebugPreset = 'Windows Desktop',
	[string]$ClientReleasePreset = 'Windows Desktop',
	[string]$Configuration = 'Release',
	[string]$Runtime = 'win-x64',
	[switch]$SkipClient,
	[switch]$SkipServer,
	[switch]$NoTimestamp
)

$ErrorActionPreference = 'Stop'

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$buildRoot = Join-Path $projectRoot 'Build'
$packagesRoot = Join-Path $buildRoot 'Packages'

if (-not (Test-Path -LiteralPath $packagesRoot)) {
	New-Item -ItemType Directory -Path $packagesRoot | Out-Null
}

function Resolve-GodotExe {
	param(
		[string]$BinDir,
		[string]$ExplicitExe
	)

	if (-not [string]::IsNullOrWhiteSpace($ExplicitExe)) {
		if (-not (Test-Path -LiteralPath $ExplicitExe)) {
			throw "Godot executable not found: $ExplicitExe"
		}
		return (Resolve-Path $ExplicitExe).Path
	}

	if (-not (Test-Path -LiteralPath $BinDir)) {
		throw "Godot bin directory not found: $BinDir"
	}

	$candidates = @(
		'godot.windows.editor.x86_64.mono.exe',
		'godot.windows.editor.x86_64.exe',
		'Godot_v4.6-stable_mono_win64.exe',
		'Godot_v4.5-stable_mono_win64.exe',
		'Godot_v4.4-stable_mono_win64.exe'
	)

	foreach ($name in $candidates) {
		$path = Join-Path $BinDir $name
		if (Test-Path -LiteralPath $path) {
			return (Resolve-Path $path).Path
		}
	}

	$anyExe = Get-ChildItem -LiteralPath $BinDir -Filter '*.exe' -File |
		Where-Object { $_.Name -match 'godot' } |
		Select-Object -First 1

	if ($null -ne $anyExe) {
		return $anyExe.FullName
	}

	throw "No Godot executable found in: $BinDir"
}

$stamp = if ($NoTimestamp) { 'latest' } else { Get-Date -Format 'yyyyMMdd-HHmmss' }
$packageDir = Join-Path $packagesRoot "LAN-Package-$stamp"
$clientDir = Join-Path $packageDir 'Client'
$serverDir = Join-Path $packageDir 'Server'

if (Test-Path -LiteralPath $packageDir) {
	Remove-Item -LiteralPath $packageDir -Recurse -Force
}

New-Item -ItemType Directory -Path $clientDir | Out-Null
New-Item -ItemType Directory -Path $serverDir | Out-Null

$resolvedGodotExe = $null
if (-not $SkipClient) {
	$resolvedGodotExe = Resolve-GodotExe -BinDir $GodotBinDir -ExplicitExe $GodotExe
}

Push-Location $projectRoot
try {
	if (-not $SkipClient) {
		Write-Host "[1/4] Building client C# project..."
		dotnet build 'MiniRPG.csproj' -c $Configuration -r $Runtime
		if ($LASTEXITCODE -ne 0) { throw 'Client build failed.' }

		$clientDebugOut = Join-Path $clientDir 'MiniRPG-Debug.exe'
		$clientReleaseOut = Join-Path $clientDir 'MiniRPG-Release.exe'

		Write-Host "[2/4] Exporting client debug..."
		& $resolvedGodotExe --headless --path $projectRoot --export-debug $ClientDebugPreset $clientDebugOut
		if ($LASTEXITCODE -ne 0) { throw 'Godot debug export failed.' }

		Write-Host "[3/4] Exporting client release..."
		& $resolvedGodotExe --headless --path $projectRoot --export-release $ClientReleasePreset $clientReleaseOut
		if ($LASTEXITCODE -ne 0) { throw 'Godot release export failed.' }
	}

	if (-not $SkipServer) {
		Write-Host "[4/4] Publishing dedicated server..."
		dotnet publish 'MiniRPG.Server/MiniRPG.Server.csproj' -c $Configuration -r $Runtime --output $serverDir
		if ($LASTEXITCODE -ne 0) { throw 'Server publish failed.' }
	}
}
finally {
	Pop-Location
}

$readmePath = Join-Path $packageDir 'README.txt'
$readme = @"
MiniRPG LAN package

Package folder: $packageDir
Generated at: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')

Structure:
- Client/
  - MiniRPG-Debug.exe (if client export enabled)
  - MiniRPG-Release.exe (if client export enabled)
- Server/
  - MiniRPG.Server.exe (or runtime files from dotnet publish)

Server run example:
MiniRPG.Server.exe --lobby-prefix http://127.0.0.1:5076/ --game-address 0.0.0.0 --game-port 2455 --max-clients 10
"@
Set-Content -LiteralPath $readmePath -Value $readme -Encoding UTF8

Write-Host "Done. Package created: $packageDir"
if ($resolvedGodotExe) {
	Write-Host "Godot executable: $resolvedGodotExe"
}
