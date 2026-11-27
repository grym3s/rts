// Slice 4: host lifecycle + authoritative rendering + input->command conversion.
// Scenario roster/positions/colors are presentation fixtures ONLY; the host owns every
// rule. On any host/protocol failure we stop stepping and surface the error — never
// fabricate state.
#include "RtsBridgeGameMode.h"
#include "RtsSimHostBridge.h"
#include "RtsSimUnitActor.h"
#include "RtsRtsPlayerController.h"
#include "RtsMinimapHud.h"
#include "RtsContentLoader.h"
#include "RtsTerrainActor.h"
#include "RtsVeinActor.h"
#include "RtsExplosionActor.h"
#include "Engine/World.h"
#include "Engine/StaticMeshActor.h"
#include "Engine/DirectionalLight.h"
#include "Components/DirectionalLightComponent.h"
#include "Components/LightComponent.h"
#include "Engine/SkyLight.h"
#include "Components/SkyLightComponent.h"
#include "Components/StaticMeshComponent.h"
#include "Materials/MaterialInstanceDynamic.h"
#include "Kismet/GameplayStatics.h"
#include "Kismet/KismetSystemLibrary.h"
#include "HAL/PlatformTime.h"
#include "Engine/Engine.h"

// The match comes from content/ (MatchScenarioId() / MapId): the scenario minus its scripted
// commands becomes the host init; the typed tile map is drawn. Rules stay host-owned.

// Design scale: 100 UU per map cell.
static constexpr float SimCellUU = ARtsBridgeGameMode::CellUU;

int32 ARtsBridgeGameMode::MapCellsW = 32;
int32 ARtsBridgeGameMode::MapCellsH = 24;
int32 ARtsBridgeGameMode::PlayerFaction = 0;

ARtsBridgeGameMode::ARtsBridgeGameMode()
{
	// Set here, not in DefaultEngine.ini: GlobalDefaultServerPlayerControllerClass is the
	// *server* default and is ignored for the local player, which then got the engine's
	// free-fly DefaultPawn + plain PlayerController (mouse-look camera, no cursor).
	PlayerControllerClass = ARtsRtsPlayerController::StaticClass();
	HUDClass = ARtsMinimapHud::StaticClass();
	DefaultPawnClass = nullptr; // RTS: no avatar; the controller drives its own camera actor
	// Must be set in the constructor: tick registration happens before StartPlay, so
	// setting it there left the game mode never ticking (no host steps, no units).
	PrimaryActorTick.bCanEverTick = true;
	PrimaryActorTick.bStartWithTickEnabled = true;
}

void ARtsBridgeGameMode::StartPlay()
{
	Super::StartPlay();

	// --- Match content: scenario (host init) + typed tile map (what we draw). ---
	FRtsMatchSetup Setup;
	FString LoadErr;
	if (!RtsContent::LoadMatch(MatchScenarioId(), MapId, Setup, LoadErr))
	{
		FailHost(FString::Printf(TEXT("CONTENT LOAD FAILED: %s"), *LoadErr));
		return;
	}
	MapCellsW = Setup.Width;
	MapCellsH = Setup.Height;
	TerrainRows = Setup.Rows;
	if (!Setup.bTypedMap)
		UE_LOG(LogTemp, Warning, TEXT("RtsBridge: no typed map '%s' matching the scenario; drawing blocked cells only"), MapId);

	// --- Battlefield: tiles (water plane + land/cliff/bridge boxes) + sun. ---
	FActorSpawnParameters SP;
	SP.Name = TEXT("Battlefield");
	TerrainActor = GetWorld()->SpawnActor<ARtsTerrainActor>(FVector::ZeroVector, FRotator::ZeroRotator, SP);
	if (TerrainActor)
		TerrainActor->Build(TerrainRows, SimCellUU);
	FActorSpawnParameters VP;
	VP.Name = TEXT("Veins");
	VeinActor = GetWorld()->SpawnActor<ARtsVeinActor>(FVector::ZeroVector, FRotator::ZeroRotator, VP);
	// Lights spawned at runtime into an unbuilt level must be Movable to light anything.
	if (ADirectionalLight* Sun = GetWorld()->SpawnActor<ADirectionalLight>(FVector::ZeroVector, FRotator(-45.0f, 30.0f, 0.0f)))
	{
		Sun->GetLightComponent()->SetMobility(EComponentMobility::Movable);
		Sun->GetLightComponent()->SetIntensity(2.5f); // 8 blew out to white with fixed exposure
	}
	if (ASkyLight* Sky = GetWorld()->SpawnActor<ASkyLight>(FVector::ZeroVector, FRotator::ZeroRotator))
		Sky->GetLightComponent()->SetMobility(EComponentMobility::Movable);

	// --- Player: RTS camera + RTS input scheme on the default player. ---
	if (APlayerController* PC = UGameplayStatics::GetPlayerController(this, 0))
	{
		ARtsRtsPlayerController* RtsPC = Cast<ARtsRtsPlayerController>(PC);
		// The default class was set via DefaultEngine.ini; if not, log rather than crash.
		if (RtsPC)
		{
			RtsPC->RefreshMapBounds(); // the map size is only known now (loaded above)
		}
		else
		{
			UE_LOG(LogTemp, Warning, TEXT("RtsBridge: PlayerController is not ARtsRtsPlayerController — check DefaultEngine.ini"));
		}
	}

	// --- Host: launch + init. Failure is reported, not faked. ---
	Bridge = NewObject<URtsSimHostBridge>(this);
	FString Err;
	InitRequest = Setup.InitRequest;
	if (!Bridge->StartAndInit(InitRequest, Err))
	{
		FailHost(FString::Printf(TEXT("HOST START FAILED: %s"), *Err));
		return;
	}
	bHostAlive = true;
	BuildingTypes = Bridge->GetInitReply().BuildingTypes; // T36 catalogs (static content)
	UnitTypes = Bridge->GetInitReply().UnitTypes;
	bChoosingFaction = CanRestart(); // single player: pick a side first (the sim waits)
	UE_LOG(LogTemp, Display, TEXT("RTS_SESSION: host up, stepping live (select: LMB click/drag, Shift adds, Ctrl+A, groups Ctrl+1-3/1-3, move: RMB (enemy = attack), attack-move: F+LMB, stop: X, minimap: bottom-left)"));
}

void ARtsBridgeGameMode::Tick(float DeltaSeconds)
{
	Super::Tick(DeltaSeconds);
	if (bFinished || !Bridge || !bHostAlive || bChoosingFaction)
		return;

	// Fixed sim cadence: 20 ticks/s (SimWorld.TicksPerSecond). Catch up after a hitch, but
	// at most MaxStepsPerFrame: if steps ever cost more than real time, the sim slows down
	// instead of the frame loop spiralling into a freeze.
	static constexpr double TickDt = 1.0 / 20.0;
	static constexpr int32 MaxStepsPerFrame = 3;
	StepAccum = FMath::Min(StepAccum + DeltaSeconds, TickDt * MaxStepsPerFrame);
	while (StepAccum >= TickDt && !bFinished && bHostAlive)
	{
		StepAccum -= TickDt;
		FString Commands = TEXT("[]");
		if (PendingCommandBodies.Num() > 0)
		{
			// Stamp every queued command with the tick we are about to execute.
			FString Joined;
			for (const FString& Body : PendingCommandBodies)
			{
				if (Joined.Len())
					Joined += TEXT(",");
				Joined += FString::Printf(TEXT("{\"tick\":%d,%s}"), NextTickToSend, *Body);
			}
			Commands = FString::Printf(TEXT("[%s]"), *Joined);
			PendingCommandBodies.Reset();
		}
		// T20: ask for the debug block only while an overlay is on (it costs host time).
		const FString Step = FString::Printf(
			TEXT("{\"protocolVersion\":1,\"kind\":\"step\",\"tick\":%d,\"commands\":%s%s}"), NextTickToSend, *Commands,
			DebugMask != 0 ? TEXT(",\"debug\":true") : TEXT(""));
		FRtsHostReply Reply;
		if (!Bridge->Step(Step, 5.0, Reply))
		{
			FailHost(FString::Printf(TEXT("HOST FAILURE at tick %d: %s"), NextTickToSend, *Reply.Error));
			return;
		}
		if (!Reply.bOk)
		{
			FailHost(FString::Printf(TEXT("PROTOCOL ERROR at tick %d: %s"), NextTickToSend, *Reply.Error));
			return;
		}
		NextTickToSend++;
		SyncActorsFromReply(Reply);
		RoundTripMsAvg = FMath::Lerp(RoundTripMsAvg, Reply.RoundTripMs, 0.1);
		if (Reply.bHasDebug)
		{
			StepMsAvg = FMath::Lerp(StepMsAvg, Reply.StepMicros / 1000.0, 0.1);
			DebugById.Reset();
			for (FRtsDebugUnit& D : Reply.DebugUnits)
				DebugById.Add(D.Id, MoveTemp(D));
		}
		if (Reply.bMatchOver && !bMatchReported)
		{
			// T06 result -> T19 screen (shown by the HUD after MatchScreenDelay).
			bMatchReported = true;
			MatchWinner = Reply.MatchWinner;
			MatchEndTick = Reply.Tick;
			MatchOverRealTime = FPlatformTime::Seconds();
			const TCHAR* Msg = MatchWinner == PlayerFaction ? TEXT("VICTORY") : MatchWinner < 0 ? TEXT("DRAW") : TEXT("DEFEAT");
			UE_LOG(LogTemp, Display, TEXT("RTS_MATCH_OVER: %s (winner %d, tick %lld)"), Msg, MatchWinner, MatchEndTick);
		}
	}
}

bool ARtsBridgeGameMode::QueueOrderForSelection(const TCHAR* Type, const FString& TargetJson, bool bSkipHarvesters)
{
	if (!bHostAlive || Selection.GetSelected().Num() == 0)
		return false;
	if (IsMatchScreenUp())
		return false; // T19: the result screen owns input
	FString Ids;
	for (const int32 Id : Selection.GetSelected())
	{
		if (IsBuildingId(Id))
			continue; // T37: buildings take rally points, not unit orders
		if (bSkipHarvesters)
			if (const FRtsUnitSnapshot* HR = RowsById.Find(Id); HR && HR->bHarvester)
				continue; // T38: they got a `harvest` instead
		if (Ids.Len())
			Ids += TEXT(",");
		Ids += FString::Printf(TEXT("%d"), Id);
	}
	if (Ids.IsEmpty())
		return false;
	// Tick-less body; Tick() wraps it as {"tick":N, <body>} when it sends.
	FString Body = FString::Printf(TEXT("\"faction\":%d,\"type\":\"%s\",\"units\":[%s]"), PlayerFaction, Type, *Ids);
	if (!TargetJson.IsEmpty())
		Body += TEXT(",\"target\":") + TargetJson;
	PendingCommandBodies.Add(MoveTemp(Body));
	return true;
}

static FString MapPointJson(const FVector& World)
{
	return FString::Printf(TEXT("[%.4f,%.4f]"), World.X / SimCellUU, World.Y / SimCellUU);
}

bool ARtsBridgeGameMode::RequestMoveTo(const FVector& WorldDestination)
{
	return QueueOrderForSelection(TEXT("move"), MapPointJson(WorldDestination));
}

bool ARtsBridgeGameMode::RequestAttackMoveTo(const FVector& WorldDestination)
{
	return QueueOrderForSelection(TEXT("attack-move"), MapPointJson(WorldDestination));
}

bool ARtsBridgeGameMode::RequestAttackUnit(int32 TargetSimId)
{
	// Host contract: `attack` target is an integer entity id, never a coordinate.
	return QueueOrderForSelection(TEXT("attack"), FString::Printf(TEXT("%d"), TargetSimId));
}

bool ARtsBridgeGameMode::RequestStop()
{
	return QueueOrderForSelection(TEXT("stop"), FString());
}

int32 ARtsBridgeGameMode::PickOwnUnitAt(const FVector2D& ScreenPos) const
{
	return PickUnitAt(ScreenPos, /*bOwn=*/true);
}

int32 ARtsBridgeGameMode::PickEnemyUnitAt(const FVector2D& ScreenPos) const
{
	return PickUnitAt(ScreenPos, /*bOwn=*/false);
}

int32 ARtsBridgeGameMode::PickUnitAt(const FVector2D& ScreenPos, bool bOwn) const
{
	return PickUnitImpl(ScreenPos, bOwn ? 0 : 1);
}

int32 ARtsBridgeGameMode::PickAnyUnitAt(const FVector2D& ScreenPos) const
{
	return PickUnitImpl(ScreenPos, 2);
}

bool ARtsBridgeGameMode::ScreenToGround(const FVector2D& ScreenPos, FVector& Out) const
{
	APlayerController* PC = UGameplayStatics::GetPlayerController(this, 0);
	FVector Origin, Dir;
	if (!PC || !PC->DeprojectScreenPositionToWorld(ScreenPos.X, ScreenPos.Y, Origin, Dir) || FMath::Abs(Dir.Z) < 1e-4)
		return false;
	const double T = -Origin.Z / Dir.Z;
	if (T < 0.0)
		return false;
	Out = Origin + Dir * T;
	return true;
}

int32 ARtsBridgeGameMode::PickUnitImpl(const FVector2D& ScreenPos, int32 Mode) const
{
	APlayerController* PC = UGameplayStatics::GetPlayerController(this, 0);
	if (!PC)
		return INDEX_NONE;
	// Project each candidate unit to screen; take the closest within a click radius.
	int32 BestId = INDEX_NONE;
	float BestDist = 40.0f; // px click tolerance
	FVector Ground;
	const bool bGround = ScreenToGround(ScreenPos, Ground);
	int32 UnderId = INDEX_NONE; // T28: a building whose footprint is under the cursor
	for (const ARtsSimUnitActor* A : UnitActors)
	{
		if (!A || A->IsDying())
			continue;
		const bool bOwnUnit = A->GetFaction() == PlayerFaction;
		if ((Mode == 0 && !bOwnUnit) || (Mode == 1 && bOwnUnit))
			continue; // own = PlayerFaction (you); enemy = any other faction
		FVector2D Proj;
		if (PC->ProjectWorldLocationToScreen(A->GetActorLocation(), Proj))
		{
			const float D = FVector2D::Distance(Proj, ScreenPos);
			if (D < BestDist) { BestDist = D; BestId = A->GetSimUnitId(); }
		}
		if (bGround && UnderId == INDEX_NONE)
			if (const FRtsUnitSnapshot* R = RowsById.Find(A->GetSimUnitId()))
				if (R->IsBuilding())
				{
					const FVector L = A->GetActorLocation();
					const double HalfW = FMath::Max(1, R->FootprintW) * SimCellUU * 0.5;
					const double HalfH = FMath::Max(1, R->FootprintH) * SimCellUU * 0.5;
					if (FMath::Abs(Ground.X - L.X) <= HalfW && FMath::Abs(Ground.Y - L.Y) <= HalfH)
						UnderId = A->GetSimUnitId();
				}
	}
	if (BestId != INDEX_NONE && !IsBuildingId(BestId))
		return BestId; // a unit at the cursor beats the building it stands beside
	return UnderId != INDEX_NONE ? UnderId : BestId;
}

FString ARtsBridgeGameMode::OwnerName(int32 Faction)
{
	return Faction == PlayerFaction ? FString(TEXT("Yours")) : FactionName(Faction);
}

FString ARtsBridgeGameMode::FactionName(int32 Faction)
{
	switch (Faction)
	{
	case 0: return TEXT("Coalition");
	case 1: return TEXT("Hegemony");
	case 2: return TEXT("Ascendant");
	default: return TEXT("Neutral");
	}
}

void ARtsBridgeGameMode::ChooseFaction(int32 Faction)
{
	if (!bChoosingFaction || Faction < 0 || Faction > 2)
		return;
	PlayerFaction = Faction;
	bChoosingFaction = false;
	// The sides you didn't pick are the host's computer players (sim/ai). The world is still at
	// tick 0, so re-initialising it with `ai` set rebuilds the same entities with the same ids.
	if (Bridge && bHostAlive)
	{
		FRtsHostReply Reply;
		const bool bOk = Bridge->Step(TEXT("{\"protocolVersion\":1,\"kind\":\"reset\"}"), 5.0, Reply) && Reply.bOk
			&& Bridge->Step(RtsContent::WithComputerPlayers(InitRequest, Faction), 10.0, Reply) && Reply.bOk;
		if (!bOk)
		{
			FailHost(FString::Printf(TEXT("COMPUTER PLAYERS INIT FAILED: %s"), *Reply.Error));
			return;
		}
		NextTickToSend = 0;
	}
	bCameraFramedOnPlayer = false; // frame your own start on the first snapshot
	StepAccum = 0.0;
	UE_LOG(LogTemp, Display, TEXT("RTS_FACTION: playing %s (faction %d)"), *FactionName(Faction), Faction);
}

FString ARtsBridgeGameMode::DisplayName(int32 SimUnitId) const
{
	const FRtsUnitSnapshot* R = RowsById.Find(SimUnitId);
	if (!R)
		return FString::Printf(TEXT("#%d"), SimUnitId);
	if (R->IsBuilding())
	{
		const FRtsBuildingType* B = FindBuildingType(R->BuildingId);
		return B && !B->Name.IsEmpty() ? B->Name : R->BuildingId;
	}
	const FRtsUnitType* U = FindUnitType(R->Type);
	return U && !U->Name.IsEmpty() ? U->Name : (R->Type.IsEmpty() ? FString::Printf(TEXT("#%d"), SimUnitId) : R->Type);
}

TArray<FString> ARtsBridgeGameMode::TooltipLines(int32 SimUnitId) const
{
	TArray<FString> L;
	const FRtsUnitSnapshot* R = RowsById.Find(SimUnitId);
	if (!R)
		return L;
	L.Add(DisplayName(SimUnitId));
	// what it is for (content `description`), right under the name
	if (R->IsBuilding())
	{
		if (const FRtsBuildingType* BT = FindBuildingType(R->BuildingId); BT && !BT->Description.IsEmpty())
			L.Add(BT->Description);
	}
	else if (const FRtsUnitType* UT = FindUnitType(R->Type); UT && !UT->Description.IsEmpty())
		L.Add(UT->Description);
	L.Add(FString::Printf(TEXT("%s    HP %.0f / %.0f"), *OwnerName(R->Faction), R->Hp, R->MaxHp));
	const bool bOwn = R->Faction == PlayerFaction;
	if (R->Bridge >= 0)
	{
		const FRtsBridgeSnapshot* B = Bridges.FindByPredicate([R](const FRtsBridgeSnapshot& X) { return X.Id == R->Bridge; });
		const bool bDamaged = B && B->IsDamaged();
		L.Add(bDamaged ? TEXT("Its bridge is damaged: right-click here with an engineer to repair it")
			: TEXT("Its bridge is intact. If it falls, right-click here with an engineer to rebuild it"));
		return L;
	}
	if (R->bWrecked)
	{
		L.Add(TEXT("Wrecked: right-click with an engineer to restore it (it becomes yours)"));
		return L;
	}
	if (!bOwn && (R->IsBuilding() || IsMcvRow(*R)))
		L.Add(TEXT("Right-click with an engineer to capture it"));
	if (bOwn && R->IsBuilding())
	{
		const TArray<FRtsUnitType> Trains = TrainableAt(R->BuildingId);
		if (Trains.Num() > 0)
		{
			TArray<FString> Names;
			for (int32 i = 0; i < Trains.Num() && i < 5; ++i)
				Names.Add(Trains[i].Name.IsEmpty() ? Trains[i].Id : Trains[i].Name);
			L.Add(FString::Printf(TEXT("Trains: %s%s"), *FString::Join(Names, TEXT(", ")),
				Trains.Num() > 5 ? *FString::Printf(TEXT(" +%d more"), Trains.Num() - 5) : TEXT("")));
		}
	}
	if (bOwn && IsMcvRow(*R))
		if (const FRtsBuildingType* Y = FindBuildingType(R->DeploysTo))
			L.Add(FString::Printf(TEXT("Deploys into a %s"), *(Y->Name.IsEmpty() ? Y->Id : Y->Name)));
	if (bOwn && R->bCanCapture)
		L.Add(TEXT("Right-click: captures enemy buildings, MCVs and the oil derrick; repairs bridges at their huts"));
	else if (bOwn && R->bRepairs)
		L.Add(TEXT("Right-click your damaged building to repair it, or a damaged bridge or its hut"));
	else if (bOwn && R->bEngineer)
		L.Add(TEXT("Right-click a damaged bridge or its hut to repair it"));
	if (bOwn && R->bMindControl)
	{
		L.Add(TEXT("Right-click an enemy unit: controls it after 3 s (up to 3; they go home if it dies or strays 12 cells)"));
		L.Add(TEXT("Right-click an enemy building: takes it for good after 6 s"));
	}
	if (R->ChannelTarget >= 0 && R->ChannelNeed > 0)
		L.Add(FString::Printf(TEXT("Channelling: %.1f s left"), (R->ChannelNeed - R->Channel) / 20.0f));
	if (R->ControlledBy >= 0)
		L.Add(TEXT("Mind-controlled: goes home if its Mindbender dies or strays"));
	if (R->bMarked)
		L.Add(TEXT("Marked: takes extra damage from the marking side"));
	if (R->IsPhased())
		L.Add(TEXT("Phased: can't be hit or fire"));
	if (bOwn && R->PhaseTicks >= 0 && !R->IsPhased())
		L.Add(R->PhaseCooldown > 0 ? FString::Printf(TEXT("Phase (Q): ready in %.0f s"), R->PhaseCooldown / 20.0f) : FString(TEXT("Phase (Q): ready")));
	if (bOwn && R->bConscriptable)
		L.Add(TEXT("Conscript (C): cash it in"));
	if (!R->Holds.IsEmpty())
	{
		const TCHAR* What = R->Holds == TEXT("garrison") ? TEXT("Garrison") : R->Holds == TEXT("tunnel") ? TEXT("Tunnel network") : TEXT("Transport");
		L.Add(FString::Printf(TEXT("%s: %d / %d slots (%s)%s"), What, R->Used, R->Capacity,
			R->Carries == TEXT("ground") ? TEXT("infantry 1, vehicles 4") : TEXT("infantry"),
			R->Holds == TEXT("garrison") ? TEXT(", they fire out") : TEXT("")));
		if (bOwn)
			L.Add(TEXT("Right-click it with units selected to load; Unload (U)"));
	}
	if (R->bTeleport && bOwn)
		L.Add(TEXT("Teleport (T): units near another node appear here"));
	if (R->IsSuperweapon())
	{
		if (R->bStrike)
			L.Add(FString::Printf(TEXT("Strike landing in %.1f s"), R->StrikeIn / 20.0f));
		else if (R->Charge > 0)
			L.Add(FString::Printf(TEXT("Charging: %d:%02d left (paused at low power)"), R->Charge / 20 / 60, (R->Charge / 20) % 60));
		else if (bOwn)
			L.Add(TEXT("Ready: Fire (G), then click the target"));
	}
	if (R->Sabotaged > 0)
		L.Add(FString::Printf(TEXT("Sabotaged: shut down for %.0f s"), R->Sabotaged / 20.0f));
	if (bOwn && R->bSabotages)
		L.Add(TEXT("Right-click an enemy building: shuts it down for 30 s (used up)"));
	if (bOwn && R->bHijacks)
		L.Add(TEXT("Right-click an enemy vehicle: steals it (used up)"));
	if (R->Shield > 0)
		L.Add(FString::Printf(TEXT("Shielded: takes %d%% less damage"), R->Shield));
	if (R->bCloaked)
		L.Add(TEXT("Cloaked: hidden from enemies without a detector"));
	else if (R->bStealth && bOwn)
		L.Add(TEXT("Stealthy: cloaked unless a detector is near or it just fired"));
	if (R->Carrier >= 0)
		L.Add(TEXT("Carrier aircraft: fights near its carrier, lost with it"));
	if (!R->Control.IsEmpty())
	{
		L.Add(FString::Printf(TEXT("Rig: %s"), *R->Control));
		if (R->Hold > 0 && R->HoldNeed > 0)
			L.Add(FString::Printf(TEXT("%s taking it: %.0f s left"), *FactionName(R->HoldFaction), (R->HoldNeed - R->Hold) / 20.0f));
		L.Add(TEXT("Hold it with the most units nearby to take it"));
	}
	return L;
}

TArray<FString> ARtsBridgeGameMode::BridgeTooltipLines(int32 BridgeId) const
{
	TArray<FString> L;
	const FRtsBridgeSnapshot* B = Bridges.FindByPredicate([BridgeId](const FRtsBridgeSnapshot& X) { return X.Id == BridgeId; });
	if (!B)
		return L;
	L.Add(B->bCauseway ? TEXT("Causeway") : TEXT("Bridge"));
	L.Add(B->bBroken ? FString(TEXT("Destroyed")) : FString::Printf(TEXT("HP %.0f / %.0f"), B->Hp, B->MaxHp));
	L.Add(TEXT("Ctrl+right-click with armed units to attack it"));
	if (B->IsDamaged())
		L.Add(TEXT("Engineers repair it: right-click it, or a bridge hut at either end"));
	return L;
}

void ARtsBridgeGameMode::RefreshSelectionRings()
{
	for (ARtsSimUnitActor* A : UnitActors)
		if (A)
			A->SetSelected(Selection.Contains(A->GetSimUnitId()));
}

void ARtsBridgeGameMode::ClickSelect(const FVector2D& ScreenPos, bool bShift)
{
	Selection.Click(PickOwnUnitAt(ScreenPos), bShift);
	RefreshSelectionRings();
}

void ARtsBridgeGameMode::BoxSelect(const FVector2D& A, const FVector2D& B, bool bShift)
{
	APlayerController* PC = UGameplayStatics::GetPlayerController(this, 0);
	if (!PC)
		return;
	const FBox2D Rect(FVector2D(FMath::Min(A.X, B.X), FMath::Min(A.Y, B.Y)),
		FVector2D(FMath::Max(A.X, B.X), FMath::Max(A.Y, B.Y)));
	TArray<int32> Ids;
	for (const ARtsSimUnitActor* U : UnitActors)
	{
		FVector2D Proj;
		if (U && U->GetFaction() == PlayerFaction && !IsBuildingId(U->GetSimUnitId()) && PC->ProjectWorldLocationToScreen(U->GetActorLocation(), Proj)
			&& Rect.IsInside(Proj))
			Ids.Add(U->GetSimUnitId());
	}
	Selection.BoxSelect(Ids, bShift);
	RefreshSelectionRings();
}

void ARtsBridgeGameMode::SelectAll()
{
	TArray<int32> Ids;
	for (const ARtsSimUnitActor* A : UnitActors)
		if (A && A->GetFaction() == PlayerFaction && !IsBuildingId(A->GetSimUnitId()))
			Ids.Add(A->GetSimUnitId());
	Selection.SetAll(Ids);
	RefreshSelectionRings();
}

void ARtsBridgeGameMode::AssignGroup(int32 Group)
{
	Selection.AssignGroup(Group);
}

bool ARtsBridgeGameMode::RecallGroup(int32 Group)
{
	const bool bOk = Selection.RecallGroup(Group);
	if (bOk)
		RefreshSelectionRings();
	return bOk;
}

bool ARtsBridgeGameMode::SelectionCentroid(FVector2D& OutWorldXY) const
{
	FVector2D Sum(0, 0);
	int32 N = 0;
	for (const ARtsSimUnitActor* A : UnitActors)
		if (A && Selection.Contains(A->GetSimUnitId()))
		{
			const FVector L = A->GetActorLocation();
			Sum += FVector2D(L.X, L.Y);
			++N;
		}
	if (N == 0)
		return false;
	OutWorldXY = Sum / N;
	return true;
}

void ARtsBridgeGameMode::SyncActorsFromReply(FRtsHostReply& Reply)
{
	// Match actors to snapshot rows by sim id: new ids spawn (placed exactly, no glide),
	// known ids get a new interpolation target, missing ids die (death event) or are removed.
	// Units the sim killed this step (death events) get a death sequence; anything else
	// that leaves the roster is simply removed.
	TSet<int32> Killed;
	for (const FRtsSimEvent& E : Reply.Events)
		if (E.Kind == TEXT("death"))
		{
			Killed.Add(E.Subject);
			if (Lost.IsValidIndex(E.Faction))
				++Lost[E.Faction];
			// T18: vehicles and buildings blow up where you last saw them (infantry just fall)
			if (const FRtsUnitSnapshot* Was = RowsById.Find(E.Subject))
				if (IsRowVisible(*Was))
				{
					const FRtsUnitType* T = UnitTypes.FindByPredicate([Was](const FRtsUnitType& U) { return U.Id == Was->Type; });
					const float Size = Was->IsBuilding() ? FMath::Max(Was->FootprintW, Was->FootprintH) * SimCellUU * 0.6f
						: (T && T->Armor == TEXT("infantry")) ? 0.0f : 90.0f;
					if (Size > 0.0f)
						SpawnExplosion(FVector(Was->X * SimCellUU, Was->Y * SimCellUU, 0.0), Size);
				}
		}

	// T35: economy rows are read straight from the snapshot (presentation only).
	Factions = Reply.Factions;
	Veins = Reply.Veins;
	if (VeinActor)
		VeinActor->UpdateVeins(Veins, SimCellUU);
	// T26: a fallen bridge's cells draw as water, a mended one's as bridge again.
	bool bTerrainChanged = false;
	for (const FRtsBridgeSnapshot& B : Reply.Bridges)
		for (const FIntPoint& C : B.Cells)
			if (TerrainRows.IsValidIndex(C.Y) && C.X >= 0 && C.X < TerrainRows[C.Y].Len())
			{
				const TCHAR Want = B.bBroken ? TEXT('~') : B.bCauseway ? TEXT('.') : TEXT('=');
				if (TerrainRows[C.Y][C.X] != Want)
				{
					TerrainRows[C.Y][C.X] = Want;
					bTerrainChanged = true;
				}
			}
	Bridges = Reply.Bridges;
	if (bTerrainChanged)
	{
		++TerrainVersion;
		if (TerrainActor)
			TerrainActor->Build(TerrainRows, SimCellUU);
	}
	RowsById.Reset();
	for (const FRtsUnitSnapshot& S : Reply.Units)
		RowsById.Add(S.Id, S);
	UpdateFog();
	// T18: a superweapon pulse landed when its strike ends or the next pulse is scheduled
	for (const FRtsUnitSnapshot& S : Reply.Units)
	{
		if (!S.IsSuperweapon())
			continue;
		FStrikeMemo& M = StrikeMemo.FindOrAdd(S.Id);
		if (M.bStrike && (!S.bStrike || S.StrikeIn > M.StrikeIn))
			SpawnExplosion(FVector(M.X * SimCellUU, M.Y * SimCellUU, 0.0), float(FMath::Max(1.0, M.R)) * SimCellUU);
		M = { S.bStrike, S.StrikeIn, S.StrikeX, S.StrikeY, S.StrikeRadius };
	}

	TSet<int32> Alive;
	UnitActors.Reset(Reply.Units.Num());
	for (const FRtsUnitSnapshot& S : Reply.Units)
	{
		Alive.Add(S.Id);
		ARtsSimUnitActor* A = FindUnitActor(S.Id);
		if (!A)
		{
			A = GetWorld()->SpawnActor<ARtsSimUnitActor>();
			if (!A)
				continue;
			if (Fielded.IsValidIndex(S.Faction))
				++Fielded[S.Faction];
			A->SetSimUnitId(S.Id);
			A->SetFaction(S.Faction);
			A->SetAirborne(S.Domain == TEXT("air")); // T25
			A->SnapToSimPosition(S.X, S.Y);
			A->SetModel(S.Type); // T17: imported model where there is one
			ActorsById.Add(S.Id, A);
		}
		else
		{
			A->SetSimPosition(S.X, S.Y);
			if (A->GetFaction() != S.Faction)
			{
				// T28: captured: the new owner's colour (orders already skip rows that aren't ours)
				A->SetFaction(S.Faction);
				A->InvalidateShape();
			}
		}
		// fog, cloak and passengers: hidden actors are left out of UnitActors, so they are
		// never picked, selected, hovered or drawn on the minimap either
		const bool bVisible = IsRowVisible(S);
		A->SetActorHiddenInGame(!bVisible);
		if (!bVisible)
			continue;
		A->SetHealth(S.Hp, S.MaxHp);
		if (S.IsBuilding())
			A->SetBuildingShape(FMath::Max(1, S.FootprintW), FMath::Max(1, S.FootprintH), S.bWrecked ? 0.0f // T28: a wreck is a dark slab
				: S.ConstructionTotal > 0 ? 1.0f - float(S.ConstructionRemaining) / float(S.ConstructionTotal) : 1.0f);
		else if (S.bHarvester)
			A->SetCargo(S.LoadCapacity > 0 ? float(S.Load) / float(S.LoadCapacity) : 0.0f, S.bCarrying);
		UnitActors.Add(A);
	}
	// T17: the shooter plays its fire clip
	for (const FRtsSimEvent& E : Reply.Events)
		if (E.Kind == TEXT("shot"))
			if (ARtsSimUnitActor* Shooter = FindUnitActor(E.Subject))
				Shooter->PlayFire();
	// T16: one tracer per shot, read while both actors are still mapped (a killing shot's
	// target leaves the roster below). Shooter faction colours it.
	if (APlayerController* PC = UGameplayStatics::GetPlayerController(this, 0))
		if (ARtsMinimapHud* Hud = Cast<ARtsMinimapHud>(PC->GetHUD()))
			for (const FRtsSimEvent& E : Reply.Events)
				if (E.Kind == TEXT("shot"))
				{
					const ARtsSimUnitActor* From = FindUnitActor(E.Subject);
					const ARtsSimUnitActor* To = FindUnitActor(E.Target);
					if (From && To)
						Hud->AddTracer(From->GetActorLocation(), To->GetActorLocation(), From->GetFaction());
				}
				else if (E.Kind == TEXT("bridge_hit")) // T26: subject = shooter, target = bridge id
				{
					const ARtsSimUnitActor* From = FindUnitActor(E.Subject);
					const FRtsBridgeSnapshot* B = Bridges.FindByPredicate([&E](const FRtsBridgeSnapshot& X) { return X.Id == E.Target; });
					if (From && B && B->Cells.Num() > 0)
					{
						// aim at the bridge cell nearest the shooter
						const FVector L = From->GetActorLocation();
						FVector Best = FVector::ZeroVector;
						double BestD = TNumericLimits<double>::Max();
						for (const FIntPoint& C : B->Cells)
						{
							const FVector P((C.X + 0.5) * SimCellUU, (C.Y + 0.5) * SimCellUU, 20.0);
							const double D = FVector::DistSquared2D(P, L);
							if (D < BestD) { BestD = D; Best = P; }
						}
						Hud->AddTracer(L, Best, From->GetFaction());
					}
				}
	DyingActors.RemoveAll([](const TWeakObjectPtr<ARtsSimUnitActor>& D) { return !D.IsValid(); });
	for (auto It = ActorsById.CreateIterator(); It; ++It)
		if (!Alive.Contains(It.Key()))
		{
			if (ARtsSimUnitActor* Gone = It.Value())
			{
				if (Killed.Contains(It.Key()))
				{
					Gone->BeginDeath();
					DyingActors.Add(Gone);
				}
				else
					Gone->Destroy();
			}
			It.RemoveCurrent();
		}
	// Units that left the roster (deaths) drop out of the selection and groups.
	Selection.Prune([&Alive](int32 Id) { return Alive.Contains(Id); });
	RefreshSelectionRings();

	// First snapshot: centre the camera on your units, like an RTS start.
	if (!bCameraFramedOnPlayer)
	{
		FVector2D Sum(0, 0);
		int32 N = 0;
		for (const FRtsUnitSnapshot& S : Reply.Units)
			if (S.Faction == PlayerFaction) { Sum += FVector2D(S.X, S.Y) * SimCellUU; ++N; }
		if (N > 0)
			if (ARtsRtsPlayerController* PC = Cast<ARtsRtsPlayerController>(UGameplayStatics::GetPlayerController(this, 0)))
			{
				PC->CenterCameraOn(Sum / N);
				bCameraFramedOnPlayer = true;
			}
	}
}

void ARtsBridgeGameMode::FailHost(const FString& Reason)
{
	bHostAlive = false;
	bFinished = true;
	if (Bridge)
		Bridge->Shutdown();
	// Design doc: host failure must be visible, never silently replaced by local rules.
	UE_LOG(LogTemp, Error, TEXT("RTS_SESSION_HOST_FAILURE: %s"), *Reason);
}

bool ARtsBridgeGameMode::RestartMatch()
{
	if (InitRequest.IsEmpty())
		return false; // content never loaded; nothing to replay
	if (!Bridge)
		Bridge = NewObject<URtsSimHostBridge>(this);

	FString Err;
	bool bInit = false;
	if (bHostAlive)
	{
		// Same process: drop the world, then init it again. The host rebuilds it purely
		// from the scenario, so this replays exactly like a fresh launch (host tests).
		FRtsHostReply Reply;
		bInit = Bridge->Step(TEXT("{\"protocolVersion\":1,\"kind\":\"reset\"}"), 5.0, Reply) && Reply.bOk
			&& Bridge->Step(InitRequest, 10.0, Reply) && Reply.bOk;
		if (!bInit)
			Err = Reply.Error;
	}
	if (!bInit)
	{
		// Host dead (or reset refused): relaunch it cleanly.
		Bridge->Shutdown();
		bInit = Bridge->StartAndInit(InitRequest, Err);
	}
	if (!bInit)
	{
		FailHost(FString::Printf(TEXT("RESTART FAILED: %s"), *Err));
		return false;
	}

	for (auto& Pair : ActorsById)
		if (Pair.Value)
			Pair.Value->Destroy();
	ActorsById.Reset();
	UnitActors.Reset();
	for (const TWeakObjectPtr<ARtsSimUnitActor>& D : DyingActors)
		if (D.IsValid())
			D->Destroy();
	DyingActors.Reset();
	Selection.Prune([](int32) { return false; }); // selection and control groups
	RefreshSelectionRings();
	PendingCommandBodies.Reset();
	NextTickToSend = 0;
	StepAccum = 0.0;
	bMatchReported = false;
	bCameraFramedOnPlayer = false;
	MatchWinner = -1;
	MatchEndTick = -1;
	Fielded = {0, 0, 0};
	Lost = {0, 0, 0};
	bFinished = false;
	bHostAlive = true;
	bChoosingFaction = CanRestart(); // pick again: Play again is how you switch sides
	ResetFog();
	if (GEngine)
		GEngine->ClearOnScreenDebugMessages();
	UE_LOG(LogTemp, Display, TEXT("RTS_MATCH_RESTART: new match from tick 0"));
	return true;
}

bool ARtsBridgeGameMode::IsMatchScreenUp() const
{
	return bChoosingFaction || (bMatchReported && FPlatformTime::Seconds() - MatchOverRealTime >= MatchScreenDelay);
}

float ARtsBridgeGameMode::MatchScreenAlpha() const
{
	if (!bMatchReported)
		return 0.0f;
	const double T = FPlatformTime::Seconds() - MatchOverRealTime - MatchScreenDelay;
	return float(FMath::Clamp(T / MatchScreenFade, 0.0, 1.0));
}

bool ARtsBridgeGameMode::CanRestart() const
{
	// Single player only: a standalone game (PIE counts). Never offered in a networked match.
	return GetNetMode() == NM_Standalone && !InitRequest.IsEmpty();
}

void ARtsBridgeGameMode::QuitMatch()
{
	UKismetSystemLibrary::QuitGame(this, UGameplayStatics::GetPlayerController(this, 0), EQuitPreference::Quit, false);
}

void ARtsBridgeGameMode::SetDebugMask(int32 Mask)
{
	DebugMask = Mask;
	if (DebugMask == 0)
		DebugById.Reset(); // overlays off: stop showing stale rows
	UE_LOG(LogTemp, Display, TEXT("RTS_DEBUG_OVERLAYS: mask=%d"), DebugMask);
}

// ---- T36: scenario choice, build menu, placement --------------------------------------------

FString ARtsBridgeGameMode::MatchScenarioId()
{
	const FString Env = FPlatformMisc::GetEnvironmentVariable(TEXT("RTS_SCENARIO"));
	return Env.IsEmpty() ? FString(SkirmishScenarioId) : Env;
}

bool ARtsBridgeGameMode::HasOwnBuildings() const
{
	for (const TPair<int32, FRtsUnitSnapshot>& P : RowsById)
		if (P.Value.Faction == PlayerFaction && P.Value.IsBuilding())
			return true;
	return false;
}

bool ARtsBridgeGameMode::HasOwnMcv() const
{
	for (const TPair<int32, FRtsUnitSnapshot>& P : RowsById)
		if (P.Value.Faction == PlayerFaction && IsMcvRow(P.Value))
			return true;
	return false;
}

bool ARtsBridgeGameMode::SelectionHasMcv() const
{
	for (const int32 Id : Selection.GetSelected())
		if (const FRtsUnitSnapshot* R = RowsById.Find(Id))
			if (IsMcvRow(*R))
				return true;
	return false;
}

const TArray<ARtsBridgeGameMode::FBuildOption>& ARtsBridgeGameMode::GetBuildOptions()
{
	// Prereqs are re-asked only when your buildings (or their finished state) change; credits and
	// power are re-read from the snapshot every call, so income ticks cost no host round trip.
	TArray<FString> Parts;
	for (const TPair<int32, FRtsUnitSnapshot>& P : RowsById)
		if (P.Value.Faction == PlayerFaction && P.Value.IsBuilding())
			Parts.Add(FString::Printf(TEXT("%d:%s:%d"), P.Key, *P.Value.BuildingId, P.Value.ConstructionRemaining == 0 ? 1 : 0));
	Parts.Sort();
	FString Key = FString::Join(Parts, TEXT(","));
	if (const FRtsFactionSnapshot* Own = GetFactionState(PlayerFaction)) // T28: a captured tree changes the menu too
		for (const int32 T : Own->Trees)
			Key += FString::Printf(TEXT("|t%d"), T);
	if (Key != BuildOptionsKey)
	{
		BuildOptionsKey = Key;
		RebuildBuildOptions(Parts.Num() > 0);
	}
	int32 Supply = 0, Draw = 0, Credits = 0;
	if (const FRtsFactionSnapshot* F = GetFactionState(PlayerFaction))
	{
		Supply = F->PowerSupply;
		Draw = F->PowerDraw;
		Credits = F->Credits;
	}
	for (FBuildOption& O : BuildOptions)
		if (const FRtsBuildingType* B = FindBuildingType(O.BuildingId))
		{
			O.bAffordable = Credits >= B->Cost;
			O.bPowerWarn = B->Power < 0 && Supply - Draw + B->Power < 0;
		}
	return BuildOptions;
}

void ARtsBridgeGameMode::RebuildBuildOptions(bool bHaveBase)
{
	BuildOptions.Reset();
	if (!bHaveBase || !Bridge || !bHostAlive)
		return;
	for (const FRtsBuildingType& B : BuildingTypes)
	{
		// Yards come from the MCV. Naval/offshore go on water (T25): the sim's query says
		// "water" / "shore" when the ghost is on the wrong ground.
		// T28: a captured yard or MCV opens its tree (the snapshot's `trees`)
		const FRtsFactionSnapshot* Own = GetFactionState(PlayerFaction);
		if (!(B.Faction == PlayerFaction || (Own && Own->HasTree(B.Faction))) || B.Category == TEXT("construction-yard"))
			continue;
		FBuildOption O;
		O.bNeedsWater = B.Category == TEXT("naval") || B.Category == TEXT("offshore");
		O.Description = B.Description;
		O.BuildingId = B.Id;
		O.Name = B.Name.IsEmpty() ? B.Id : B.Name;
		O.Cost = B.Cost;
		// Prereq comes before every map check in the sim's verdict, so any cell answers it.
		FRtsHostReply Q;
		O.bPrereqOk = Bridge->QueryPlace(PlayerFaction, B.Id, 0, 0, Q) && Q.QueryReason != TEXT("prereq");
		if (!O.bPrereqOk)
			if (const FRtsBuildingType* Need = FindBuildingType(B.Prereq))
				O.PrereqName = Need->Name.IsEmpty() ? Need->Id : Need->Name;
		BuildOptions.Add(MoveTemp(O));
	}
	// Buildable first, then by cost (cheap basics on top), then name: stable order.
	BuildOptions.StableSort([](const FBuildOption& A, const FBuildOption& B)
	{
		if (A.bNeedsWater != B.bNeedsWater)
			return !A.bNeedsWater; // water buildings last (they need a coast near your base)
		if (A.bPrereqOk != B.bPrereqOk)
			return A.bPrereqOk;
		if (A.Cost != B.Cost)
			return A.Cost < B.Cost;
		return A.Name < B.Name;
	});
}

bool ARtsBridgeGameMode::QueryPlace(const FString& BuildingId, int32 CellX, int32 CellY, FPlaceAnswer& Out)
{
	if (!Bridge || !bHostAlive)
		return false;
	const FString Key = FString::Printf(TEXT("%s@%d,%d#%d"), *BuildingId, CellX, CellY, NextTickToSend);
	if (Key == PlaceCacheKey)
	{
		Out = PlaceCache;
		return true;
	}
	FRtsHostReply Q;
	if (!Bridge->QueryPlace(PlayerFaction, BuildingId, CellX, CellY, Q))
		return false;
	PlaceCache.bAllowed = Q.bQueryAllowed;
	PlaceCache.Reason = Q.QueryReason;
	PlaceCache.Rect = Q.QueryRect;
	PlaceCacheKey = Key;
	Out = PlaceCache;
	return true;
}

bool ARtsBridgeGameMode::RequestPlace(const FString& BuildingId, int32 CellX, int32 CellY)
{
	if (!bHostAlive || IsMatchScreenUp())
		return false;
	PendingCommandBodies.Add(FString::Printf(TEXT("\"faction\":%d,\"type\":\"place\",\"building\":\"%s\",\"at\":[%d,%d]"), PlayerFaction,
		*BuildingId, CellX, CellY));
	return true;
}

bool ARtsBridgeGameMode::RequestDeploySelected()
{
	if (!bHostAlive || IsMatchScreenUp())
		return false;
	bool bAny = false;
	for (const int32 Id : Selection.GetSelected())
		if (const FRtsUnitSnapshot* R = RowsById.Find(Id))
			if (R->Faction == PlayerFaction && IsMcvRow(*R))
			{
				PendingCommandBodies.Add(FString::Printf(TEXT("\"faction\":%d,\"type\":\"deploy\",\"unit\":%d"), PlayerFaction, Id));
				bAny = true;
			}
	return bAny;
}

void ARtsBridgeGameMode::SelectOwnMcvs()
{
	TArray<int32> Ids;
	for (const TPair<int32, FRtsUnitSnapshot>& P : RowsById)
		if (P.Value.Faction == PlayerFaction && IsMcvRow(P.Value))
			Ids.Add(P.Key);
	Selection.SetAll(Ids);
	RefreshSelectionRings();
}

// ---- T37: production queue and rally ---------------------------------------------------------

TArray<FRtsUnitType> ARtsBridgeGameMode::TrainableAt(const FString& BuildingTypeId) const
{
	TArray<FRtsUnitType> Out;
	for (const FRtsUnitType& U : UnitTypes)
		if (U.BuiltFrom == BuildingTypeId) // T28: a captured building fields its own (enemy) units
			Out.Add(U);
	return Out;
}

int32 ARtsBridgeGameMode::GetSelectedProducer() const
{
	int32 Best = INDEX_NONE;
	for (const int32 Id : Selection.GetSelected())
		if (const FRtsUnitSnapshot* R = RowsById.Find(Id))
			if (R->Faction == PlayerFaction && R->IsBuilding() && (Best == INDEX_NONE || Id < Best)
				&& UnitTypes.ContainsByPredicate([R](const FRtsUnitType& U) { return U.BuiltFrom == R->BuildingId; }))
				Best = Id;
	return Best;
}

bool ARtsBridgeGameMode::RequestTrain(int32 BuildingSimId, const FString& UnitId, int32 Count)
{
	if (!bHostAlive || IsMatchScreenUp() || Count <= 0)
		return false;
	for (int32 i = 0; i < Count; ++i)
		PendingCommandBodies.Add(FString::Printf(TEXT("\"faction\":%d,\"type\":\"train\",\"building\":%d,\"unit\":\"%s\""), PlayerFaction,
			BuildingSimId, *UnitId));
	return true;
}

bool ARtsBridgeGameMode::RequestCancel(int32 BuildingSimId, int32 Index)
{
	if (!bHostAlive || IsMatchScreenUp() || Index < 0)
		return false;
	PendingCommandBodies.Add(FString::Printf(TEXT("\"faction\":%d,\"type\":\"cancel\",\"building\":%d,\"index\":%d"), PlayerFaction,
		BuildingSimId, Index));
	return true;
}

bool ARtsBridgeGameMode::RequestCancelLastOf(int32 BuildingSimId, const FString& UnitId)
{
	const FRtsUnitSnapshot* R = RowsById.Find(BuildingSimId);
	if (!R)
		return false;
	const int32 Index = R->ProductionQueue.FindLast(UnitId);
	return Index != INDEX_NONE && RequestCancel(BuildingSimId, Index);
}

bool ARtsBridgeGameMode::RequestRallySelected(const FVector& WorldPoint)
{
	if (!bHostAlive || IsMatchScreenUp())
		return false;
	bool bAny = false;
	for (const int32 Id : Selection.GetSelected())
		if (const FRtsUnitSnapshot* R = RowsById.Find(Id))
			if (R->Faction == PlayerFaction && R->IsBuilding() && TrainableAt(R->BuildingId).Num() > 0)
			{
				PendingCommandBodies.Add(FString::Printf(TEXT("\"faction\":%d,\"type\":\"rally\",\"building\":%d,\"at\":%s"), PlayerFaction,
					Id, *MapPointJson(WorldPoint)));
				bAny = true;
			}
	return bAny;
}

// ---- T38: harvest orders ---------------------------------------------------------------------

int32 ARtsBridgeGameMode::VeinAt(const FVector& WorldPoint) const
{
	const FVector2D P(WorldPoint.X / SimCellUU, WorldPoint.Y / SimCellUU);
	int32 Best = INDEX_NONE;
	double BestD = 1.0; // within one cell of the vein centre
	for (const FRtsVeinSnapshot& V : Veins)
	{
		if (V.Pool <= 0)
			continue;
		const double D = FVector2D::Distance(P, FVector2D(V.X, V.Y));
		if (D <= BestD)
		{
			BestD = D;
			Best = V.Id;
		}
	}
	return Best;
}

int32 ARtsBridgeGameMode::BridgeAt(const FVector& WorldPoint) const
{
	const int32 X = FMath::FloorToInt(WorldPoint.X / SimCellUU);
	const int32 Y = FMath::FloorToInt(WorldPoint.Y / SimCellUU);
	for (const FRtsBridgeSnapshot& B : Bridges)
		if (B.Covers(X, Y))
			return B.Id;
	return INDEX_NONE;
}

bool ARtsBridgeGameMode::RequestBridgeAt(const FVector& WorldPoint, bool bAttack)
{
	if (!bHostAlive || IsMatchScreenUp())
		return false;
	const int32 Id = BridgeAt(WorldPoint);
	const FRtsBridgeSnapshot* B = Bridges.FindByPredicate([Id](const FRtsBridgeSnapshot& X) { return X.Id == Id; });
	if (!B)
		return false;
	// Engineers mend a damaged or fallen bridge; armed units shoot a standing one on a forced attack.
	FString Ids;
	for (const int32 Sel : Selection.GetSelected())
		if (const FRtsUnitSnapshot* R = RowsById.Find(Sel))
		{
			if (R->Faction != PlayerFaction || R->IsBuilding())
				continue;
			const bool bFix = R->bEngineer && B->IsDamaged();
			const bool bShoot = bAttack && !R->bEngineer && !B->bBroken;
			if (bFix || bShoot)
				Ids += (Ids.IsEmpty() ? TEXT("") : TEXT(",")) + FString::FromInt(Sel);
		}
	if (Ids.IsEmpty())
		return false; // nobody here can work on it: an ordinary move
	PendingCommandBodies.Add(FString::Printf(TEXT("\"faction\":%d,\"type\":\"bridge\",\"units\":[%s],\"bridge\":%d"), PlayerFaction, *Ids, Id));
	return true;
}

bool ARtsBridgeGameMode::RequestRepair(int32 TargetSimId)
{
	if (!bHostAlive || IsMatchScreenUp())
		return false;
	// Weaver: selected repairers mend your own damaged, finished building
	const FRtsUnitSnapshot* T = RowsById.Find(TargetSimId);
	if (!T || T->Faction != PlayerFaction || !T->IsBuilding() || T->Bridge >= 0 || T->ConstructionRemaining > 0 || T->Hp >= T->MaxHp)
		return false;
	FString Ids;
	for (const int32 Sel : Selection.GetSelected())
		if (const FRtsUnitSnapshot* R = RowsById.Find(Sel))
			if (R->Faction == PlayerFaction && R->bRepairs)
				Ids += (Ids.IsEmpty() ? TEXT("") : TEXT(",")) + FString::FromInt(Sel);
	if (Ids.IsEmpty())
		return false;
	PendingCommandBodies.Add(FString::Printf(TEXT("\"faction\":%d,\"type\":\"capture\",\"units\":[%s],\"target\":%d"), PlayerFaction, *Ids, TargetSimId));
	return true;
}

bool ARtsBridgeGameMode::RequestCaptureOrAttack(int32 TargetSimId)
{
	if (!bHostAlive || IsMatchScreenUp())
		return false;
	// T28: selected capturing engineers take an enemy or neutral building, or an enemy MCV; the
	// rest of the selection attacks it (a neutral one can't be shot, so they just don't).
	const FRtsUnitSnapshot* T = RowsById.Find(TargetSimId);
	const bool bCapturable = T && T->Faction != PlayerFaction && (T->IsBuilding() || IsMcvRow(*T));
	// a Mindbender channels on any enemy or neutral unit or building but a hut or a wreck
	const bool bChannelable = T && T->Faction != PlayerFaction && T->Bridge < 0 && !T->bWrecked && !T->IsPhased();
	FString Capturers, Attackers;
	for (const int32 Sel : Selection.GetSelected())
		if (const FRtsUnitSnapshot* R = RowsById.Find(Sel))
		{
			if (R->Faction != PlayerFaction || R->IsBuilding())
				continue;
			// a bridge hut is a repair job: any engineer, the Weaver too (it can't capture)
			const bool bTakes = R->bCanCapture || (T && T->Bridge >= 0 && R->bEngineer);
			// Operative: sabotage an enemy building; Hijacker: steal an enemy vehicle (the sim checks the kind)
			const bool bInfiltrates = T && T->Faction >= 0 && T->Faction != PlayerFaction && T->Bridge < 0
				&& ((R->bSabotages && T->IsBuilding()) || (R->bHijacks && !T->IsBuilding()));
			FString& Into = (bCapturable && bTakes) || (R->bMindControl && bChannelable) || bInfiltrates ? Capturers : Attackers;
			Into += (Into.IsEmpty() ? TEXT("") : TEXT(",")) + FString::FromInt(Sel);
		}
	bool bQueued = false;
	if (!Capturers.IsEmpty())
	{
		PendingCommandBodies.Add(FString::Printf(TEXT("\"faction\":%d,\"type\":\"capture\",\"units\":[%s],\"target\":%d"), PlayerFaction, *Capturers, TargetSimId));
		bQueued = true;
	}
	if (!Attackers.IsEmpty() && T && T->Faction >= 0)
	{
		PendingCommandBodies.Add(FString::Printf(TEXT("\"faction\":%d,\"type\":\"attack\",\"units\":[%s],\"target\":%d"), PlayerFaction, *Attackers, TargetSimId));
		bQueued = true;
	}
	return bQueued;
}

bool ARtsBridgeGameMode::RequestHarvestAt(const FVector& WorldPoint)
{
	if (!bHostAlive || IsMatchScreenUp())
		return false;
	const int32 Vein = VeinAt(WorldPoint);
	if (Vein == INDEX_NONE)
		return false;
	FString Ids;
	for (const int32 Id : Selection.GetSelected())
		if (const FRtsUnitSnapshot* R = RowsById.Find(Id))
			if (R->Faction == PlayerFaction && R->bHarvester)
				Ids += (Ids.IsEmpty() ? TEXT("") : TEXT(",")) + FString::FromInt(Id);
	if (Ids.IsEmpty())
		return false; // no harvesters: an ordinary move onto the field
	PendingCommandBodies.Add(FString::Printf(TEXT("\"faction\":%d,\"type\":\"harvest\",\"units\":[%s],\"vein\":%d"), PlayerFaction, *Ids, Vein));
	QueueOrderForSelection(TEXT("move"), MapPointJson(WorldPoint), /*bSkipHarvesters=*/true); // the rest, if any
	return true;
}

// ---- abilities: phase, conscript, transports, teleports, superweapons ------------------------

FString ARtsBridgeGameMode::SelectedIdsJson(TFunctionRef<bool(const FRtsUnitSnapshot&)> Pred) const
{
	FString Ids;
	for (const int32 Id : Selection.GetSelected())
		if (const FRtsUnitSnapshot* R = RowsById.Find(Id))
			if (R->Faction == PlayerFaction && Pred(*R))
				Ids += (Ids.IsEmpty() ? TEXT("") : TEXT(",")) + FString::FromInt(Id);
	return Ids;
}

/** Within this many cells of an own Teleport Node a unit can be sent to another (TransportSystem.TeleportReach). */
static constexpr double TeleportReachCells = 5.0;

ARtsBridgeGameMode::FSelectionAbilities ARtsBridgeGameMode::GetSelectionAbilities() const
{
	FSelectionAbilities A;
	TArray<const FRtsUnitSnapshot*> Nodes;
	for (const TPair<int32, FRtsUnitSnapshot>& P : RowsById)
		if (P.Value.Faction == PlayerFaction && P.Value.bTeleport && P.Value.ConstructionRemaining == 0 && P.Value.Sabotaged == 0)
			Nodes.Add(&P.Value);
	for (const int32 Id : Selection.GetSelected())
	{
		const FRtsUnitSnapshot* R = RowsById.Find(Id);
		if (!R || R->Faction != PlayerFaction)
			continue;
		if (R->PhaseTicks >= 0)
		{
			++A.Phase;
			if (R->PhaseTicks == 0 && R->PhaseCooldown == 0)
				++A.PhaseReady;
		}
		if (R->bConscriptable)
			++A.Conscript;
		if (!R->Holds.IsEmpty() && R->Used > 0)
			++A.Unload;
		if (R->IsSuperweapon())
		{
			++A.Fire;
			if (R->Charge == 0 && !R->bStrike && R->Sabotaged == 0)
				++A.FireReady;
		}
		if (IsMcvRow(*R))
			++A.Deploy;
		if (!R->IsBuilding() && Nodes.Num() >= 2)
			for (const FRtsUnitSnapshot* N : Nodes)
				if (FVector2D::Distance(FVector2D(R->X, R->Y), FVector2D(N->X, N->Y)) <= TeleportReachCells)
				{
					++A.Teleport;
					break;
				}
	}
	return A;
}

bool ARtsBridgeGameMode::RequestPhaseSelected()
{
	if (!bHostAlive || IsMatchScreenUp())
		return false;
	const FString Ids = SelectedIdsJson([](const FRtsUnitSnapshot& R) { return R.PhaseTicks == 0 && R.PhaseCooldown == 0; });
	if (Ids.IsEmpty())
		return false;
	PendingCommandBodies.Add(FString::Printf(TEXT("\"faction\":%d,\"type\":\"phase\",\"units\":[%s]"), PlayerFaction, *Ids));
	return true;
}

bool ARtsBridgeGameMode::RequestConscriptSelected()
{
	if (!bHostAlive || IsMatchScreenUp())
		return false;
	const FString Ids = SelectedIdsJson([](const FRtsUnitSnapshot& R) { return R.bConscriptable; });
	if (Ids.IsEmpty())
		return false;
	PendingCommandBodies.Add(FString::Printf(TEXT("\"faction\":%d,\"type\":\"conscript\",\"units\":[%s]"), PlayerFaction, *Ids));
	return true;
}

bool ARtsBridgeGameMode::RequestUnloadSelected()
{
	if (!bHostAlive || IsMatchScreenUp())
		return false;
	bool bAny = false;
	for (const int32 Id : Selection.GetSelected())
		if (const FRtsUnitSnapshot* R = RowsById.Find(Id))
			if (R->Faction == PlayerFaction && !R->Holds.IsEmpty() && R->Used > 0)
			{
				PendingCommandBodies.Add(FString::Printf(TEXT("\"faction\":%d,\"type\":\"unload\",\"holder\":%d"), PlayerFaction, Id));
				bAny = true;
			}
	return bAny;
}

bool ARtsBridgeGameMode::RequestLoad(int32 HolderSimId)
{
	if (!bHostAlive || IsMatchScreenUp())
		return false;
	const FRtsUnitSnapshot* H = RowsById.Find(HolderSimId);
	if (!H || H->Faction != PlayerFaction || H->Holds.IsEmpty() || H->ConstructionRemaining > 0)
		return false;
	// the sim decides who fits (infantry-only holds, room); air and naval never board
	const FString Ids = SelectedIdsJson([HolderSimId](const FRtsUnitSnapshot& R)
	{
		return R.Id != HolderSimId && !R.IsBuilding() && R.Holds.IsEmpty() && R.Domain != TEXT("air") && R.Domain != TEXT("naval");
	});
	if (Ids.IsEmpty())
		return false;
	PendingCommandBodies.Add(FString::Printf(TEXT("\"faction\":%d,\"type\":\"load\",\"units\":[%s],\"holder\":%d"), PlayerFaction, *Ids, HolderSimId));
	return true;
}

bool ARtsBridgeGameMode::RequestTeleportTo(int32 NodeSimId)
{
	if (!bHostAlive || IsMatchScreenUp())
		return false;
	const FRtsUnitSnapshot* N = RowsById.Find(NodeSimId);
	if (!N || N->Faction != PlayerFaction || !N->bTeleport)
		return false;
	const FString Ids = SelectedIdsJson([](const FRtsUnitSnapshot& R) { return !R.IsBuilding(); });
	if (Ids.IsEmpty())
		return false;
	PendingCommandBodies.Add(FString::Printf(TEXT("\"faction\":%d,\"type\":\"teleport\",\"units\":[%s],\"node\":%d"), PlayerFaction, *Ids, NodeSimId));
	return true;
}

bool ARtsBridgeGameMode::RequestFireAt(const FVector& WorldPoint)
{
	if (!bHostAlive || IsMatchScreenUp())
		return false;
	// one strike per order: the lowest-id charged superweapon in the selection
	int32 Best = INDEX_NONE;
	for (const int32 Id : Selection.GetSelected())
		if (const FRtsUnitSnapshot* R = RowsById.Find(Id))
			if (R->Faction == PlayerFaction && R->IsSuperweapon() && R->Charge == 0 && !R->bStrike && R->Sabotaged == 0
				&& (Best == INDEX_NONE || Id < Best))
				Best = Id;
	if (Best == INDEX_NONE)
		return false;
	PendingCommandBodies.Add(FString::Printf(TEXT("\"faction\":%d,\"type\":\"fire\",\"building\":%d,\"target\":%s"), PlayerFaction, Best,
		*MapPointJson(WorldPoint)));
	return true;
}

// ---- fog of war ------------------------------------------------------------------------------

void ARtsBridgeGameMode::ResetFog()
{
	FogW = FMath::Max(1, (MapCellsW + FogCell - 1) / FogCell);
	FogH = FMath::Max(1, (MapCellsH + FogCell - 1) / FogCell);
	Fog.Init(0, FogW * FogH);
	SeenBuildings.Reset();
}

void ARtsBridgeGameMode::UpdateFog()
{
	if (FogW != FMath::Max(1, (MapCellsW + FogCell - 1) / FogCell) || Fog.Num() != FogW * FogH)
		ResetFog();
	for (uint8& F : Fog)
		if (F == 2)
			F = 1;
	auto Reveal = [this](double X, double Y, double R)
	{
		const int32 X0 = FMath::Max(0, FMath::FloorToInt((X - R) / FogCell)), X1 = FMath::Min(FogW - 1, FMath::FloorToInt((X + R) / FogCell));
		const int32 Y0 = FMath::Max(0, FMath::FloorToInt((Y - R) / FogCell)), Y1 = FMath::Min(FogH - 1, FMath::FloorToInt((Y + R) / FogCell));
		const double R2 = R * R;
		for (int32 Cy = Y0; Cy <= Y1; ++Cy)
			for (int32 Cx = X0; Cx <= X1; ++Cx)
			{
				const double Dx = (Cx + 0.5) * FogCell - X, Dy = (Cy + 0.5) * FogCell - Y;
				if (Dx * Dx + Dy * Dy <= R2)
					Fog[Cy * FogW + Cx] = 2;
			}
	};
	for (const TPair<int32, FRtsUnitSnapshot>& P : RowsById)
	{
		const FRtsUnitSnapshot& R = P.Value;
		if (R.Faction != PlayerFaction || R.IsInside())
			continue;
		const double Sight = R.Sight > 0.0 ? R.Sight : (R.IsBuilding() ? 6.0 : 5.0);
		Reveal(R.X, R.Y, FMath::Max(Sight, R.Radar));
	}
	for (const TPair<int32, FRtsUnitSnapshot>& P : RowsById)
		if (P.Value.IsBuilding() && P.Value.Faction != PlayerFaction
			&& FogAt(FMath::FloorToInt(P.Value.X / FogCell), FMath::FloorToInt(P.Value.Y / FogCell)) == 2)
			SeenBuildings.Add(P.Key);
}

bool ARtsBridgeGameMode::IsRowVisible(const FRtsUnitSnapshot& R) const
{
	if (R.IsInside())
		return false; // passengers ride hidden (their holder shows how full it is)
	if (R.Faction == PlayerFaction || bChoosingFaction)
		return true;
	if (!R.VisibleTo(PlayerFaction))
		return false; // cloaked, and no detector of yours on it
	if (!bFogEnabled)
		return true;
	if (R.IsBuilding() && SeenBuildings.Contains(R.Id))
		return true;
	return FogAt(FMath::FloorToInt(R.X / FogCell), FMath::FloorToInt(R.Y / FogCell)) == 2;
}

// ---- T18: explosions -------------------------------------------------------------------------

void ARtsBridgeGameMode::SpawnExplosion(const FVector& Where, float SizeUU)
{
	Explosions.RemoveAll([](const TWeakObjectPtr<ARtsExplosionActor>& E) { return !E.IsValid(); });
	if (Explosions.Num() >= MaxExplosions || !GetWorld())
		return; // a dense brawl stays readable (and cheap)
	if (ARtsExplosionActor* E = GetWorld()->SpawnActor<ARtsExplosionActor>(Where, FRotator::ZeroRotator))
	{
		E->Ignite(SizeUU);
		Explosions.Add(E);
	}
}
