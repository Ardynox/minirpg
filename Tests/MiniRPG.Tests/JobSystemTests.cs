using System;
using MiniRPG.Core;
using MiniRPG.Core.AI;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Data;
using MiniRPG.Core.Facility;
using MiniRPG.Core.Job;
using MiniRPG.Core.World;
using Xunit;

namespace MiniRPG.Tests;

public class JobSchedulerTests
{
	private static GameState CreateTestState()
	{
		var state = new GameState();
		state.Turn = 0;
		state.WorldSeed = 42;
		return state;
	}

	private static Actor CreateWorker(string id, int x, int y, int z = 0)
	{
		var worker = new Actor
		{
			Id = id,
			DisplayName = id,
			X = x,
			Y = y,
			Z = z,
			Faction = Factions.Friendly,
			BrainId = WorkBrainIds.DomainWorker,
		};
		worker.Limbs.Add(new Limb { Id = "torso", Durability = 10, MaxDurability = 10 });
		return worker;
	}

	private static FacilityInstance CreateFacility(string id, string defId, int x, int y, FacilityStage stage)
	{
		return new FacilityInstance
		{
			Id = id,
			FacilityDefId = defId,
			AnchorX = x,
			AnchorY = y,
			Z = 0,
			Stage = stage,
			OwnerDomainId = DomainIds.Player,
			HitPoints = 20,
			MaxHitPoints = 20,
		};
	}

	[Fact]
	public void FindBestTicket_ReturnsNull_WhenNoTickets()
	{
		var state = CreateTestState();
		var worker = CreateWorker("w1", 5, 5);
		state.Actors[worker.Id] = worker;

		var ticket = JobScheduler.FindBestTicket(state, worker);
		Assert.Null(ticket);
	}

	[Fact]
	public void FindBestTicket_ReturnsHighestPriority()
	{
		var state = CreateTestState();
		var worker = CreateWorker("w1", 5, 5);
		state.Actors[worker.Id] = worker;

		// 注册一个简单设施定义
		FacilityRegistry.Clear();
		FacilityRegistry.Register(new FacilityDef { Id = "test_facility", Name = "Test" });

		var facility = CreateFacility("f1", "test_facility", 6, 5, FacilityStage.Construct);
		state.Facilities[facility.Id] = facility;

		var board = state.JobBoardState;
		var lowId = board.AllocateTicketId();
		board.Tickets[lowId] = new WorkTicket
		{
			Id = lowId,
			Type = WorkTicketType.HaulFacilityOutput,
			Priority = 10,
			FacilityId = "f1",
		};

		var highId = board.AllocateTicketId();
		board.Tickets[highId] = new WorkTicket
		{
			Id = highId,
			Type = WorkTicketType.ConstructFacility,
			Priority = 60,
			FacilityId = "f1",
		};

		// 跳过自动重建
		board.LastRebuildTurn = state.Turn;

		var ticket = JobScheduler.FindBestTicket(state, worker);
		Assert.NotNull(ticket);
		Assert.Equal(highId, ticket.Id);
	}

	[Fact]
	public void TryReserve_SetsReservation()
	{
		var state = CreateTestState();
		var worker = CreateWorker("w1", 5, 5);

		var ticket = new WorkTicket { Id = "t1", FacilityId = "f1" };
		Assert.True(JobScheduler.TryReserve(state, ticket, worker));
		Assert.Equal("w1", ticket.ReservedByActorId);
	}

	[Fact]
	public void TryReserve_FailsWhenReservedByOther()
	{
		var state = CreateTestState();
		var worker1 = CreateWorker("w1", 5, 5);
		var worker2 = CreateWorker("w2", 6, 6);

		var ticket = new WorkTicket { Id = "t1", FacilityId = "f1", ReservedByActorId = "w1" };
		Assert.False(JobScheduler.TryReserve(state, ticket, worker2));
	}

	[Fact]
	public void GetReservedTicket_ReturnsCorrectTicket()
	{
		var state = CreateTestState();
		var worker = CreateWorker("w1", 5, 5);

		var board = state.JobBoardState;
		var id = board.AllocateTicketId();
		board.Tickets[id] = new WorkTicket
		{
			Id = id,
			FacilityId = "f1",
			ReservedByActorId = "w1",
		};

		var reserved = JobScheduler.GetReservedTicket(state, worker);
		Assert.NotNull(reserved);
		Assert.Equal(id, reserved.Id);
	}

	[Fact]
	public void ReleaseAll_ClearsAllReservationsForActor()
	{
		var state = CreateTestState();
		var board = state.JobBoardState;

		board.Tickets["t1"] = new WorkTicket { Id = "t1", FacilityId = "f1", ReservedByActorId = "w1" };
		board.Tickets["t2"] = new WorkTicket { Id = "t2", FacilityId = "f2", ReservedByActorId = "w1" };
		board.Tickets["t3"] = new WorkTicket { Id = "t3", FacilityId = "f3", ReservedByActorId = "w2" };

		JobScheduler.ReleaseAll(state, "w1");

		Assert.Equal(string.Empty, board.Tickets["t1"].ReservedByActorId);
		Assert.Equal(string.Empty, board.Tickets["t2"].ReservedByActorId);
		Assert.Equal("w2", board.Tickets["t3"].ReservedByActorId);
	}

	[Fact]
	public void CompleteTicket_RemovesFromBoard()
	{
		var state = CreateTestState();
		var board = state.JobBoardState;
		board.Tickets["t1"] = new WorkTicket { Id = "t1", FacilityId = "f1" };

		JobScheduler.CompleteTicket(state, "t1");
		Assert.Empty(board.Tickets);
	}

	[Fact]
	public void FindBestTicket_SkipsReservedByOther()
	{
		var state = CreateTestState();
		var worker = CreateWorker("w1", 5, 5);
		state.Actors[worker.Id] = worker;

		FacilityRegistry.Clear();
		FacilityRegistry.Register(new FacilityDef { Id = "test_facility", Name = "Test" });

		var facility = CreateFacility("f1", "test_facility", 6, 5, FacilityStage.Active);
		state.Facilities[facility.Id] = facility;

		var board = state.JobBoardState;
		board.LastRebuildTurn = state.Turn;

		var id = board.AllocateTicketId();
		board.Tickets[id] = new WorkTicket
		{
			Id = id,
			Type = WorkTicketType.ConstructFacility,
			Priority = 60,
			FacilityId = "f1",
			ReservedByActorId = "other_worker",
		};

		var ticket = JobScheduler.FindBestTicket(state, worker);
		Assert.Null(ticket);
	}

	[Fact]
	public void FindBestTicket_SkipsTooFarFacility()
	{
		var state = CreateTestState();
		var worker = CreateWorker("w1", 0, 0);
		state.Actors[worker.Id] = worker;

		FacilityRegistry.Clear();
		FacilityRegistry.Register(new FacilityDef { Id = "test_facility", Name = "Test" });

		// 设施在 (100, 100)，超出搜索范围
		var facility = CreateFacility("f1", "test_facility", 100, 100, FacilityStage.Construct);
		state.Facilities[facility.Id] = facility;

		var board = state.JobBoardState;
		board.LastRebuildTurn = state.Turn;

		var id = board.AllocateTicketId();
		board.Tickets[id] = new WorkTicket
		{
			Id = id,
			Type = WorkTicketType.ConstructFacility,
			Priority = 60,
			FacilityId = "f1",
		};

		var ticket = JobScheduler.FindBestTicket(state, worker);
		Assert.Null(ticket);
	}
}

public class JobBehaviorModuleTests
{
	[Fact]
	public void IsWorker_ReturnsTrueForDomainWorker()
	{
		var actor = new Actor { BrainId = WorkBrainIds.DomainWorker };
		Assert.Equal(WorkBrainIds.DomainWorker, actor.BrainId);
	}

	[Fact]
	public void IsWorker_ReturnsFalseForSimpleBrain()
	{
		var actor = new Actor { BrainId = "simple" };
		Assert.False(string.Equals(actor.BrainId, WorkBrainIds.DomainWorker, StringComparison.Ordinal));
	}
}
