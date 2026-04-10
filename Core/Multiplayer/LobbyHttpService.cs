using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MiniRPG.Core.Multiplayer;

public sealed class LobbyHttpService : IDisposable
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		WriteIndented = true,
		Converters =
		{
			new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
		},
	};

	private readonly HttpListener _listener = new();
	private readonly ILobbyService _lobby;
	private CancellationTokenSource? _cts;
	private Task? _loopTask;

	public LobbyHttpService(ILobbyService lobby, params string[] prefixes)
	{
		_lobby = lobby ?? throw new ArgumentNullException(nameof(lobby));
		foreach (var prefix in prefixes)
			_listener.Prefixes.Add(prefix);
	}

	public bool IsRunning => _listener.IsListening;

	public void Start(CancellationToken cancellationToken = default)
	{
		if (_listener.Prefixes.Count == 0)
			throw new InvalidOperationException("At least one HTTP prefix is required.");
		if (_listener.IsListening)
			return;

		_cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		_listener.Start();
		_loopTask = Task.Run(() => AcceptLoopAsync(_cts.Token), _cts.Token);
	}

	public async Task StopAsync()
	{
		if (!_listener.IsListening)
			return;

		_cts?.Cancel();
		_listener.Stop();
		if (_loopTask != null)
			await _loopTask.ConfigureAwait(false);
	}

	public void Dispose()
	{
		if (_listener.IsListening)
			_listener.Stop();
		_listener.Close();
		_cts?.Dispose();
	}

	private async Task AcceptLoopAsync(CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested && _listener.IsListening)
		{
			HttpListenerContext context;
			try
			{
				context = await _listener.GetContextAsync().ConfigureAwait(false);
			}
			catch (HttpListenerException) when (!_listener.IsListening)
			{
				break;
			}
			catch (ObjectDisposedException)
			{
				break;
			}

			_ = Task.Run(() => HandleRequestSafeAsync(context), cancellationToken);
		}
	}

	private async Task HandleRequestSafeAsync(HttpListenerContext context)
	{
		try
		{
			await HandleRequestAsync(context).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			await WriteJsonAsync(context.Response, HttpStatusCode.InternalServerError, new
			{
				error = ex.Message,
			}).ConfigureAwait(false);
		}
		finally
		{
			context.Response.OutputStream.Close();
		}
	}

	private async Task HandleRequestAsync(HttpListenerContext context)
	{
		var request = context.Request;
		var path = request.Url?.AbsolutePath?.TrimEnd('/') ?? string.Empty;
		var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

		if (request.HttpMethod == "GET" && segments.Length == 1 && string.Equals(segments[0], "healthz", StringComparison.OrdinalIgnoreCase))
		{
			await WriteJsonAsync(context.Response, HttpStatusCode.OK, new
			{
				ok = true,
			}).ConfigureAwait(false);
			return;
		}

		if (request.HttpMethod == "GET" && segments.Length == 1 && string.Equals(segments[0], "rooms", StringComparison.OrdinalIgnoreCase))
		{
			await WriteJsonAsync(context.Response, HttpStatusCode.OK, _lobby.ListRooms()).ConfigureAwait(false);
			return;
		}

		if (request.HttpMethod == "POST" && segments.Length == 1 && string.Equals(segments[0], "rooms", StringComparison.OrdinalIgnoreCase))
		{
			var payload = await ReadJsonAsync<LobbyCreateRoomRequest>(request).ConfigureAwait(false);
			await WriteJsonAsync(context.Response, HttpStatusCode.OK, _lobby.CreateRoom(payload)).ConfigureAwait(false);
			return;
		}

		if (request.HttpMethod == "GET" && segments.Length == 3
			&& string.Equals(segments[0], "rooms", StringComparison.OrdinalIgnoreCase)
			&& string.Equals(segments[1], "resolve", StringComparison.OrdinalIgnoreCase))
		{
			await WriteJsonAsync(context.Response, HttpStatusCode.OK, _lobby.ResolveRoomCode(segments[2])).ConfigureAwait(false);
			return;
		}

		if (request.HttpMethod == "POST" && segments.Length == 3
			&& string.Equals(segments[0], "rooms", StringComparison.OrdinalIgnoreCase)
			&& string.Equals(segments[2], "join", StringComparison.OrdinalIgnoreCase))
		{
			var payload = await ReadJsonAsync<LobbyJoinRoomRequest>(request).ConfigureAwait(false);
			payload.RoomId = segments[1];
			await WriteJsonAsync(context.Response, HttpStatusCode.OK, _lobby.JoinRoom(payload)).ConfigureAwait(false);
			return;
		}

		if (request.HttpMethod == "POST" && segments.Length == 3
			&& string.Equals(segments[0], "rooms", StringComparison.OrdinalIgnoreCase)
			&& string.Equals(segments[2], "reconnect", StringComparison.OrdinalIgnoreCase))
		{
			var payload = await ReadJsonAsync<LobbyReconnectClaimRequest>(request).ConfigureAwait(false);
			payload.RoomId = segments[1];
			await WriteJsonAsync(context.Response, HttpStatusCode.OK, _lobby.ReconnectClaim(payload)).ConfigureAwait(false);
			return;
		}

		await WriteJsonAsync(context.Response, HttpStatusCode.NotFound, new
		{
			error = "Unknown lobby endpoint.",
		}).ConfigureAwait(false);
	}

	private static async Task<T> ReadJsonAsync<T>(HttpListenerRequest request)
	{
		using var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8);
		var json = await reader.ReadToEndAsync().ConfigureAwait(false);
		return JsonSerializer.Deserialize<T>(json, JsonOptions)
			?? throw new InvalidOperationException($"Failed to deserialize {typeof(T).Name}.");
	}

	private static async Task WriteJsonAsync(HttpListenerResponse response, HttpStatusCode statusCode, object payload)
	{
		response.StatusCode = (int)statusCode;
		response.ContentType = "application/json; charset=utf-8";
		var json = JsonSerializer.Serialize(payload, JsonOptions);
		var bytes = Encoding.UTF8.GetBytes(json);
		response.ContentLength64 = bytes.Length;
		await response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
	}
}
