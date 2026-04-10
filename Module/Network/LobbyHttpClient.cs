using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MiniRPG.Core.Multiplayer;

namespace MiniRPG.Module.Network;

public interface ILobbyClient
{
	Task<IReadOnlyList<LobbyRoomSummary>> ListRoomsAsync(CancellationToken cancellationToken = default);
	Task<LobbyJoinTicket> CreateRoomAsync(LobbyCreateRoomRequest request, CancellationToken cancellationToken = default);
	Task<LobbyRoomResolution> ResolveRoomCodeAsync(string roomCode, CancellationToken cancellationToken = default);
	Task<LobbyJoinTicket> JoinRoomAsync(LobbyJoinRoomRequest request, CancellationToken cancellationToken = default);
	Task<LobbyJoinTicket> ReconnectClaimAsync(LobbyReconnectClaimRequest request, CancellationToken cancellationToken = default);
	Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default);
}

public sealed class LobbyHttpClient : ILobbyClient, IDisposable
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		Converters =
		{
			new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
		},
	};

	private readonly HttpClient _httpClient;
	private readonly bool _ownsHttpClient;

	public LobbyHttpClient(string baseAddress, HttpClient? httpClient = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(baseAddress);

		if (httpClient == null)
		{
			_httpClient = new HttpClient();
			_ownsHttpClient = true;
		}
		else
		{
			_httpClient = httpClient;
		}

		_httpClient.BaseAddress = new Uri(NormalizeBaseAddress(baseAddress), UriKind.Absolute);
	}

	public Task<IReadOnlyList<LobbyRoomSummary>> ListRoomsAsync(CancellationToken cancellationToken = default) =>
		SendAsync<IReadOnlyList<LobbyRoomSummary>>(HttpMethod.Get, "rooms", payload: null, cancellationToken);

	public Task<LobbyJoinTicket> CreateRoomAsync(LobbyCreateRoomRequest request, CancellationToken cancellationToken = default) =>
		SendAsync<LobbyJoinTicket>(HttpMethod.Post, "rooms", request, cancellationToken);

	public Task<LobbyRoomResolution> ResolveRoomCodeAsync(string roomCode, CancellationToken cancellationToken = default) =>
		SendAsync<LobbyRoomResolution>(HttpMethod.Get, $"rooms/resolve/{Uri.EscapeDataString(roomCode)}", payload: null, cancellationToken);

	public Task<LobbyJoinTicket> JoinRoomAsync(LobbyJoinRoomRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		return SendAsync<LobbyJoinTicket>(
			HttpMethod.Post,
			$"rooms/{Uri.EscapeDataString(request.RoomId)}/join",
			request,
			cancellationToken);
	}

	public Task<LobbyJoinTicket> ReconnectClaimAsync(LobbyReconnectClaimRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		return SendAsync<LobbyJoinTicket>(
			HttpMethod.Post,
			$"rooms/{Uri.EscapeDataString(request.RoomId)}/reconnect",
			request,
			cancellationToken);
	}

	public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
	{
		using var request = new HttpRequestMessage(HttpMethod.Get, "healthz");
		using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
		return response.IsSuccessStatusCode;
	}

	public void Dispose()
	{
		if (_ownsHttpClient)
			_httpClient.Dispose();
	}

	private async Task<T> SendAsync<T>(
		HttpMethod method,
		string relativePath,
		object? payload,
		CancellationToken cancellationToken)
	{
		using var request = new HttpRequestMessage(method, relativePath);
		if (payload != null)
		{
			var json = JsonSerializer.Serialize(payload, JsonOptions);
			request.Content = new StringContent(json, Encoding.UTF8, "application/json");
		}

		using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
		var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
		if (!response.IsSuccessStatusCode)
			throw new LobbyHttpException(response.StatusCode, body);

		var value = JsonSerializer.Deserialize<T>(body, JsonOptions);
		if (value == null)
			throw new InvalidDataException($"Failed to deserialize lobby response into {typeof(T).Name}.");

		return value;
	}

	private static string NormalizeBaseAddress(string baseAddress) =>
		baseAddress.EndsWith("/", StringComparison.Ordinal)
			? baseAddress
			: $"{baseAddress}/";
}

public sealed class LobbyHttpException : Exception
{
	public LobbyHttpException(System.Net.HttpStatusCode statusCode, string responseBody)
		: base($"Lobby request failed with {(int)statusCode} {statusCode}.")
	{
		StatusCode = statusCode;
		ResponseBody = responseBody;
	}

	public System.Net.HttpStatusCode StatusCode { get; }
	public string ResponseBody { get; }
}
