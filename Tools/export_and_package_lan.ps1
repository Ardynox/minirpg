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

$godotExportFallbackDir = Join-Path $projectRoot 'Build/_godot_export'
if (-not (Test-Path -LiteralPath $godotExportFallbackDir)) {
	New-Item -ItemType Directory -Path $godotExportFallbackDir | Out-Null
}

function Ensure-ExportTemplates {
	param([string]$PresetFile)

	$content = Get-Content -LiteralPath $PresetFile -Raw
	if ($content -match 'custom_template/debug="[^"]+\.exe"') {
		return $false  # already configured
	}

	$templateBase = Join-Path $env:APPDATA 'Godot\export_templates'
	if (-not (Test-Path -LiteralPath $templateBase)) {
		throw "Godot export templates directory not found: $templateBase"
	}

	$templateDirs = Get-ChildItem -LiteralPath $templateBase -Directory -ErrorAction SilentlyContinue |
		Sort-Object LastWriteTime -Descending

	foreach ($dir in $templateDirs) {
		$debugTpl = Join-Path $dir.FullName 'windows_debug_x86_64.exe'
		$releaseTpl = Join-Path $dir.FullName 'windows_release_x86_64.exe'

		if ((Test-Path -LiteralPath $debugTpl) -and (Test-Path -LiteralPath $releaseTpl)) {
			Write-Host "[templates] Auto-detected export templates in $($dir.FullName)"
			$debugTplFwd = $debugTpl.Replace('\', '/')
			$releaseTplFwd = $releaseTpl.Replace('\', '/')
			$content = $content.Replace('custom_template/debug=""', "custom_template/debug=`"$debugTplFwd`"")
			$content = $content.Replace('custom_template/release=""', "custom_template/release=`"$releaseTplFwd`"")
			Set-Content -LiteralPath $PresetFile -Value $content -NoNewline
			return $true
		}
	}

	throw "No Windows export templates (windows_debug_x86_64.exe / windows_release_x86_64.exe) found in $templateBase"
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
$clientDebugDir = Join-Path $clientDir 'Debug'
$clientReleaseDir = Join-Path $clientDir 'Release'
$serverDir = Join-Path $packageDir 'Server'

if (Test-Path -LiteralPath $packageDir) {
	Remove-Item -LiteralPath $packageDir -Recurse -Force
}

New-Item -ItemType Directory -Path $clientDir | Out-Null
New-Item -ItemType Directory -Path $clientDebugDir | Out-Null
New-Item -ItemType Directory -Path $clientReleaseDir | Out-Null
New-Item -ItemType Directory -Path $serverDir | Out-Null

function Find-ExportPairInDir {
	param(
		[string]$Dir,
		[string]$BuildFlavor,
		[switch]$Recurse
	)

	if (-not (Test-Path -LiteralPath $Dir)) {
		return $null
	}

	$searchParams = @{
		LiteralPath = $Dir
		File = $true
		ErrorAction = 'SilentlyContinue'
	}
	if ($Recurse) {
		$searchParams.Recurse = $true
	}

	$allFiles = Get-ChildItem @searchParams
	$exeCandidates = $allFiles |
		Where-Object { $_.Extension -ieq '.exe' -and $_.BaseName -like 'MiniRPG*' } |
		Sort-Object LastWriteTime -Descending
	$pckCandidates = $allFiles |
		Where-Object { $_.Extension -ieq '.pck' -and $_.BaseName -like 'MiniRPG*' } |
		Sort-Object LastWriteTime -Descending

	if ($exeCandidates.Count -eq 0 -or $pckCandidates.Count -eq 0) {
		return $null
	}

	foreach ($exe in $exeCandidates) {
		$exactPck = Join-Path $exe.DirectoryName ($exe.BaseName + '.pck')
		if (Test-Path -LiteralPath $exactPck) {
			return [PSCustomObject]@{
				ExePath = $exe.FullName
				PckPath = $exactPck
				SourceDir = $exe.DirectoryName
			}
		}
	}

	# Fallback: allow exe/pck with different suffixes and use newest files.
	return [PSCustomObject]@{
		ExePath = $exeCandidates[0].FullName
		PckPath = $pckCandidates[0].FullName
		SourceDir = $exeCandidates[0].DirectoryName
	}
}

function Move-ExportOutputIfNeeded {
	param(
		[string]$TargetExePath,
		[string]$OutputDir,
		[string]$BuildFlavor
	)

	if (Test-Path -LiteralPath $TargetExePath) {
		return
	}
	Write-Warning "Godot did not write export to requested path: $TargetExePath"

	$canonicalExe = Join-Path $OutputDir 'MiniRPG.exe'
	$canonicalPck = Join-Path $OutputDir 'MiniRPG.pck'

	# Godot may still export into OutputDir but with suffixes like MiniRPG-Debug.exe / MiniRPG-Release.exe.
	$pairInOutput = Find-ExportPairInDir -Dir $OutputDir -BuildFlavor $BuildFlavor -Recurse
	if ($null -ne $pairInOutput) {
		if (-not ($pairInOutput.ExePath -ieq $canonicalExe)) {
			Move-Item -LiteralPath $pairInOutput.ExePath -Destination $canonicalExe -Force
		}
		if (-not ($pairInOutput.PckPath -ieq $canonicalPck)) {
			Move-Item -LiteralPath $pairInOutput.PckPath -Destination $canonicalPck -Force
		}
		return
	}

	$fallbackRoots = @(
		(Resolve-Path (Join-Path $projectRoot 'Build/_godot_export') -ErrorAction SilentlyContinue),
		(Resolve-Path (Join-Path $projectRoot '..\minirpg') -ErrorAction SilentlyContinue)
	) | Where-Object { $null -ne $_ }

	foreach ($fallbackRoot in $fallbackRoots) {
		$fallbackDir = $fallbackRoot.Path

		# Fast path for current known export names.
		$knownPairs = @(
			@{ Exe = (Join-Path $fallbackDir 'MiniRPG-Debug.exe');   Pck = (Join-Path $fallbackDir 'MiniRPG-Debug.pck') },
			@{ Exe = (Join-Path $fallbackDir 'MiniRPG-Release.exe'); Pck = (Join-Path $fallbackDir 'MiniRPG-Release.pck') },
			@{ Exe = (Join-Path $fallbackDir 'MiniRPG.exe');         Pck = (Join-Path $fallbackDir 'MiniRPG.pck') }
		)

		$knownHit = $knownPairs | Where-Object { (Test-Path -LiteralPath $_.Exe) -and (Test-Path -LiteralPath $_.Pck) } | Select-Object -First 1
		if ($null -ne $knownHit) {
			Write-Host "[client] Godot exported to $fallbackDir, moving files to package/$BuildFlavor"
			Move-Item -LiteralPath $knownHit.Exe -Destination $canonicalExe -Force
			Move-Item -LiteralPath $knownHit.Pck -Destination $canonicalPck -Force

			Get-ChildItem -LiteralPath $fallbackDir -Directory -ErrorAction SilentlyContinue |
				Where-Object { $_.Name -like 'data_MiniRPG*' } |
				ForEach-Object {
					Move-Item -LiteralPath $_.FullName -Destination (Join-Path $OutputDir $_.Name) -Force
				}
			return
		}

		$fallbackPair = Find-ExportPairInDir -Dir $fallbackDir -BuildFlavor $BuildFlavor -Recurse
		if ($null -eq $fallbackPair) {
			continue
		}

		Write-Host "[client] Godot exported to preset path ($($fallbackPair.SourceDir)), moving files to package/$BuildFlavor"
		Move-Item -LiteralPath $fallbackPair.ExePath -Destination $canonicalExe -Force
		Move-Item -LiteralPath $fallbackPair.PckPath -Destination $canonicalPck -Force

		Get-ChildItem -LiteralPath $fallbackPair.SourceDir -Directory -ErrorAction SilentlyContinue |
			Where-Object { $_.Name -like 'data_MiniRPG*' } |
			ForEach-Object {
				Move-Item -LiteralPath $_.FullName -Destination (Join-Path $OutputDir $_.Name) -Force
			}
		return
	}

	$debugDump = @(
		"Client executable not found after export: $TargetExePath"
		"Searched output dir: $OutputDir"
		"Fallback dir expected: $godotExportFallbackDir"
	)

	if (Test-Path -LiteralPath $godotExportFallbackDir) {
		$entries = Get-ChildItem -LiteralPath $godotExportFallbackDir -Force -ErrorAction SilentlyContinue |
			Select-Object -First 30 -ExpandProperty Name
		if ($entries.Count -gt 0) {
			$debugDump += "Fallback dir entries: " + ($entries -join ', ')
		} else {
			$debugDump += "Fallback dir entries: <empty>"
		}
	}

	$debugLog = Join-Path $godotExportFallbackDir 'godot-export-debug.log'
	if (Test-Path -LiteralPath $debugLog) {
		$debugDump += "godot debug log path: $debugLog"
	}

	throw ($debugDump -join [Environment]::NewLine)
}

function Assert-ClientExportBundle {
	param(
		[string]$OutputDir,
		[string]$ExeName,
		[string]$BuildFlavor
	)

	$exePath = Join-Path $OutputDir $ExeName
	Move-ExportOutputIfNeeded -TargetExePath $exePath -OutputDir $OutputDir -BuildFlavor $BuildFlavor

	if (-not (Test-Path -LiteralPath $exePath)) {
		throw "Client executable not found after export: $exePath"
	}

	$pckPath = Join-Path $OutputDir 'MiniRPG.pck'
	if (-not (Test-Path -LiteralPath $pckPath)) {
		throw "Client PCK not found after export: $pckPath"
	}

	# For dotnet/embed_build_outputs=true, data_* folder may not be generated.
	# We only enforce exe + pck existence here.
}

$resolvedGodotExe = $null
if (-not $SkipClient) {
	$resolvedGodotExe = Resolve-GodotExe -BinDir $GodotBinDir -ExplicitExe $GodotExe
	if ($resolvedGodotExe -notmatch '(?i)mono') {
		throw "Godot executable must be the Mono build to export C# data folders. Please use a mono editor from $GodotBinDir (name contains 'mono')."
	}
}

$presetFile = Join-Path $projectRoot 'export_presets.cfg'
$presetBackup = $null
$presetPatched = $false

Push-Location $projectRoot
try {
	if (-not $SkipClient) {
		# Ensure export templates are configured (custom/dev builds may not find templates by version).
		$presetBackup = Get-Content -LiteralPath $presetFile -Raw
		$presetPatched = Ensure-ExportTemplates -PresetFile $presetFile

		Write-Host "[1/4] Building client C# project..."
		dotnet build 'MiniRPG.csproj' -c $Configuration -r $Runtime
		if ($LASTEXITCODE -ne 0) { throw 'Client build failed.' }

		$clientDebugOut = Join-Path $clientDebugDir 'MiniRPG.exe'
		$clientReleaseOut = Join-Path $clientReleaseDir 'MiniRPG.exe'
		$tempDebugOut = Join-Path $godotExportFallbackDir 'MiniRPG-Debug.exe'
		$tempReleaseOut = Join-Path $godotExportFallbackDir 'MiniRPG-Release.exe'

		# Ensure target and temp parent directories exist.
		New-Item -ItemType Directory -Path (Split-Path -Parent $clientDebugOut) -Force | Out-Null
		New-Item -ItemType Directory -Path (Split-Path -Parent $clientReleaseOut) -Force | Out-Null
		New-Item -ItemType Directory -Path (Split-Path -Parent $tempDebugOut) -Force | Out-Null

		# Clean temp export remnants to avoid stale files being detected as fresh exports.
		Get-ChildItem -LiteralPath $godotExportFallbackDir -File -ErrorAction SilentlyContinue |
			Where-Object { $_.Name -like 'MiniRPG*.exe' -or $_.Name -like 'MiniRPG*.pck' -or $_.Name -like 'godot-export-*.log' } |
			Remove-Item -Force -ErrorAction SilentlyContinue
		Get-ChildItem -LiteralPath $godotExportFallbackDir -Directory -ErrorAction SilentlyContinue |
			Where-Object { $_.Name -like 'data_MiniRPG*' } |
			Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

		$debugLogPath = Join-Path $godotExportFallbackDir 'godot-export-debug.log'
		$releaseLogPath = Join-Path $godotExportFallbackDir 'godot-export-release.log'

		Write-Host "[2/4] Exporting client debug..."
		$debugArgs = "--headless --log-file `"$debugLogPath`" --path `"$projectRoot`" --export-debug `"$ClientDebugPreset`" `"$tempDebugOut`""
		$debugProc = Start-Process -FilePath $resolvedGodotExe -ArgumentList $debugArgs -Wait -PassThru -NoNewWindow -WorkingDirectory $projectRoot
		if ($debugProc.ExitCode -ne 0) { throw "Godot debug export failed. ExitCode=$($debugProc.ExitCode)" }
		Assert-ClientExportBundle -OutputDir $clientDebugDir -ExeName 'MiniRPG.exe' -BuildFlavor 'Debug'

		Write-Host "[3/4] Exporting client release..."
		$releaseArgs = "--headless --log-file `"$releaseLogPath`" --path `"$projectRoot`" --export-release `"$ClientReleasePreset`" `"$tempReleaseOut`""
		$releaseProc = Start-Process -FilePath $resolvedGodotExe -ArgumentList $releaseArgs -Wait -PassThru -NoNewWindow -WorkingDirectory $projectRoot
		if ($releaseProc.ExitCode -ne 0) { throw "Godot release export failed. ExitCode=$($releaseProc.ExitCode)" }
		Assert-ClientExportBundle -OutputDir $clientReleaseDir -ExeName 'MiniRPG.exe' -BuildFlavor 'Release'

		$debugPck = Join-Path $clientDebugDir 'MiniRPG.pck'
		$releasePck = Join-Path $clientReleaseDir 'MiniRPG.pck'
		if ((Test-Path -LiteralPath $debugPck) -and (Test-Path -LiteralPath $releasePck)) {
			$debugPckInfo = Get-Item -LiteralPath $debugPck
			$releasePckInfo = Get-Item -LiteralPath $releasePck
			if ($releasePckInfo.Length -lt 1MB -and $debugPckInfo.Length -gt 10MB) {
				Write-Warning "Release export appears invalid (tiny pck). Falling back to debug export assets for release folder."
				Copy-Item -LiteralPath (Join-Path $clientDebugDir '*') -Destination $clientReleaseDir -Recurse -Force
			}
		}
	}

	if (-not $SkipServer) {
		Write-Host "[4/4] Publishing dedicated server..."
		dotnet publish 'MiniRPG.Server/MiniRPG.Server.csproj' -c $Configuration -r $Runtime --output $serverDir
		if ($LASTEXITCODE -ne 0) { throw 'Server publish failed.' }
	}
}
finally {
	Pop-Location
	# Restore export_presets.cfg if we patched custom_template paths.
	if ($presetPatched) {
		Set-Content -LiteralPath $presetFile -Value $presetBackup -NoNewline
	}
}

$readmePath = Join-Path $packageDir 'README.txt'
$readme = @"
MiniRPG LAN package

Package folder: $packageDir
Generated at: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')

Structure:
- Client/
  - Debug/
    - MiniRPG.exe + MiniRPG.pck
  - Release/
    - MiniRPG.exe + MiniRPG.pck
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
