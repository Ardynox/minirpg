using Godot;

public partial class WaterRiverFlowPreview : Node3D
{
	private const float TileSize = 1.5f;
	private const int TileCountX = 7;
	private const int TileCountZ = 4;
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
			Position = new Vector3(8.2f, 4.25f, 7.1f),
			Fov = 34.0f
		};
		camera.LookAtFromPosition(camera.Position, new Vector3(0.4f, -0.12f, 0.0f), Vector3.Up);
		return camera;
	}

	private static DirectionalLight3D CreateSunLight()
	{
		return new DirectionalLight3D
		{
			Name = "SunLight",
			RotationDegrees = new Vector3(-37.0f, -31.0f, 0.0f),
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
			RotationDegrees = new Vector3(-16.0f, 122.0f, 0.0f),
			LightColor = new Color(0.62f, 0.76f, 0.78f),
			LightEnergy = 0.42f,
			ShadowEnabled = false
		};
	}

	private static MeshInstance3D CreateGround()
	{
		var mesh = new PlaneMesh
		{
			Size = new Vector2(34.0f, 24.0f),
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
		material.SetShaderParameter("speed", 1.0f);
		material.SetShaderParameter("steepness", 0.52f);
		material.SetShaderParameter("wave_a", new Vector4(1.0f, 0.08f, 0.075f, 2.7f));
		material.SetShaderParameter("wave_b", new Vector4(0.92f, 0.24f, 0.04f, 1.45f));
		material.SetShaderParameter("wave_c", new Vector4(0.76f, -0.12f, 0.02f, 0.82f));
		material.SetShaderParameter("pool_depth", 0.0f);
		material.SetShaderParameter("river_half_width", 2.0f);
		material.SetShaderParameter("river_depth", 0.6f);
		material.SetShaderParameter("river_bank_softness", 0.6f);
		material.SetShaderParameter("meander_amp", 1.5f);
		material.SetShaderParameter("meander_freq", 0.3f);
		material.SetShaderParameter("foam_gain", 1.0f);
		material.SetShaderParameter("detail_strength", 0.24f);
		material.SetShaderParameter("flow_gradient_gain", 40.0f);
		material.SetShaderParameter("shallow_color", new Color(0.16f, 0.20f, 0.18f, 1.0f));
		material.SetShaderParameter("deep_color", new Color(0.06f, 0.09f, 0.11f, 1.0f));
		material.SetShaderParameter("foam_color", new Color(0.96f, 0.97f, 0.95f, 1.0f));
		material.SetShaderParameter("wall_color", new Color(0.07f, 0.09f, 0.10f, 1.0f));
		material.SetShaderParameter("specular_tint", new Color(1.0f, 0.98f, 0.94f, 1.0f));
		material.SetShaderParameter("sun_direction", new Vector3(-1.0f, 0.7f, 0.25f));
		material.SetShaderParameter("sun_color", new Vector3(5.0f, 4.25f, 2.5f));
		material.SetShaderParameter("sky_color", new Vector3(0.1f, 0.5f, 1.0f));
		material.SetShaderParameter("env_floor_color", new Vector3(0.3f, 0.2f, 0.2f));
		material.SetShaderParameter("fog_ext", new Vector3(0.03f, 0.045f, 0.045f));
		material.SetShaderParameter("fog_in", new Vector3(0.015f, 0.0135f, 0.012f));
		return material;
	}
}
