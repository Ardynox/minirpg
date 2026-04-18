using Godot;

public partial class WaterRiverFlowPreview : Node3D
{
	private const float TileSize = 1.45f;
	private const int TileCountX = 5;
	private const int TileCountZ = 2;
	private const float WaterDepth = 1.3f;
	private const int TileSubdivisions = 52;
	private const int WallSubdivisions = 96;

	public override void _Ready()
	{
		AddChild(CreateEnvironment());
		AddChild(CreateCamera());
		AddChild(CreateSunLight());
		AddChild(CreateFillLight());
		AddChild(CreateGround());
		AddChild(CreateRiverBlock());
	}

	private static WorldEnvironment CreateEnvironment()
	{
		var environment = new Godot.Environment
		{
			BackgroundMode = Godot.Environment.BGMode.Color,
			BackgroundColor = new Color(0.05f, 0.07f, 0.08f),
			AmbientLightSource = Godot.Environment.AmbientSource.Color,
			AmbientLightColor = new Color(0.77f, 0.84f, 0.82f),
			AmbientLightEnergy = 1.05f,
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
			Position = new Vector3(5.8f, 3.65f, 4.4f),
			Fov = 36.0f
		};
		camera.LookAtFromPosition(camera.Position, new Vector3(0.2f, -0.1f, 0.0f), Vector3.Up);
		return camera;
	}

	private static DirectionalLight3D CreateSunLight()
	{
		return new DirectionalLight3D
		{
			Name = "SunLight",
			RotationDegrees = new Vector3(-41.0f, -38.0f, 0.0f),
			LightColor = new Color(1.0f, 0.91f, 0.79f),
			LightEnergy = 1.55f,
			ShadowEnabled = true
		};
	}

	private static DirectionalLight3D CreateFillLight()
	{
		return new DirectionalLight3D
		{
			Name = "FillLight",
			RotationDegrees = new Vector3(-18.0f, 128.0f, 0.0f),
			LightColor = new Color(0.62f, 0.76f, 0.78f),
			LightEnergy = 0.42f,
			ShadowEnabled = false
		};
	}

	private static MeshInstance3D CreateGround()
	{
		var mesh = new PlaneMesh
		{
			Size = new Vector2(28.0f, 18.0f),
			SubdivideWidth = 2,
			SubdivideDepth = 2
		};

		var material = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.10f, 0.11f, 0.10f),
			Roughness = 0.98f
		};

		return new MeshInstance3D
		{
			Name = "Ground",
			Mesh = mesh,
			Position = new Vector3(0.0f, -WaterDepth - 0.42f, 0.0f),
			MaterialOverride = material
		};
	}

	private Node3D CreateRiverBlock()
	{
		var root = new Node3D { Name = "RiverBlock" };
		var totalSizeX = TileSize * TileCountX;
		var totalSizeZ = TileSize * TileCountZ;

		var pedestalMesh = new BoxMesh
		{
			Size = new Vector3(totalSizeX, WaterDepth, totalSizeZ)
		};

		var pedestalMaterial = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.045f, 0.055f, 0.06f),
			Roughness = 0.84f
		};

		root.AddChild(new MeshInstance3D
		{
			Name = "Pedestal",
			Mesh = pedestalMesh,
			Position = new Vector3(0.0f, -WaterDepth * 0.5f - 0.06f, 0.0f),
			MaterialOverride = pedestalMaterial
		});

		var topMaterial = CreateWaterMaterial(surfaceMode: 0);
		for (var x = 0; x < TileCountX; x++)
		{
			for (var z = 0; z < TileCountZ; z++)
			{
				var tileMesh = new PlaneMesh
				{
					Size = new Vector2(TileSize, TileSize),
					SubdivideWidth = TileSubdivisions,
					SubdivideDepth = TileSubdivisions
				};

				var tile = new MeshInstance3D
				{
					Name = $"RiverTile_{x}_{z}",
					Mesh = tileMesh,
					Position = new Vector3(
						(x - (TileCountX - 1) * 0.5f) * TileSize,
						0.0f,
						(z - (TileCountZ - 1) * 0.5f) * TileSize),
					MaterialOverride = topMaterial
				};
				root.AddChild(tile);
			}
		}

		var sideMaterial = CreateWaterMaterial(surfaceMode: 1);
		root.AddChild(CreateWall("FrontWall", totalSizeX, WaterDepth, new Vector3(0.0f, -WaterDepth * 0.5f, totalSizeZ * 0.5f), new Vector3(90.0f, 0.0f, 0.0f), sideMaterial));
		root.AddChild(CreateWall("BackWall", totalSizeX, WaterDepth, new Vector3(0.0f, -WaterDepth * 0.5f, -totalSizeZ * 0.5f), new Vector3(90.0f, 180.0f, 0.0f), sideMaterial));
		root.AddChild(CreateWall("LeftWall", totalSizeZ, WaterDepth, new Vector3(-totalSizeX * 0.5f, -WaterDepth * 0.5f, 0.0f), new Vector3(90.0f, 90.0f, 0.0f), sideMaterial));
		root.AddChild(CreateWall("RightWall", totalSizeZ, WaterDepth, new Vector3(totalSizeX * 0.5f, -WaterDepth * 0.5f, 0.0f), new Vector3(90.0f, -90.0f, 0.0f), sideMaterial));

		return root;
	}

	private static MeshInstance3D CreateWall(string name, float width, float height, Vector3 position, Vector3 rotationDegrees, ShaderMaterial material)
	{
		var mesh = new PlaneMesh
		{
			Size = new Vector2(width, height),
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
		var shader = GD.Load<Shader>("res://Assets/Shaders/river_flow_water_block.gdshader");
		var material = new ShaderMaterial
		{
			Shader = shader
		};
		material.SetShaderParameter("surface_mode", surfaceMode);
		return material;
	}
}
