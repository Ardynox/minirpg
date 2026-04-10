namespace MiniRPG.Core.Multiplayer;

public enum TransportError
{
	Ok = 0,
	AlreadyInUse,
	InvalidParameter,
	CantResolve,
	CantCreate,
	CantConnect,
}
