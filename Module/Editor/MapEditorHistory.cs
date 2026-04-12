using System.Collections.Generic;
using MiniRPG.Core.World;

namespace MiniRPG.Module.Editor;

public sealed class MapEditorHistory
{
	private const int MaxUndoCapacity = 500;

	private readonly Stack<IMapEditorCommand> _undoStack = new();
	private readonly Stack<IMapEditorCommand> _redoStack = new();

	public bool CanUndo => _undoStack.Count > 0;
	public bool CanRedo => _redoStack.Count > 0;
	public int UndoCount => _undoStack.Count;

	public void Execute(IMapEditorCommand command, WorldMap world)
	{
		command.Execute(world);
		_undoStack.Push(command);
		_redoStack.Clear();

		if (_undoStack.Count > MaxUndoCapacity)
			TrimOldest();
	}

	public bool Undo(WorldMap world)
	{
		if (_undoStack.Count == 0) return false;

		var command = _undoStack.Pop();
		command.Undo(world);
		_redoStack.Push(command);
		return true;
	}

	public bool Redo(WorldMap world)
	{
		if (_redoStack.Count == 0) return false;

		var command = _redoStack.Pop();
		command.Execute(world);
		_undoStack.Push(command);
		return true;
	}

	public void Clear()
	{
		_undoStack.Clear();
		_redoStack.Clear();
	}

	private void TrimOldest()
	{
		var temp = new Stack<IMapEditorCommand>();
		var keep = MaxUndoCapacity - 50;
		while (_undoStack.Count > 0 && temp.Count < keep)
			temp.Push(_undoStack.Pop());
		_undoStack.Clear();
		while (temp.Count > 0)
			_undoStack.Push(temp.Pop());
	}
}
