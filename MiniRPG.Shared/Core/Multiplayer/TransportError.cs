namespace MiniRPG.Core.Multiplayer;

public enum TransportError
{
	Ok = 0,
	Failed,
	AlreadyInUse,
	InvalidParameter,
	CantResolve,
	CantCreate,
	CantConnect,
}
