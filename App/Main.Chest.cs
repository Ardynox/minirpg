namespace MiniRPG;

public partial class Main
{
	private void OpenChestPanel(Item chestItem, ContainerSourceKind source, string? ownerActorId = null)
		=> _chestCoordinator.OpenChestPanel(chestItem, source, ownerActorId);

	private void CloseChestPanel()
		=> _chestCoordinator.CloseChestPanel();

	private void CheckChestRange()
		=> _chestCoordinator.CheckChestRange();

	private void OpenPutIntoChestSelection(Item chestItem)
		=> _chestCoordinator.OpenPutIntoChestSelection(chestItem);

	private void PersistOpenChestState(Item chestItem)
		=> _chestCoordinator.PersistOpenChestState(chestItem);

	private void TakeChestItem(Item chestItem, int itemIndex)
		=> _chestCoordinator.TakeChestItem(chestItem, itemIndex);

	private void TakeAllChestItems(Item chestItem)
		=> _chestCoordinator.TakeAllChestItems(chestItem);
}
