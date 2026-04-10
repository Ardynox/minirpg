using System.Threading;
using static enet.ENet;

namespace MiniRPG.Module.Network;

internal static class ENetNativeLifetime
{
	private static int _refCount;

	public static TransportError Acquire()
	{
		var next = Interlocked.Increment(ref _refCount);
		if (next != 1)
			return TransportError.Ok;

		if (enet_initialize() != 0)
		{
			Interlocked.Decrement(ref _refCount);
			return TransportError.Failed;
		}

		return TransportError.Ok;
	}

	public static void Release()
	{
		var next = Interlocked.Decrement(ref _refCount);
		if (next > 0)
			return;

		if (next < 0)
		{
			Interlocked.Exchange(ref _refCount, 0);
			return;
		}

		enet_deinitialize();
	}
}
