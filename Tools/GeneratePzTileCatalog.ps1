param(
	[string]$RulePath = "Data/pz_tile_catalog_rules.json",
	[string]$OutputPath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-NormalizedRelativePath {
	param([string]$Path)

	return $Path.Trim().Replace('\', '/').TrimStart('/')
}

function Get-OrderedJson {
	param([object]$Value)

	return $Value | ConvertTo-Json -Depth 16
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
		switch ($part.ToLowerInvariant()) {
			"on" { "On" }
			"off" { "Off" }
			"jumbo" { "Jumbo" }
			default {
				if ($part -match '^\d+$') { $part }
				else { $textInfo.ToTitleCase($part.ToLowerInvariant()) }
			}
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

function Get-ZhTokenMap {
	return @{
		"appliances" = "家电"
		"bathroom" = "浴室"
		"blends" = "混合"
		"blocks" = "块墙"
		"brick" = "砖墙"
		"burnt" = "烧毁"
		"cooking" = "烹饪"
		"carpentry" = "木工"
		"clapboard" = "木挂板"
		"commercial" = "商业"
		"constructedobjects" = "建造物件"
		"construction" = "施工"
		"counters" = "柜台"
		"curbs" = "路缘"
		"debug" = "调试"
		"diner" = "餐馆"
		"doors" = "门"
		"erosion" = "侵蚀"
		"escalators" = "扶梯"
		"exterior" = "外部"
		"farm" = "农场"
		"farming" = "农业"
		"fencing" = "围栏"
		"fire" = "火焰"
		"fixtures" = "装置"
		"flatstone" = "平石"
		"floor" = "地面"
		"floors" = "地面"
		"food" = "食物"
		"furniture" = "家具"
		"generic" = "通用"
		"house" = "房屋"
		"hospitality" = "旅馆"
		"indoor" = "室内"
		"industry" = "工业"
		"interior" = "室内"
		"invisible" = "隐藏"
		"jumbo" = "巨型"
		"light" = "亮色"
		"lighting" = "照明"
		"location" = "场景"
		"mall" = "商场"
		"mirrored" = "镜像"
		"misc" = "杂项"
		"natural" = "自然"
		"newgrass" = "新草"
		"newgrassbase" = "新草基底"
		"on" = "开启"
		"ornamental" = "装饰植物"
		"outdoor" = "室外"
		"overlay" = "覆盖"
		"overlays" = "覆盖"
		"plants" = "植物"
		"railroad" = "铁路"
		"recreational" = "娱乐"
		"refrigeration" = "制冷"
		"restaurant" = "餐馆"
		"roadsigns" = "路牌"
		"roof" = "屋顶"
		"roofs" = "屋顶"
		"rugs" = "地毯"
		"security" = "安防"
		"seating" = "座椅"
		"sewer" = "下水道"
		"shop" = "商店"
		"sinks" = "水槽"
		"smooth" = "平整"
		"snow" = "积雪"
		"sports" = "运动"
		"stairs" = "楼梯"
		"stashes" = "藏匿点"
		"stone" = "石墙"
		"storage" = "储物"
		"street" = "街道"
		"streetcracks" = "街道裂缝"
		"tables" = "桌子"
		"television" = "电视"
		"tiles" = "瓷砖"
		"tilesandstone" = "砖石"
		"tilesandwood" = "砖木"
		"bedding" = "寝具"
		"trailer" = "拖车"
		"trash" = "垃圾"
		"tree" = "树"
		"trees" = "树木"
		"trucks" = "卡车"
		"vines" = "藤蔓"
		"vegetation" = "植被"
		"wall" = "墙面"
		"wallcracks" = "墙面裂缝"
		"walls" = "墙面"
		"weapons" = "武器"
		"windows" = "窗户"
		"wood" = "木质"
		"wooden" = "木质"
	}
}

function Convert-ToZhTitle {
	param([string]$Value)

	if ([string]::IsNullOrWhiteSpace($Value)) {
		return ""
	}

	$map = Get-ZhTokenMap
	$normalized = [System.IO.Path]::GetFileNameWithoutExtension($Value.Trim())
	$normalized = $normalized -creplace '([a-z])([A-Z])', '$1_$2'
	$parts = $normalized.Replace('-', '_').Split('_', [System.StringSplitOptions]::RemoveEmptyEntries)
	$words = foreach ($part in $parts) {
		$key = $part.ToLowerInvariant()
		if ($map.ContainsKey($key)) {
			$map[$key]
		}
		else {
			if ($part -match '^\d+$') { $part }
			else { $part }
		}
	}

	return ($words -join ' ').Trim()
}

function Get-MatchedDirectoryRule {
	param(
		[hashtable[]]$Rules,
		[string]$Directory
	)

	$normalizedDirectory = Get-NormalizedRelativePath $Directory
	foreach ($rule in $Rules) {
		if ((Get-NormalizedRelativePath $rule.directory) -ieq $normalizedDirectory) {
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
		$ruleDirectory = if ($rule.ContainsKey("directory")) { Get-NormalizedRelativePath $rule.directory } else { "" }
		if (-not [string]::IsNullOrWhiteSpace($ruleDirectory) -and $ruleDirectory -ine $normalizedDirectory) {
			continue
		}

		$prefix = [string]$rule.prefix
		if ($FileName.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase) -or
			$BaseName.StartsWith($prefix.TrimEnd('_'), [System.StringComparison]::OrdinalIgnoreCase)) {
			return $rule
		}
	}

	return $null
}

function Get-OverrideMap {
	param(
		[hashtable]$Rules,
		[string]$RepoRoot
	)

	$overrideMap = @{}

	if ($Rules.ContainsKey("renamePlanCsvPaths")) {
		foreach ($csvPath in $Rules.renamePlanCsvPaths) {
			$fullCsvPath = Join-Path $RepoRoot $csvPath
			if (-not (Test-Path $fullCsvPath)) {
				continue
			}

			foreach ($row in (Import-Csv $fullCsvPath)) {
				$relativePath = Get-NormalizedRelativePath ([string]$row.original_path)
				$overrideMap[$relativePath] = @{
					path = $relativePath
					displayNameZh = ([string]$row.suggested_name_zh).Trim()
					displayNameEn = Convert-ToEnglishTitle ([string]$row.suggested_basename)
					confidence = ([string]$row.confidence).Trim()
				}
			}
		}
	}

	if ($Rules.ContainsKey("fileOverrides")) {
		foreach ($override in $Rules.fileOverrides) {
			$relativePath = Get-NormalizedRelativePath ([string]$override.path)
			if (-not $overrideMap.ContainsKey($relativePath)) {
				$overrideMap[$relativePath] = @{ path = $relativePath }
			}

			foreach ($key in $override.Keys) {
				$overrideMap[$relativePath][$key] = $override[$key]
			}
		}
	}

	return $overrideMap
}

function Get-MergedTags {
	param(
		[object[]]$TagSources
	)

	$tags = New-Object System.Collections.Generic.List[string]
	foreach ($source in $TagSources) {
		if ($null -eq $source) {
			continue
		}

		foreach ($tag in @($source)) {
			if ([string]::IsNullOrWhiteSpace([string]$tag)) {
				continue
			}

			$tags.Add(([string]$tag).Trim().ToLowerInvariant())
		}
	}

	return $tags.ToArray() | Sort-Object -Unique
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

$rules = (Get-Content -Raw $ruleFilePath | ConvertFrom-Json -AsHashtable)
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
$overrideMap = Get-OverrideMap -Rules $rules -RepoRoot $repoRoot

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

	$group = if ($fileOverride -and $fileOverride.ContainsKey("group") -and -not [string]::IsNullOrWhiteSpace([string]$fileOverride.group)) {
		([string]$fileOverride.group).Trim()
	}
	elseif ($prefixRule -and $prefixRule.ContainsKey("group") -and -not [string]::IsNullOrWhiteSpace([string]$prefixRule.group)) {
		([string]$prefixRule.group).Trim()
	}
	elseif ($directoryRule -and $directoryRule.ContainsKey("group") -and -not [string]::IsNullOrWhiteSpace([string]$directoryRule.group)) {
		([string]$directoryRule.group).Trim()
	}
	else {
		Convert-ToZhTitle ($relativeDirectory -replace '/', '_')
	}

	$displayNameZh = Get-ResolvedName `
		-ExactName ($(if ($fileOverride) { [string]$fileOverride.displayNameZh } else { "" })) `
		-BaseName ($(if ($prefixRule) { [string]$prefixRule.displayNameZhBase } else { "" })) `
		-Sequence $sequence `
		-FallbackBuilder { Get-ResolvedName -ExactName "" -BaseName (Convert-ToZhTitle $baseName) -Sequence $sequence -FallbackBuilder { Convert-ToZhTitle $file.Name } }

	$displayNameEn = Get-ResolvedName `
		-ExactName ($(if ($fileOverride) { [string]$fileOverride.displayNameEn } else { "" })) `
		-BaseName ($(if ($prefixRule) { [string]$prefixRule.displayNameEnBase } else { "" })) `
		-Sequence $sequence `
		-FallbackBuilder { Get-ResolvedName -ExactName "" -BaseName (Convert-ToEnglishTitle $baseName) -Sequence $sequence -FallbackBuilder { Convert-ToEnglishTitle $file.Name } }

	$kind = if ($fileOverride -and $fileOverride.ContainsKey("kind") -and -not [string]::IsNullOrWhiteSpace([string]$fileOverride.kind)) {
		([string]$fileOverride.kind).Trim()
	}
	elseif ($prefixRule -and $prefixRule.ContainsKey("kind") -and -not [string]::IsNullOrWhiteSpace([string]$prefixRule.kind)) {
		([string]$prefixRule.kind).Trim()
	}
	elseif ($directoryRule -and $directoryRule.ContainsKey("kind") -and -not [string]::IsNullOrWhiteSpace([string]$directoryRule.kind)) {
		([string]$directoryRule.kind).Trim()
	}
	else {
		"sprite"
	}

	$confidence = if ($fileOverride -and $fileOverride.ContainsKey("confidence") -and -not [string]::IsNullOrWhiteSpace([string]$fileOverride.confidence)) {
		([string]$fileOverride.confidence).Trim()
	}
	elseif ($prefixRule -and $prefixRule.ContainsKey("confidence") -and -not [string]::IsNullOrWhiteSpace([string]$prefixRule.confidence)) {
		([string]$prefixRule.confidence).Trim()
	}
	elseif ($directoryRule -and $directoryRule.ContainsKey("confidence") -and -not [string]::IsNullOrWhiteSpace([string]$directoryRule.confidence)) {
		([string]$directoryRule.confidence).Trim()
	}
	else {
		"medium"
	}

	$mappingEligible = $false
	if ($directoryRule -and $directoryRule.ContainsKey("mappingEligible")) {
		$mappingEligible = [bool]$directoryRule.mappingEligible
	}
	if ($prefixRule -and $prefixRule.ContainsKey("mappingEligible")) {
		$mappingEligible = [bool]$prefixRule.mappingEligible
	}
	if ($fileOverride -and $fileOverride.ContainsKey("mappingEligible")) {
		$mappingEligible = [bool]$fileOverride.mappingEligible
	}

	$derivedTags = Get-TokenTags @($relativeDirectory, $baseName, $file.Name)
	$tags = Get-MergedTags -TagSources @(
		$(if ($directoryRule) { $directoryRule.tags } else { @() }),
		$(if ($prefixRule) { $prefixRule.tags } else { @() }),
		$(if ($fileOverride -and $fileOverride.ContainsKey("tags")) { $fileOverride.tags } else { @() }),
		$derivedTags
	)

	$entries.Add([ordered]@{
		id = Convert-ToId $relativePath
		path = "res://Assets/Art/PZ_Tiles_Copy/$relativePath"
		group = $group
		originalFileName = $file.Name
		displayNameZh = $displayNameZh
		displayNameEn = $displayNameEn
		tags = @($tags)
		kind = $kind
		confidence = $confidence
		mappingEligible = $mappingEligible
	})
}

$document = @{
	entries = $entries.ToArray()
}

$outputDirectory = Split-Path $resolvedOutputPath -Parent
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
	New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
}

Set-Content -Path $resolvedOutputPath -Value (Get-OrderedJson $document) -Encoding UTF8
Write-Output ("Generated {0} catalog entries at {1}" -f $entries.Count, $resolvedOutputPath)
