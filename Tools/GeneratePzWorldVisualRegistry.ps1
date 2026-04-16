param(
	[string]$CatalogPath = "Data/pz_tile_catalog.json",
	[string]$VoxelMappingPath = "Data/voxel_tile_mapping.json",
	[string]$ItemsPath = "Data/items.json",
	[string]$FixturesPath = "Data/fixtures.json",
	[string]$FacilitiesPath = "Data/facilities.json",
	[string]$RegistryOutputPath = "Data/pz_world_visual_registry.json",
	[string]$CompatVoxelOutputPath = "Data/voxel_tile_mapping.json",
	[string]$CompatItemOutputPath = "Data/item_world_render.json",
	[string]$CompatTileOutputPath = "Data/tile_mapping.json"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Normalize-ResPath {
	param([string]$Path)

	if ([string]::IsNullOrWhiteSpace($Path)) {
		return ""
	}

	$normalized = $Path.Trim().Replace('\', '/')
	if ($normalized.StartsWith('Assets/', [System.StringComparison]::OrdinalIgnoreCase)) {
		$normalized = "res://$normalized"
	}

	if ($normalized.StartsWith('res://Assets/Art/PZ_Tiles/', [System.StringComparison]::OrdinalIgnoreCase)) {
		return 'res://Assets/Art/PZ_Tiles_Copy' + $normalized.Substring('res://Assets/Art/PZ_Tiles'.Length)
	}

	return $normalized
}

function Get-OptionalPropertyValue {
	param(
		$Object,
		[string]$Name,
		$Default = $null
	)

	if ($null -eq $Object) {
		return $Default
	}

	$property = $Object.PSObject.Properties[$Name]
	if ($null -eq $property) {
		return $Default
	}

	return $property.Value
}

function New-Scale {
	param(
		[float]$X = 1.0,
		[float]$Y = 1.0
	)

	return [ordered]@{
		x = $X
		y = $Y
	}
}

function New-Offset {
	param(
		[float]$X = 0.0,
		[float]$Y = 0.0
	)

	return [ordered]@{
		x = $X
		y = $Y
	}
}

function Require-CatalogEntryByRelativePath {
	param(
		[string]$RelativePath,
		$CatalogByRelativePath
	)

	$key = $RelativePath.Trim().Replace('\', '/')
	if (-not $CatalogByRelativePath.ContainsKey($key)) {
		throw "Catalog entry not found for relative path: $RelativePath"
	}

	return $CatalogByRelativePath[$key]
}

function Resolve-CatalogEntry {
	param(
		[string]$RelativePath,
		$CatalogByRelativePath
	)

	if ([string]::IsNullOrWhiteSpace($RelativePath)) {
		return $null
	}

	return Require-CatalogEntryByRelativePath -RelativePath $RelativePath -CatalogByRelativePath $CatalogByRelativePath
}

function New-CatalogTextureVisual {
	param(
		[string]$Id,
		[string]$RelativePath,
		[string]$Placement,
		[string]$Notes,
		[string]$VariantGroup,
		[float]$ScaleX = 1.0,
		[float]$ScaleY = 1.0,
		[float]$OffsetX = 0.0,
		[float]$OffsetY = 0.0,
		[float]$ZBias = 0.0,
		$CatalogByRelativePath
	)

	$catalogEntry = Resolve-CatalogEntry -RelativePath $RelativePath -CatalogByRelativePath $CatalogByRelativePath
	if ($null -eq $catalogEntry) {
		return $null
	}

	return [ordered]@{
		id = $Id
		visualKind = 'catalog_texture'
		catalogId = $catalogEntry.id
		scale = (New-Scale -X $ScaleX -Y $ScaleY)
		offset = (New-Offset -X $OffsetX -Y $OffsetY)
		zBias = $ZBias
		placement = $Placement
		variantGroup = $VariantGroup
		notes = $Notes
	}
}

function New-AnimatedVisual {
	param(
		[string]$Id,
		[string[]]$RelativePaths,
		[string]$Placement,
		[string]$Notes,
		[string]$VariantGroup,
		[float]$ScaleX = 1.0,
		[float]$ScaleY = 1.0,
		[float]$OffsetX = 0.0,
		[float]$OffsetY = 0.0,
		[float]$ZBias = 0.0,
		$CatalogByRelativePath
	)

	$paths = @()
	$catalogIds = @()
	foreach ($relativePath in $RelativePaths) {
		$catalogEntry = Resolve-CatalogEntry -RelativePath $relativePath -CatalogByRelativePath $CatalogByRelativePath
		if ($null -eq $catalogEntry) {
			continue
		}

		$catalogIds += $catalogEntry.id
		$paths += $catalogEntry.path
	}

	if ($paths.Count -eq 0) {
		return $null
	}

	return [ordered]@{
		id = $Id
		visualKind = 'animated_sequence'
		catalogIds = @($catalogIds)
		paths = @($paths)
		scale = (New-Scale -X $ScaleX -Y $ScaleY)
		offset = (New-Offset -X $OffsetX -Y $OffsetY)
		zBias = $ZBias
		placement = $Placement
		variantGroup = $VariantGroup
		notes = $Notes
	}
}

function Convert-TerrainMappingToRegistryEntry {
	param(
		$MappingEntry,
		$CatalogByPath
	)

	$topPath = Normalize-ResPath ([string](Get-OptionalPropertyValue -Object $MappingEntry -Name 'topTilePath' -Default ''))
	$leftPath = Normalize-ResPath ([string](Get-OptionalPropertyValue -Object $MappingEntry -Name 'leftSideTilePath' -Default ''))
	$rightPath = Normalize-ResPath ([string](Get-OptionalPropertyValue -Object $MappingEntry -Name 'rightSideTilePath' -Default ''))

	$topCatalog = if (-not [string]::IsNullOrWhiteSpace($topPath) -and $CatalogByPath.ContainsKey($topPath)) { $CatalogByPath[$topPath] } else { $null }
	$leftCatalog = if (-not [string]::IsNullOrWhiteSpace($leftPath) -and $CatalogByPath.ContainsKey($leftPath)) { $CatalogByPath[$leftPath] } else { $null }
	$rightCatalog = if (-not [string]::IsNullOrWhiteSpace($rightPath) -and $CatalogByPath.ContainsKey($rightPath)) { $CatalogByPath[$rightPath] } else { $null }

	return [ordered]@{
		terrainId = ([string](Get-OptionalPropertyValue -Object $MappingEntry -Name 'terrainId' -Default '')).Trim()
		topCatalogId = if ($null -ne $topCatalog) { $topCatalog.id } else { $null }
		topPath = if ([string]::IsNullOrWhiteSpace($topPath)) { $null } else { $topPath }
		topIsIso = [bool](Get-OptionalPropertyValue -Object $MappingEntry -Name 'topIsIso' -Default $false)
		topScaleX = [float](Get-OptionalPropertyValue -Object $MappingEntry -Name 'topScaleX' -Default 1.0)
		topScaleY = [float](Get-OptionalPropertyValue -Object $MappingEntry -Name 'topScaleY' -Default 1.0)
		topOffsetX = [float](Get-OptionalPropertyValue -Object $MappingEntry -Name 'topOffsetX' -Default 0.0)
		topOffsetY = [float](Get-OptionalPropertyValue -Object $MappingEntry -Name 'topOffsetY' -Default 0.0)
		left = [ordered]@{
			mode = if ([string]::IsNullOrWhiteSpace([string](Get-OptionalPropertyValue -Object $MappingEntry -Name 'leftSideMode' -Default ''))) { $null } else { ([string](Get-OptionalPropertyValue -Object $MappingEntry -Name 'leftSideMode' -Default '')).Trim() }
			catalogId = if ($null -ne $leftCatalog) { $leftCatalog.id } else { $null }
			path = if ([string]::IsNullOrWhiteSpace($leftPath)) { $null } else { $leftPath }
			color = if ([string]::IsNullOrWhiteSpace([string](Get-OptionalPropertyValue -Object $MappingEntry -Name 'leftSideColor' -Default ''))) { $null } else { ([string](Get-OptionalPropertyValue -Object $MappingEntry -Name 'leftSideColor' -Default '')).Trim() }
			isIso = [bool](Get-OptionalPropertyValue -Object $MappingEntry -Name 'leftIsIso' -Default $false)
			offsetX = [float](Get-OptionalPropertyValue -Object $MappingEntry -Name 'leftOffsetX' -Default 0.0)
			offsetY = [float](Get-OptionalPropertyValue -Object $MappingEntry -Name 'leftOffsetY' -Default 0.0)
			height = [int](Get-OptionalPropertyValue -Object $MappingEntry -Name 'leftHeight' -Default 0)
			visible = [bool](Get-OptionalPropertyValue -Object $MappingEntry -Name 'showLeftSide' -Default $true)
		}
		right = [ordered]@{
			mode = if ([string]::IsNullOrWhiteSpace([string](Get-OptionalPropertyValue -Object $MappingEntry -Name 'rightSideMode' -Default ''))) { $null } else { ([string](Get-OptionalPropertyValue -Object $MappingEntry -Name 'rightSideMode' -Default '')).Trim() }
			catalogId = if ($null -ne $rightCatalog) { $rightCatalog.id } else { $null }
			path = if ([string]::IsNullOrWhiteSpace($rightPath)) { $null } else { $rightPath }
			color = if ([string]::IsNullOrWhiteSpace([string](Get-OptionalPropertyValue -Object $MappingEntry -Name 'rightSideColor' -Default ''))) { $null } else { ([string](Get-OptionalPropertyValue -Object $MappingEntry -Name 'rightSideColor' -Default '')).Trim() }
			isIso = [bool](Get-OptionalPropertyValue -Object $MappingEntry -Name 'rightIsIso' -Default $false)
			offsetX = [float](Get-OptionalPropertyValue -Object $MappingEntry -Name 'rightOffsetX' -Default 0.0)
			offsetY = [float](Get-OptionalPropertyValue -Object $MappingEntry -Name 'rightOffsetY' -Default 0.0)
			height = [int](Get-OptionalPropertyValue -Object $MappingEntry -Name 'rightHeight' -Default 0)
			visible = [bool](Get-OptionalPropertyValue -Object $MappingEntry -Name 'showRightSide' -Default $true)
		}
	}
}

function Convert-WorldVisualToCompatEntry {
	param(
		$Visual
	)

	if ($null -eq $Visual) {
		return $null
	}

	$kind = ([string]$Visual.visualKind).Trim().ToLowerInvariant()
	$scaleX = if ($null -ne $Visual.scale -and $null -ne $Visual.scale.x) { [float]$Visual.scale.x } else { 1.0 }
	$scaleY = if ($null -ne $Visual.scale -and $null -ne $Visual.scale.y) { [float]$Visual.scale.y } else { 1.0 }

	switch ($kind) {
		'catalog_texture' {
			return [ordered]@{
				kind = 'catalog_texture'
				value = [string]$Visual.catalogId
				scale = @($scaleX, $scaleY)
			}
		}
		'texture' {
			return [ordered]@{
				kind = 'texture'
				value = Normalize-ResPath ([string]$Visual.path)
				scale = @($scaleX, $scaleY)
			}
		}
		'animated_sequence' {
			if ($null -ne $Visual.paths -and @($Visual.paths).Count -gt 0) {
				return [ordered]@{
					kind = 'texture'
					value = Normalize-ResPath ([string]$Visual.paths[0])
					scale = @($scaleX, $scaleY)
				}
			}
		}
	}

	return $null
}

function Get-RelativePathsInDirectory {
	param(
		[string]$DirectoryPath,
		$CatalogByRelativePath
	)

	return $CatalogByRelativePath.Keys | Where-Object { $_.StartsWith($DirectoryPath + '/', [System.StringComparison]::OrdinalIgnoreCase) } | Sort-Object
}

function Get-MatchingCatalogIds {
	param(
		[scriptblock]$Predicate,
		$CatalogEntries
	)

	return @($CatalogEntries | Where-Object { & $Predicate $_ } | Select-Object -ExpandProperty id)
}

$repoRoot = (Resolve-Path '.').Path
$catalog = Get-Content (Join-Path $repoRoot $CatalogPath) -Raw -Encoding utf8 | ConvertFrom-Json
$voxelMapping = Get-Content (Join-Path $repoRoot $VoxelMappingPath) -Raw -Encoding utf8 | ConvertFrom-Json
$items = Get-Content (Join-Path $repoRoot $ItemsPath) -Raw -Encoding utf8 | ConvertFrom-Json
$fixtures = Get-Content (Join-Path $repoRoot $FixturesPath) -Raw -Encoding utf8 | ConvertFrom-Json
$facilities = Get-Content (Join-Path $repoRoot $FacilitiesPath) -Raw -Encoding utf8 | ConvertFrom-Json

$catalogByPath = @{}
$catalogByRelativePath = @{}
foreach ($entry in $catalog.entries) {
	$normalizedPath = Normalize-ResPath ([string]$entry.path)
	$catalogByPath[$normalizedPath] = $entry
	$relativePath = $normalizedPath.Substring('res://Assets/Art/PZ_Tiles_Copy/'.Length)
	$catalogByRelativePath[$relativePath] = $entry
}

$terrainEntries = @()
foreach ($entry in $voxelMapping.entries) {
	if ([string]::IsNullOrWhiteSpace([string]$entry.terrainId)) {
		continue
	}

	$terrainEntries += Convert-TerrainMappingToRegistryEntry -MappingEntry $entry -CatalogByPath $catalogByPath
}
$terrainEntries = @($terrainEntries | Sort-Object terrainId)

$fixtureVisuals = @()
$fixtureOverrides = @(
	@{ id = 'stair_down'; relativePath = 'fixtures_stairs_01/fixtures_stairs_01_0.png'; placement = 'floor'; variantGroup = 'stairs' ; notes = 'PZ stair asset for downward stair'; scaleX = 1.0; scaleY = 1.0 },
	@{ id = 'stair_up'; relativePath = 'fixtures_stairs_01/fixtures_stairs_01_12.png'; placement = 'floor'; variantGroup = 'stairs'; notes = 'PZ stair asset for upward stair'; scaleX = 1.0; scaleY = 1.0 },
	@{ id = 'nest'; relativePath = 'constructedobjects_01/constructedobjects_01_10.png'; placement = 'object'; variantGroup = 'constructedobjects'; notes = 'Cage-like constructed prop for nest'; scaleX = 1.0; scaleY = 1.0 },
	@{ id = 'door'; relativePath = 'fixtures_doors_01/fixtures_doors_01_10.png'; placement = 'wall'; variantGroup = 'doors'; notes = 'Wood door'; scaleX = 1.0; scaleY = 1.0 },
	@{ id = 'house'; relativePath = 'roofs_01/roofs_01_10.png'; placement = 'roof'; variantGroup = 'roof'; notes = 'Roof tile used as house visual'; scaleX = 1.0; scaleY = 1.0 },
	@{ id = 'ladder'; relativePath = 'constructedobjects_01/constructedobjects_01_12.png'; placement = 'object'; variantGroup = 'constructedobjects'; notes = 'Wooden construction piece for ladder'; scaleX = 1.0; scaleY = 1.0 },
	@{ id = 'shelf'; relativePath = 'furniture_shelving_01/furniture_shelving_01_10.png'; placement = 'object'; variantGroup = 'shelving'; notes = 'Wood shelf'; scaleX = 1.0; scaleY = 1.0 },
	@{ id = 'fire_brazier'; relativePath = 'constructedobjects_01/constructedobjects_01_1.png'; placement = 'object'; variantGroup = 'constructedobjects'; notes = 'Candle stand used for brazier base'; scaleX = 1.0; scaleY = 1.0 },
	@{ id = 'bed'; relativePath = 'furniture_bedding_01/furniture_bedding_01_0.png'; placement = 'object'; variantGroup = 'bedding'; notes = 'Single bed'; scaleX = 1.0; scaleY = 1.0 },
	@{ id = 'dormitory_bed'; relativePath = 'furniture_bedding_01/furniture_bedding_01_1.png'; placement = 'object'; variantGroup = 'bedding'; notes = 'Simple bed'; scaleX = 1.0; scaleY = 1.0 },
	@{ id = 'stove'; relativePath = 'fixtures_fireplaces_01/fixtures_fireplaces_01_0.png'; placement = 'wall'; variantGroup = 'fireplace'; notes = 'Fireplace/stove proxy'; scaleX = 1.0; scaleY = 1.0 },
	@{ id = 'butcher_table'; relativePath = 'furniture_tables_high_01/furniture_tables_high_01_0.png'; placement = 'object'; variantGroup = 'table'; notes = 'High work table'; scaleX = 1.0; scaleY = 1.0 },
	@{ id = 'smithy'; relativePath = 'fixtures_fireplaces_01/fixtures_fireplaces_01_1.png'; placement = 'wall'; variantGroup = 'fireplace'; notes = 'Forge proxy'; scaleX = 1.0; scaleY = 1.0 },
	@{ id = 'loom'; relativePath = 'carpentry_01/carpentry_01_24.png'; placement = 'object'; variantGroup = 'carpentry'; notes = 'Carpentry frame proxy for loom'; scaleX = 1.0; scaleY = 1.0 },
	@{ id = 'herbal_bench'; relativePath = 'furniture_tables_high_01/furniture_tables_high_01_1.png'; placement = 'object'; variantGroup = 'table'; notes = 'Herbal workbench proxy'; scaleX = 1.0; scaleY = 1.0 },
	@{ id = 'market_stall'; relativePath = 'fixtures_counters_01/fixtures_counters_01_10.png'; placement = 'object'; variantGroup = 'counter'; notes = 'Counter proxy for market stall'; scaleX = 1.0; scaleY = 1.0 }
)

foreach ($fixtureDef in $fixtureOverrides) {
	$visual = New-CatalogTextureVisual `
		-Id ([string]$fixtureDef.id) `
		-RelativePath ([string]$fixtureDef.relativePath) `
		-Placement ([string]$fixtureDef.placement) `
		-Notes ([string]$fixtureDef.notes) `
		-VariantGroup ([string]$fixtureDef.variantGroup) `
		-ScaleX ([float]$fixtureDef.scaleX) `
		-ScaleY ([float]$fixtureDef.scaleY) `
		-CatalogByRelativePath $catalogByRelativePath
	if ($null -ne $visual) {
		$fixtureVisuals += $visual
	}
}

$campfireSequence = Get-RelativePathsInDirectory -DirectoryPath 'Fire_01' -CatalogByRelativePath $catalogByRelativePath
$fireSequence = Get-RelativePathsInDirectory -DirectoryPath 'Fire_02' -CatalogByRelativePath $catalogByRelativePath
$fixtureVisuals += New-AnimatedVisual -Id 'campfire' -RelativePaths $campfireSequence -Placement 'object' -Notes 'Campfire animation from Fire_01' -VariantGroup 'fire_small' -ScaleX 1.0 -ScaleY 1.0 -CatalogByRelativePath $catalogByRelativePath
$fixtureVisuals += New-AnimatedVisual -Id 'fire' -RelativePaths $fireSequence -Placement 'object' -Notes 'Fire animation from Fire_02' -VariantGroup 'fire_medium' -ScaleX 1.0 -ScaleY 1.0 -CatalogByRelativePath $catalogByRelativePath
$fixtureVisuals = @($fixtureVisuals | Where-Object { $null -ne $_ } | Sort-Object id)

$categoryDefaults = @{
	'consumable' = @{ relativePath = 'food_02/food_02_101.png'; notes = 'Default consumable visual'; variantGroup = 'food_02' }
	'food' = @{ relativePath = 'food_01/food_01_0.png'; notes = 'Default food visual'; variantGroup = 'food_01' }
	'weapon' = @{ relativePath = 'weapons_01/weapons_01_1.png'; notes = 'Default weapon visual'; variantGroup = 'weapons_01' }
	'armor' = @{ relativePath = 'stashes_01/stashes_01_16.png'; notes = 'Default armor visual'; variantGroup = 'stashes_01' }
	'clothing' = @{ relativePath = 'stashes_01/stashes_01_1.png'; notes = 'Default clothing visual'; variantGroup = 'stashes_01' }
	'tool' = @{ relativePath = 'constructedobjects_01/constructedobjects_01_0.png'; notes = 'Default tool visual'; variantGroup = 'constructedobjects' }
	'material' = @{ relativePath = 'carpentry_01/carpentry_01_10.png'; notes = 'Default material visual'; variantGroup = 'carpentry' }
	'ammo' = @{ relativePath = 'weapons_01/weapons_01_0.png'; notes = 'Default ammo visual'; variantGroup = 'weapons_01' }
	'misc' = @{ relativePath = 'stashes_01/stashes_01_17.png'; notes = 'Default misc visual'; variantGroup = 'stashes_01' }
}

$itemOverrides = @{
	'potion_hp' = 'food_02/food_02_101.png'
	'potion_str' = 'food_02/food_02_102.png'
	'herbal_medicine' = 'food_01/food_01_101.png'
	'antidote' = 'food_02/food_02_103.png'
	'bandage' = 'stashes_01/stashes_01_0.png'
	'go_juice' = 'food_02/food_02_100.png'
	'meal_simple' = 'food_01/food_01_0.png'
	'meal_fine' = 'food_01/food_01_1.png'
	'meal_lavish' = 'food_02/food_02_0.png'
	'raw_meat' = 'food_01/food_01_103.png'
	'berries' = 'food_01/food_01_10.png'
	'torch' = 'constructedobjects_01/constructedobjects_01_0.png'
	'generator' = 'appliances_misc_01/appliances_misc_01_0.png'
	'water_bottle' = 'food_02/food_02_103.png'
	'apple' = 'food_01/food_01_10.png'
	'bread' = 'food_01/food_01_16.png'
	'cheese' = 'food_01/food_01_17.png'
	'chocolate' = 'food_02/food_02_11.png'
	'alcohol' = 'food_02/food_02_102.png'
	'cigarette' = 'food_02/food_02_100.png'
	'candle' = 'constructedobjects_01/constructedobjects_01_1.png'
	'petrol' = 'food_02/food_02_103.png'
	'propane_tank' = 'constructedobjects_01/constructedobjects_01_14.png'
	'walkie_talkie' = 'security_01/security_01_0.png'
	'map' = 'stashes_01/stashes_01_0.png'
}

$itemCategoryEntries = @()
foreach ($category in ($categoryDefaults.Keys | Sort-Object)) {
	$definition = $categoryDefaults[$category]
	$visual = New-CatalogTextureVisual `
		-Id $category `
		-RelativePath ([string]$definition.relativePath) `
		-Placement 'object' `
		-Notes ([string]$definition.notes) `
		-VariantGroup ([string]$definition.variantGroup) `
		-ScaleX 0.55 `
		-ScaleY 0.55 `
		-CatalogByRelativePath $catalogByRelativePath
	if ($null -ne $visual) {
		$itemCategoryEntries += $visual
	}
}

$defaultItemVisual = New-CatalogTextureVisual `
	-Id 'default' `
	-RelativePath 'stashes_01/stashes_01_17.png' `
	-Placement 'object' `
	-Notes 'Default world drop visual' `
	-VariantGroup 'stashes_01' `
	-ScaleX 0.55 `
	-ScaleY 0.55 `
	-CatalogByRelativePath $catalogByRelativePath

$itemEntries = @()
foreach ($item in $items) {
	$itemId = ([string]$item.id).Trim()
	if ([string]::IsNullOrWhiteSpace($itemId)) {
		continue
	}

	$category = ([string]$item.category).Trim()
	$relativePath = if ($itemOverrides.ContainsKey($itemId)) {
		[string]$itemOverrides[$itemId]
	}
	elseif ($categoryDefaults.ContainsKey($category)) {
		[string]$categoryDefaults[$category].relativePath
	}
	else {
		'stashes_01/stashes_01_17.png'
	}

	$variantGroup = if ($categoryDefaults.ContainsKey($category)) { [string]$categoryDefaults[$category].variantGroup } else { 'item_world' }
	$notes = if ($itemOverrides.ContainsKey($itemId)) {
		"Item override for $itemId"
	}
	elseif ($categoryDefaults.ContainsKey($category)) {
		"Category fallback for $category"
	}
	else {
		'Default world item fallback'
	}

	$visual = New-CatalogTextureVisual `
		-Id $itemId `
		-RelativePath $relativePath `
		-Placement 'object' `
		-Notes $notes `
		-VariantGroup $variantGroup `
		-ScaleX 0.55 `
		-ScaleY 0.55 `
		-CatalogByRelativePath $catalogByRelativePath
	if ($null -ne $visual) {
		$itemEntries += $visual
	}
}
$itemEntries = @($itemEntries | Sort-Object id)

$fxEntries = @(
	(New-AnimatedVisual -Id 'fire_small' -RelativePaths (Get-RelativePathsInDirectory -DirectoryPath 'Fire_01' -CatalogByRelativePath $catalogByRelativePath) -Placement 'object' -Notes 'PZ small fire animation' -VariantGroup 'fire_small' -CatalogByRelativePath $catalogByRelativePath),
	(New-AnimatedVisual -Id 'fire_medium' -RelativePaths (Get-RelativePathsInDirectory -DirectoryPath 'Fire_02' -CatalogByRelativePath $catalogByRelativePath) -Placement 'object' -Notes 'PZ medium fire animation' -VariantGroup 'fire_medium' -CatalogByRelativePath $catalogByRelativePath),
	(New-AnimatedVisual -Id 'fire_large' -RelativePaths (Get-RelativePathsInDirectory -DirectoryPath 'Fire_03' -CatalogByRelativePath $catalogByRelativePath) -Placement 'object' -Notes 'PZ large fire animation' -VariantGroup 'fire_large' -CatalogByRelativePath $catalogByRelativePath)
) | Where-Object { $null -ne $_ } | Sort-Object id

$decorPools = @()

$decorPools += [ordered]@{
	poolId = 'floor_cracks'
	placement = 'floor'
	catalogIds = @(Get-MatchingCatalogIds -CatalogEntries $catalog.entries -Predicate {
		param($entry)
		$entry.path -match '/overlays/floors_overlay_' -or $entry.path -match '/erosion/d_streetcracks_1_'
	})
	weights = @()
	density = 0.65
	tags = @('floor', 'crack', 'overlay')
}

$decorPools += [ordered]@{
	poolId = 'wall_cracks'
	placement = 'wall'
	catalogIds = @(Get-MatchingCatalogIds -CatalogEntries $catalog.entries -Predicate {
		param($entry)
		$entry.path -match '/overlays/walls_house_' -or $entry.path -match '/erosion/d_wallcracks_1_'
	})
	weights = @()
	density = 0.55
	tags = @('wall', 'crack', 'overlay')
}

$decorPools += [ordered]@{
	poolId = 'street_trash'
	placement = 'floor'
	catalogIds = @(Get-MatchingCatalogIds -CatalogEntries $catalog.entries -Predicate {
		param($entry)
		$entry.path -match '/trash_01/'
	})
	weights = @()
	density = 0.85
	tags = @('trash', 'street', 'floor')
}

$decorPools += [ordered]@{
	poolId = 'interior_trash'
	placement = 'floor'
	catalogIds = @(Get-MatchingCatalogIds -CatalogEntries $catalog.entries -Predicate {
		param($entry)
		if ($entry.path -notmatch '/trash_01/trash_01_([0-9]+)\.png$') {
			return $false
		}

		$index = [int]$matches[1]
		return $index -ge 24 -and $index -le 53
	})
	weights = @()
	density = 0.55
	tags = @('trash', 'interior', 'floor')
}

$decorPools += [ordered]@{
	poolId = 'leaf_litter'
	placement = 'floor'
	catalogIds = @(Get-MatchingCatalogIds -CatalogEntries $catalog.entries -Predicate {
		param($entry)
		$entry.path -match '/erosion/d_floorleaves_1_'
	})
	weights = @()
	density = 0.7
	tags = @('leaf', 'floor', 'erosion')
}

$decorPools += [ordered]@{
	poolId = 'roof_snow'
	placement = 'roof'
	catalogIds = @(Get-MatchingCatalogIds -CatalogEntries $catalog.entries -Predicate {
		param($entry)
		$entry.path -match '/erosion/e_roof_snow_1_'
	})
	weights = @()
	density = 0.75
	tags = @('roof', 'snow', 'erosion')
}

$decorPools += [ordered]@{
	poolId = 'vines'
	placement = 'wall'
	catalogIds = @(Get-MatchingCatalogIds -CatalogEntries $catalog.entries -Predicate {
		param($entry)
		$entry.path -match '/erosion/f_wallvines_1_'
	})
	weights = @()
	density = 0.45
	tags = @('wall', 'vine', 'erosion')
}

$decorPools += [ordered]@{
	poolId = 'grass_patch'
	placement = 'floor'
	catalogIds = @(Get-MatchingCatalogIds -CatalogEntries $catalog.entries -Predicate {
		param($entry)
		$entry.path -match '/erosion/e_newgrass_1_'
	})
	weights = @()
	density = 0.8
	tags = @('grass', 'patch', 'erosion')
}

$decorPools += [ordered]@{
	poolId = 'tree_canopy'
	placement = 'mixed'
	catalogIds = @(Get-MatchingCatalogIds -CatalogEntries $catalog.entries -Predicate {
		param($entry)
		$entry.path -match '/jumbo_trees/'
	})
	weights = @()
	density = 0.9
	tags = @('tree', 'canopy', 'decor')
}

foreach ($pool in $decorPools) {
	$pool.weights = @($pool.catalogIds | ForEach-Object { 1.0 })
}
$decorPools = @($decorPools | Sort-Object poolId)

$registry = [ordered]@{
	terrain = @($terrainEntries)
	fixture = @($fixtureVisuals)
	itemWorld = [ordered]@{
		items = @($itemEntries)
		categories = @($itemCategoryEntries | Sort-Object id)
		default = $defaultItemVisual
	}
	fx = @($fxEntries)
	decorPools = @($decorPools)
}

$registryOutputFullPath = Join-Path $repoRoot $RegistryOutputPath
$registry | ConvertTo-Json -Depth 32 | Set-Content -Path $registryOutputFullPath -Encoding UTF8

$compatVoxel = [ordered]@{ entries = @() }
foreach ($terrain in $registry.terrain) {
	$compatVoxel.entries += [ordered]@{
		terrainId = $terrain.terrainId
		category = 'terrain'
		topTilePath = $terrain.topPath
		topIsIso = [bool]$terrain.topIsIso
		leftSideMode = $terrain.left.mode
		leftSideTilePath = $terrain.left.path
		leftSideColor = $terrain.left.color
		leftIsIso = [bool]$terrain.left.isIso
		rightSideMode = $terrain.right.mode
		rightSideTilePath = $terrain.right.path
		rightSideColor = $terrain.right.color
		rightIsIso = [bool]$terrain.right.isIso
		topScaleX = [float]$terrain.topScaleX
		topScaleY = [float]$terrain.topScaleY
		topOffsetX = [float]$terrain.topOffsetX
		topOffsetY = [float]$terrain.topOffsetY
		leftOffsetX = [float]$terrain.left.offsetX
		leftOffsetY = [float]$terrain.left.offsetY
		leftHeight = [int]$terrain.left.height
		rightOffsetX = [float]$terrain.right.offsetX
		rightOffsetY = [float]$terrain.right.offsetY
		rightHeight = [int]$terrain.right.height
		showLeftSide = [bool]$terrain.left.visible
		showRightSide = [bool]$terrain.right.visible
	}
}
($compatVoxel | ConvertTo-Json -Depth 16) | Set-Content -Path (Join-Path $repoRoot $CompatVoxelOutputPath) -Encoding UTF8

$compatItemWorld = [ordered]@{
	default = (Convert-WorldVisualToCompatEntry -Visual $registry.itemWorld.default)
	categories = [ordered]@{}
	items = [ordered]@{}
}
foreach ($category in $registry.itemWorld.categories) {
	$compat = Convert-WorldVisualToCompatEntry -Visual $category
	if ($null -ne $compat) {
		$compatItemWorld.categories[$category.id] = $compat
	}
}
foreach ($item in $registry.itemWorld.items) {
	$compat = Convert-WorldVisualToCompatEntry -Visual $item
	if ($null -ne $compat) {
		$compatItemWorld.items[$item.id] = $compat
	}
}
($compatItemWorld | ConvertTo-Json -Depth 16) | Set-Content -Path (Join-Path $repoRoot $CompatItemOutputPath) -Encoding UTF8

$compatTileMapping = [ordered]@{
	terrain = [ordered]@{}
	entity = [ordered]@{
		player = 'Misc B1_N'
		hostile = 'Misc B2_N'
		friendly = 'Misc B3_N'
		fire = 'fire_medium'
	}
	fixture = [ordered]@{}
	item = [ordered]@{
		container = (Normalize-ResPath 'res://Assets/Art/PZ_Tiles_Copy/furniture_storage_01/furniture_storage_01_10.png')
		drop = (Normalize-ResPath 'res://Assets/Art/PZ_Tiles_Copy/stashes_01/stashes_01_17.png')
	}
}
$compatTileMapping.terrain['void'] = 'BLACK TILE'
$compatTileMapping.terrain['air'] = 'BLACK TILE'
foreach ($terrain in $registry.terrain) {
	$compatTileMapping.terrain[$terrain.terrainId] = $terrain.topPath
}
foreach ($fixture in $registry.fixture) {
	if ($fixture.visualKind -eq 'catalog_texture' -and -not [string]::IsNullOrWhiteSpace([string]$fixture.catalogId)) {
		$catalogEntry = $catalog.entries | Where-Object { $_.id -eq $fixture.catalogId } | Select-Object -First 1
		if ($null -ne $catalogEntry) {
			$compatTileMapping.fixture[$fixture.id] = $catalogEntry.path
			continue
		}
	}
	if ($fixture.visualKind -eq 'animated_sequence' -and $null -ne $fixture.paths -and @($fixture.paths).Count -gt 0) {
		$compatTileMapping.fixture[$fixture.id] = Normalize-ResPath ([string]$fixture.paths[0])
	}
}
($compatTileMapping | ConvertTo-Json -Depth 16) | Set-Content -Path (Join-Path $repoRoot $CompatTileOutputPath) -Encoding UTF8

Write-Output ("Generated world visual registry: {0}" -f $registryOutputFullPath)
