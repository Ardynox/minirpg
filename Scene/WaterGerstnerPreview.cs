using Godot;

public partial class WaterGerstnerPreview : Node3D
{
	private const float TileSize = 2.2f;
	private const int TileCount = 2;
	private const float WaterDepth = 1.4f;
	private const int TileSubdivisions = 56;
	private const int WallSubdivisions = 64;

	public override void _Ready()
	{
		AddChild(CreateEnvironment());
		AddChild(CreateCamera());
		AddChild(CreateSunLight());
		AddChild(CreateFillLight());
		AddChild(CreateGround());
		AddChild(CreateWaterBlock());
	}

	private static WorldEnvironment CreateEnvironment()
	{
		var environment = new Godot.Environment
		{
			BackgroundMode = Godot.Environment.BGMode.Color,
			BackgroundColor = new Color(0.055f, 0.065f, 0.075f),
			AmbientLightSource = Godot.Environment.AmbientSource.Color,
			AmbientLightColor = new Color(0.78f, 0.84f, 0.86f),
			AmbientLightEnergy = 1.15f,
			AmbientLightSkyContribution = 0.0f,
			TonemapMode = Godot.Environment.ToneMapper.Aces
		};

		return new WorldEnvironment
		{
			Environment = environment
		};
	}

	private static Camera3D CreateCamera()
	{
		var camera = new Camera3D
		{
			Name = "PreviewCamera",
			Current = true,
			Position = new Vector3(6.8f, 4.8f, 6.6f),
			Fov = 38.0f
		};
		camera.LookAtFromPosition(camera.Position, new Vector3(0.0f, -0.1f, 0.0f), Vector3.Up);
		return camera;
	}

	private static DirectionalLight3D CreateSunLight()
	{
		return new DirectionalLight3D
		{
			Name = "SunLight",
			RotationDegrees = new Vector3(-42.0f, -34.0f, 0.0f),
			LightEnergy = 1.65f,
			ShadowEnabled = true
		};
	}

	private static DirectionalLight3D CreateFillLight()
	{
		return new DirectionalLight3D
		{
			Name = "FillLight",
			RotationDegrees = new Vector3(-18.0f, 140.0f, 0.0f),
			LightColor = new Color(0.65f, 0.74f, 0.70f),
			LightEnergy = 0.45f,
			ShadowEnabled = false
		};
	}

	private static MeshInstance3D CreateGround()
	{
		var mesh = new PlaneMesh
		{
			Size = new Vector2(24.0f, 24.0f),
			SubdivideWidth = 2,
			SubdivideDepth = 2
		};

		var material = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.11f, 0.12f, 0.12f),
			Roughness = 0.96f
		};

		return new MeshInstance3D
		{
			Name = "Ground",
			Mesh = mesh,
			Position = new Vector3(0.0f, -WaterDepth - 0.35f, 0.0f),
			MaterialOverride = material
		};
	}

	private Node3D CreateWaterBlock()
	{
		var blockRoot = new Node3D { Name = "WaterBlock" };
		var totalSize = TileSize * TileCount;

		var pedestalMesh = new BoxMesh
		{
			Size = new Vector3(totalSize, WaterDepth, totalSize)
		};

		var pedestalMaterial = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.045f, 0.055f, 0.06f),
			Roughness = 0.82f
		};

		blockRoot.AddChild(new MeshInstance3D
		{
			Name = "Pedestal",
			Mesh = pedestalMesh,
			Position = new Vector3(0.0f, -WaterDepth * 0.5f - 0.08f, 0.0f),
			MaterialOverride = pedestalMaterial
		});

		var topMaterial = CreateWaterMaterial(surfaceMode: 0);
		for (var x = 0; x < TileCount; x++)
		{
			for (var z = 0; z < TileCount; z++)
			{
				var tileMesh = new PlaneMesh
				{
					Size = new Vector2(TileSize, TileSize),
					SubdivideWidth = TileSubdivisions,
					SubdivideDepth = TileSubdivisions
				};

				var tile = new MeshInstance3D
				{
					Name = $"TopTile_{x}_{z}",
					Mesh = tileMesh,
					Position = new Vector3(
						(x - (TileCount - 1) * 0.5f) * TileSize,
						0.0f,
						(z - (TileCount - 1) * 0.5f) * TileSize),
					MaterialOverride = topMaterial
				};
				blockRoot.AddChild(tile);
			}
		}

		var sideMaterial = CreateWaterMaterial(surfaceMode: 1);
		blockRoot.AddChild(CreateWall("FrontWall", totalSize, new Vector3(0.0f, -WaterDepth * 0.5f, totalSize * 0.5f), new Vector3(90.0f, 0.0f, 0.0f), sideMaterial));
		blockRoot.AddChild(CreateWall("BackWall", totalSize, new Vector3(0.0f, -WaterDepth * 0.5f, -totalSize * 0.5f), new Vector3(90.0f, 180.0f, 0.0f), sideMaterial));
		blockRoot.AddChild(CreateWall("LeftWall", totalSize, new Vector3(-totalSize * 0.5f, -WaterDepth * 0.5f, 0.0f), new Vector3(90.0f, 90.0f, 0.0f), sideMaterial));
		blockRoot.AddChild(CreateWall("RightWall", totalSize, new Vector3(totalSize * 0.5f, -WaterDepth * 0.5f, 0.0f), new Vector3(90.0f, -90.0f, 0.0f), sideMaterial));

		return blockRoot;
	}

	private static MeshInstance3D CreateWall(string name, float width, Vector3 position, Vector3 rotationDegrees, ShaderMaterial material)
	{
		var mesh = new PlaneMesh
		{
			Size = new Vector2(width, WaterDepth),
			SubdivideWidth = WallSubdivisions,
			SubdivideDepth = 8
		};

		return new MeshInstance3D
		{
			Name = name,
			Mesh = mesh,
			Position = position,
			RotationDegrees = rotationDegrees,
			MaterialOverride = material
		};
	}

	private static ShaderMaterial CreateWaterMaterial(int surfaceMode)
	{
		var shader = GD.Load<Shader>("res://Assets/Shaders/gerstner_water_block.gdshader");
		var material = new ShaderMaterial
		{
			Shader = shader
		};
		material.SetShaderParameter("surface_mode", surfaceMode);
		return material;
	}
}
