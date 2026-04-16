param(
	[string]$RulePath = "Data/pz_tile_catalog_rules.json",
	[string]$OutputPath = "",
	[switch]$PreserveExistingCatalogMetadata = $false
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-NormalizedRelativePath {
	param([string]$Path)

	if ([string]::IsNullOrWhiteSpace($Path)) {
		return ""
	}

	return $Path.Trim().Replace('\', '/').TrimStart('/')
}

function ConvertTo-Hashtable {
	param($InputObject)

	if ($null -eq $InputObject) {
		return $null
	}

	if ($InputObject -is [System.Collections.IDictionary]) {
		$result = @{}
		foreach ($key in $InputObject.Keys) {
			$result[$key] = ConvertTo-Hashtable $InputObject[$key]
		}
		return $result
	}

	if ($InputObject -is [System.Collections.IEnumerable] -and $InputObject -isnot [string]) {
		$result = @()
		foreach ($item in $InputObject) {
			$result += ,(ConvertTo-Hashtable $item)
		}
		return $result
	}

	if ($InputObject -is [pscustomobject]) {
		$result = @{}
		foreach ($property in $InputObject.PSObject.Properties) {
			$result[$property.Name] = ConvertTo-Hashtable $property.Value
		}
		return $result
	}

	return $InputObject
}

function Convert-ToEnglishTitle {
	param([string]$Value)

	if ([string]::IsNullOrWhiteSpace($Value)) {
		return ""
	}

	$normalized = [System.IO.Path]::GetFileNameWithoutExtension($Value.Trim())
	$normalized = $normalized -creplace '([a-z])([A-Z])', '$1 $2'
	$normalized = $normalized.Replace('-', '_')
	$parts = $normalized.Split('_', [System.StringSplitOptions]::RemoveEmptyEntries)
	$textInfo = [System.Globalization.CultureInfo]::InvariantCulture.TextInfo
	$words = foreach ($part in $parts) {
		if ($part -match '^\d+$') {
			$part
		}
		else {
			$textInfo.ToTitleCase($part.ToLowerInvariant())
		}
	}

	return ($words -join ' ').Trim()
}

function Convert-ToId {
	param([string]$RelativePath)

	$raw = [System.IO.Path]::ChangeExtension((Get-NormalizedRelativePath $RelativePath), $null)
	$raw = $raw.ToLowerInvariant().Replace('/', '__')
	return ($raw -replace '[^a-z0-9_]', '_').Trim('_')
}

function Get-SequenceInfo {
	param([string]$FileName)

	$baseName = [System.IO.Path]::GetFileNameWithoutExtension($FileName)
	if ($baseName -match '^(.*)_([0-9]+)$') {
		return @{
			Base = $matches[1]
			Index = $matches[2]
		}
	}

	return @{
		Base = $baseName
		Index = $null
	}
}

function Get-TokenTags {
	param([string[]]$Values)

	$tags = New-Object System.Collections.Generic.List[string]
	foreach ($value in $Values) {
		if ([string]::IsNullOrWhiteSpace($value)) {
			continue
		}

		$normalized = $value.Trim().Replace('\', '/')
		$tags.Add($normalized.ToLowerInvariant())
		foreach ($token in ($normalized -replace '[^A-Za-z0-9_]', '_').Split('_', [System.StringSplitOptions]::RemoveEmptyEntries)) {
			$tokenValue = $token.ToLowerInvariant()
			if ($tokenValue.Length -ge 2 -and -not ($tokenValue -match '^\d+$')) {
				$tags.Add($tokenValue)
			}
		}
	}

	return $tags
}

function Merge-UniqueStrings {
	param([object[]]$Sources)

	$result = New-Object System.Collections.Generic.List[string]
	foreach ($source in $Sources) {
		if ($null -eq $source) {
			continue
		}

		foreach ($value in @($source)) {
			if ([string]::IsNullOrWhiteSpace([string]$value)) {
				continue
			}

			$result.Add(([string]$value).Trim())
		}
	}

	return $result.ToArray() | Sort-Object -Unique
}

function Get-MatchedDirectoryRule {
	param(
		[hashtable[]]$Rules,
		[string]$Directory
	)

	$normalizedDirectory = Get-NormalizedRelativePath $Directory
	foreach ($rule in $Rules) {
		if ((Get-NormalizedRelativePath ([string]$rule.directory)) -ieq $normalizedDirectory) {
			return $rule
		}
	}

	return $null
}

function Get-MatchedPrefixRule {
	param(
		[hashtable[]]$Rules,
		[string]$Directory,
		[string]$FileName,
		[string]$BaseName
	)

	$normalizedDirectory = Get-NormalizedRelativePath $Directory
	foreach ($rule in $Rules) {
		$ruleDirectory = if ($rule.ContainsKey("directory")) { Get-NormalizedRelativePath ([string]$rule.directory) } else { "" }
		if (-not [string]::IsNullOrWhiteSpace($ruleDirectory) -and $ruleDirectory -ine $normalizedDirectory) {
			continue
		}

		$prefix = [string]$rule.prefix
		if ([string]::IsNullOrWhiteSpace($prefix)) {
			continue
		}

		if ($FileName.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase) -or
			$BaseName.StartsWith($prefix.TrimEnd('_'), [System.StringComparison]::OrdinalIgnoreCase)) {
			return $rule
		}
	}

	return $null
}

function Get-OverrideMap {
	param([hashtable]$Rules)

	$overrideMap = @{}
	if ($Rules.ContainsKey("fileOverrides")) {
		foreach ($override in $Rules.fileOverrides) {
			$relativePath = Get-NormalizedRelativePath ([string]$override.path)
			$overrideMap[$relativePath] = $override
		}
	}

	return $overrideMap
}

function Get-ExistingCatalogMap {
	param([string]$OutputFilePath)

	$map = @{}
	if (-not (Test-Path $OutputFilePath)) {
		return $map
	}

	try {
		$existing = ConvertTo-Hashtable (Get-Content -Raw -Encoding utf8 $OutputFilePath | ConvertFrom-Json)
	}
	catch {
		return $map
	}
	if ($null -eq $existing -or -not $existing.ContainsKey("entries")) {
		return $map
	}

	foreach ($entry in $existing.entries) {
		$path = [string]$entry.path
		if ([string]::IsNullOrWhiteSpace($path)) {
			continue
		}

		$relativePath = $path.Replace('res://Assets/Art/PZ_Tiles/', '')
		$relativePath = Get-NormalizedRelativePath $relativePath
		$map[$relativePath] = $entry
	}

	return $map
}

function Get-ResolvedListValue {
	param(
		$FileOverride,
		$PrefixRule,
		$DirectoryRule,
		$ExistingEntry,
		[string]$Key,
		[object[]]$Fallback = @()
	)

	foreach ($source in @($FileOverride, $PrefixRule, $DirectoryRule, $ExistingEntry)) {
		if ($null -eq $source) {
			continue
		}

		if ($source.ContainsKey($Key) -and $null -ne $source[$Key]) {
			return Merge-UniqueStrings @($source[$Key])
		}
	}

	return Merge-UniqueStrings @($Fallback)
}

function Get-ResolvedStringValue {
	param(
		$FileOverride,
		$PrefixRule,
		$DirectoryRule,
		$ExistingEntry,
		[string]$Key,
		[string]$Fallback = ""
	)

	foreach ($source in @($FileOverride, $PrefixRule, $DirectoryRule, $ExistingEntry)) {
		if ($null -eq $source) {
			continue
		}

		if ($source.ContainsKey($Key) -and -not [string]::IsNullOrWhiteSpace([string]$source[$Key])) {
			return ([string]$source[$Key]).Trim()
		}
	}

	return $Fallback
}

function Resolve-UsageDomains {
	param(
		[string]$RelativeDirectory,
		[string]$Kind,
		[string[]]$Tags
	)

	$dir = $RelativeDirectory.ToLowerInvariant()
	switch -Regex ($dir) {
		'^floors' { return @('terrain') }
		'^overlays$' { return @('decor') }
		'^erosion$' { return @('decor') }
		'^fire_0[1-3]$' { return @('fx') }
		'^food_0[1-2]$' { return @('itemWorld') }
		'^weapons_01$' { return @('itemWorld') }
		'^trash_01$' { return @('decor', 'itemWorld') }
		'^stashes_01$' { return @('itemWorld', 'decor') }
		'^appliances_misc_01$' { return @('itemWorld', 'fixture') }
		'^security_01$' { return @('fixture') }
		'^recreational_01$' { return @('fixture') }
		'^carpentry_01$' { return @('fixture') }
		'^constructedobjects_01$' { return @('fixture') }
		'^furniture_' { return @('fixture') }
		'^fixtures_' { return @('fixture') }
		'^fencing_' { return @('fixture') }
		'^lighting_' { return @('fixture') }
		'^street_' { return @('fixture', 'decor') }
		'^walls_' { return @('fixture') }
		'^roofs_' { return @('fixture') }
		'^vegetation_' { return @('decor') }
		'^jumbo_trees$' { return @('decor') }
		default {
			if ($Kind -eq 'overlay') {
				return @('decor')
			}
			if ($Tags -contains 'terrain') {
				return @('terrain')
			}
			return @()
		}
	}
}

function Resolve-UsageRoles {
	param(
		[string]$RelativeDirectory,
		[string]$Kind,
		[string[]]$Tags
	)

	$dir = $RelativeDirectory.ToLowerInvariant()
	switch -Regex ($dir) {
		'^floors' { return @('top') }
		'^overlays$' { return @('overlay') }
		'^erosion$' { return @('overlay', 'ambient') }
		'^fire_0[1-3]$' { return @('fire_anim') }
		'^food_0[1-2]$' { return @('drop', 'prop') }
		'^weapons_01$' { return @('drop', 'prop') }
		'^trash_01$' { return @('drop', 'ambient') }
		'^stashes_01$' { return @('drop', 'prop') }
		'^security_01$' { return @('prop') }
		'^carpentry_01$' { return @('prop') }
		'^constructedobjects_01$' { return @('prop') }
		'^furniture_' { return @('prop') }
		'^fixtures_' { return @('prop') }
		'^fencing_' { return @('prop') }
		'^lighting_' { return @('prop', 'ambient') }
		'^street_' { return @('prop', 'ambient') }
		'^walls_' { return @('prop') }
		'^roofs_' { return @('prop') }
		'^vegetation_' { return @('ambient') }
		'^jumbo_trees$' { return @('ambient') }
		default {
			if ($Kind -eq 'overlay') {
				return @('overlay')
			}
			return @()
		}
	}
}

function Resolve-Placement {
	param(
		[string]$RelativeDirectory,
		[string]$Kind
	)

	$dir = $RelativeDirectory.ToLowerInvariant()
	if ($dir -match '^roofs_') { return 'roof' }
	if ($dir -match '^walls_' -or $dir -match '^security_' -or $dir -match '^fixtures_windows_' -or $dir -match '^fixtures_doors_' -or $dir -match '^lighting_indoor_') { return 'wall' }
	if ($dir -match '^floors' -or $dir -match '^overlays$' -or $dir -match '^erosion$' -or $dir -match '^trash_01$' -or $dir -match '^street_') { return 'floor' }
	if ($dir -match '^vegetation_' -or $dir -match '^jumbo_trees$') { return 'mixed' }
	if ($Kind -eq 'iso_tile') { return 'floor' }
	if ($Kind -eq 'overlay') { return 'mixed' }
	return 'object'
}

function Resolve-VariantGroup {
	param(
		[string]$RelativeDirectory,
		[string]$BaseName
	)

	$dir = $RelativeDirectory.ToLowerInvariant()
	switch ($dir) {
		'fire_01' { return 'fire_small' }
		'fire_02' { return 'fire_medium' }
		'fire_03' { return 'fire_large' }
		default {
			$variantBase = $BaseName -replace '_[0-9]+$', ''
			$variantBase = $variantBase -replace '_MIRRORED$', ''
			$variantBase = $variantBase -replace '_LIGHT$', ''
			$variantBase = $variantBase.Trim('_')
			if (-not [string]::IsNullOrWhiteSpace($variantBase) -and $variantBase -ine $BaseName) {
				return $variantBase.ToLowerInvariant()
			}
			if (-not [string]::IsNullOrWhiteSpace($dir)) {
				return $dir.Replace('/', '_')
			}
			return ''
		}
	}
}

function Get-ResolvedName {
	param(
		[string]$ExactName,
		[string]$BaseName,
		[string]$Sequence,
		[scriptblock]$FallbackBuilder
	)

	if (-not [string]::IsNullOrWhiteSpace($ExactName)) {
		return $ExactName.Trim()
	}

	if (-not [string]::IsNullOrWhiteSpace($BaseName)) {
		if ([string]::IsNullOrWhiteSpace($Sequence)) {
			return $BaseName.Trim()
		}

		return "{0} {1}" -f $BaseName.Trim(), $Sequence
	}

	return & $FallbackBuilder
}

$repoRoot = (Resolve-Path ".").Path
$ruleFilePath = Join-Path $repoRoot $RulePath
if (-not (Test-Path $ruleFilePath)) {
	throw "Rule file not found: $ruleFilePath"
}

$rules = ConvertTo-Hashtable (Get-Content -Raw -Encoding utf8 $ruleFilePath | ConvertFrom-Json)
$sourceRoot = Join-Path $repoRoot ([string]$rules.sourceRoot)
$resolvedOutputPath = if ([string]::IsNullOrWhiteSpace($OutputPath)) {
	Join-Path $repoRoot ([string]$rules.outputPath)
}
else {
	Join-Path $repoRoot $OutputPath
}

if (-not (Test-Path $sourceRoot)) {
	throw "Source root not found: $sourceRoot"
}

$directoryRules = @($rules.directoryRules)
$prefixRules = @($rules.prefixRules | Sort-Object { [string]$_.prefix } -Descending)
$overrideMap = Get-OverrideMap -Rules $rules
$existingCatalogMap = if ($PreserveExistingCatalogMetadata) {
	Get-ExistingCatalogMap -OutputFilePath $resolvedOutputPath
}
else {
	@{}
}

$entries = New-Object System.Collections.Generic.List[object]
$sourceRootPrefix = (Resolve-Path $sourceRoot).Path.TrimEnd('\', '/')

foreach ($file in (Get-ChildItem $sourceRoot -Recurse -File -Filter *.png | Sort-Object FullName)) {
	$relativePath = Get-NormalizedRelativePath ($file.FullName.Substring($sourceRootPrefix.Length).TrimStart('\', '/'))
	$relativeDirectory = Get-NormalizedRelativePath ([System.IO.Path]::GetDirectoryName($relativePath))
	$sequenceInfo = Get-SequenceInfo -FileName $file.Name
	$baseName = [string]$sequenceInfo.Base
	$sequence = [string]$sequenceInfo.Index

	$directoryRule = Get-MatchedDirectoryRule -Rules $directoryRules -Directory $relativeDirectory
	$prefixRule = Get-MatchedPrefixRule -Rules $prefixRules -Directory $relativeDirectory -FileName $file.Name -BaseName $baseName
	$fileOverride = if ($overrideMap.ContainsKey($relativePath)) { $overrideMap[$relativePath] } else { $null }
	$existingEntry = if ($existingCatalogMap.ContainsKey($relativePath)) { $existingCatalogMap[$relativePath] } else { $null }

	$group = Get-ResolvedStringValue `
		-FileOverride $fileOverride `
		-PrefixRule $prefixRule `
		-DirectoryRule $directoryRule `
		-ExistingEntry $existingEntry `
		-Key 'group' `
		-Fallback (Convert-ToEnglishTitle ($relativeDirectory -replace '/', '_'))

	$displayNameZh = Get-ResolvedName `
		-ExactName (Get-ResolvedStringValue -FileOverride $fileOverride -PrefixRule $null -DirectoryRule $null -ExistingEntry $existingEntry -Key 'displayNameZh') `
		-BaseName (Get-ResolvedStringValue -FileOverride $null -PrefixRule $prefixRule -DirectoryRule $directoryRule -ExistingEntry $null -Key 'displayNameZhBase') `
		-Sequence $sequence `
		-FallbackBuilder { Get-ResolvedName -ExactName "" -BaseName (Convert-ToEnglishTitle $baseName) -Sequence $sequence -FallbackBuilder { Convert-ToEnglishTitle $file.Name } }

	$displayNameEn = Get-ResolvedName `
		-ExactName (Get-ResolvedStringValue -FileOverride $fileOverride -PrefixRule $null -DirectoryRule $null -ExistingEntry $existingEntry -Key 'displayNameEn') `
		-BaseName (Get-ResolvedStringValue -FileOverride $null -PrefixRule $prefixRule -DirectoryRule $directoryRule -ExistingEntry $null -Key 'displayNameEnBase') `
		-Sequence $sequence `
		-FallbackBuilder { Get-ResolvedName -ExactName "" -BaseName (Convert-ToEnglishTitle $baseName) -Sequence $sequence -FallbackBuilder { Convert-ToEnglishTitle $file.Name } }

	$kind = Get-ResolvedStringValue `
		-FileOverride $fileOverride `
		-PrefixRule $prefixRule `
		-DirectoryRule $directoryRule `
		-ExistingEntry $existingEntry `
		-Key 'kind' `
		-Fallback 'sprite'

	$confidence = Get-ResolvedStringValue `
		-FileOverride $fileOverride `
		-PrefixRule $prefixRule `
		-DirectoryRule $directoryRule `
		-ExistingEntry $existingEntry `
		-Key 'confidence' `
		-Fallback 'medium'

	$mappingEligibleValue = $null
	foreach ($source in @($fileOverride, $prefixRule, $directoryRule, $existingEntry)) {
		if ($null -eq $source) {
			continue
		}

		if ($source.ContainsKey('mappingEligible')) {
			$mappingEligibleValue = [bool]$source.mappingEligible
			break
		}
	}
	$mappingEligible = if ($null -ne $mappingEligibleValue) { $mappingEligibleValue } else { $false }

	$derivedTags = Get-TokenTags @($relativeDirectory, $baseName, $file.Name)
	$tags = Merge-UniqueStrings @(
		$(if ($existingEntry -and $existingEntry.ContainsKey("tags")) { $existingEntry.tags } else { @() }),
		$(if ($directoryRule) { $directoryRule.tags } else { @() }),
		$(if ($prefixRule) { $prefixRule.tags } else { @() }),
		$(if ($fileOverride -and $fileOverride.ContainsKey("tags")) { $fileOverride.tags } else { @() }),
		$derivedTags
	) | ForEach-Object { ([string]$_).ToLowerInvariant() }

	$usageDomains = Get-ResolvedListValue `
		-FileOverride $fileOverride `
		-PrefixRule $prefixRule `
		-DirectoryRule $directoryRule `
		-ExistingEntry $existingEntry `
		-Key 'usageDomains' `
		-Fallback (Resolve-UsageDomains -RelativeDirectory $relativeDirectory -Kind $kind -Tags $tags)
	$usageRoles = Get-ResolvedListValue `
		-FileOverride $fileOverride `
		-PrefixRule $prefixRule `
		-DirectoryRule $directoryRule `
		-ExistingEntry $existingEntry `
		-Key 'usageRoles' `
		-Fallback (Resolve-UsageRoles -RelativeDirectory $relativeDirectory -Kind $kind -Tags $tags)
	$placement = Get-ResolvedStringValue `
		-FileOverride $fileOverride `
		-PrefixRule $prefixRule `
		-DirectoryRule $directoryRule `
		-ExistingEntry $existingEntry `
		-Key 'placement' `
		-Fallback (Resolve-Placement -RelativeDirectory $relativeDirectory -Kind $kind)
	$variantGroup = Get-ResolvedStringValue `
		-FileOverride $fileOverride `
		-PrefixRule $prefixRule `
		-DirectoryRule $directoryRule `
		-ExistingEntry $existingEntry `
		-Key 'variantGroup' `
		-Fallback (Resolve-VariantGroup -RelativeDirectory $relativeDirectory -BaseName $baseName)

	$entries.Add([ordered]@{
		id = Convert-ToId $relativePath
		path = "res://Assets/Art/PZ_Tiles/$relativePath"
		group = $group
		originalFileName = $file.Name
		displayNameZh = $displayNameZh
		displayNameEn = $displayNameEn
		tags = @($tags | Sort-Object -Unique)
		usageDomains = @($usageDomains)
		usageRoles = @($usageRoles)
		placement = $placement
		variantGroup = $variantGroup
		kind = $kind
		confidence = $confidence
		mappingEligible = $mappingEligible
	})
}

$document = @{ entries = $entries.ToArray() }
$outputDirectory = Split-Path $resolvedOutputPath -Parent
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
	New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
}

$document | ConvertTo-Json -Depth 24 | Set-Content -Path $resolvedOutputPath -Encoding UTF8
Write-Output ("Generated {0} catalog entries at {1}" -f $entries.Count, $resolvedOutputPath)
