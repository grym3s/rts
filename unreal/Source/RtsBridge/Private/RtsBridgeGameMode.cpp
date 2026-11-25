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
#include "Engine/World.h"
#include "Engine/StaticMeshActor.h"
#include "Engine/DirectionalLight.h"
#include "Components/DirectionalLightComponent.h"
#include "Engine/SkyLight.h"
#include "Components/SkyLightComponent.h"
#include "Components/StaticMeshComponent.h"
#include "Materials/MaterialInstanceDynamic.h"
#include "Kismet/GameplayStatics.h"
#include "HAL/PlatformTime.h"
#include "Engine/Engine.h"

// The match comes from content/ (ScenarioId / MapId): the scenario minus its scripted
// commands becomes the host init; the typed tile map is drawn. Rules stay host-owned.

// Design scale: 100 UU per map cell.
static constexpr float SimCellUU = ARtsBridgeGameMode::CellUU;

int32 ARtsBridgeGameMode::MapCellsW = 32;
int32 ARtsBridgeGameMode::MapCellsH = 24;

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
	if (!RtsContent::LoadMatch(ScenarioId, MapId, Setup, LoadErr))
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
	if (ARtsTerrainActor* Terrain = GetWorld()->SpawnActor<ARtsTerrainActor>(FVector::ZeroVector, FRotator::ZeroRotator, SP))
		Terrain->Build(TerrainRows, SimCellUU);
	// Lights spawned at runtime into an unbuilt level must be Movable to light anything.
	if (ADirectionalLight* Sun = GetWorld()->SpawnActor<ADirectionalLight>(FVector::ZeroVector, FRotator(-45.0f, 30.0f, 0.0f)))
	{
		Sun->GetComponent()->SetMobility(EComponentMobility::Movable);
		Sun->GetComponent()->SetIntensity(2.5f); // 8 blew out to white with fixed exposure
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
	UE_LOG(LogTemp, Display, TEXT("RTS_SESSION: host up, stepping live (select: LMB click/drag, Shift adds, Ctrl+A, groups Ctrl+1-3/1-3, move: RMB (enemy = attack), attack-move: F+LMB, stop: X, minimap: bottom-left)"));
}

void ARtsBridgeGameMode::Tick(float DeltaSeconds)
{
	Super::Tick(DeltaSeconds);
	if (bFinished || !Bridge || !bHostAlive)
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
		const FString Step = FString::Printf(
			TEXT("{\"protocolVersion\":1,\"kind\":\"step\",\"tick\":%d,\"commands\":%s}"), NextTickToSend, *Commands);
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
		if (Reply.bMatchOver && !bMatchReported)
		{
			// T06 result; the real win/lose screen with restart is T19.
			bMatchReported = true;
			const FString Msg = Reply.MatchWinner == 0 ? TEXT("VICTORY") : Reply.MatchWinner < 0 ? TEXT("DRAW") : TEXT("DEFEAT");
			UE_LOG(LogTemp, Display, TEXT("RTS_MATCH_OVER: %s (winner %d, tick %lld)"), *Msg, Reply.MatchWinner, Reply.Tick);
			if (GEngine)
				GEngine->AddOnScreenDebugMessage(-1, 30.0f, Reply.MatchWinner == 0 ? FColor::Green : FColor::Red,
					FString::Printf(TEXT("%s  (match over at tick %lld)"), *Msg, Reply.Tick), true, FVector2D(3.0f, 3.0f));
		}
	}
}

bool ARtsBridgeGameMode::QueueOrderForSelection(const TCHAR* Type, const FString& TargetJson)
{
	if (!bHostAlive || Selection.GetSelected().Num() == 0)
		return false;
	FString Ids;
	for (const int32 Id : Selection.GetSelected())
	{
		if (Ids.Len())
			Ids += TEXT(",");
		Ids += FString::Printf(TEXT("%d"), Id);
	}
	// Tick-less body; Tick() wraps it as {"tick":N, <body>} when it sends.
	FString Body = FString::Printf(TEXT("\"faction\":0,\"type\":\"%s\",\"units\":[%s]"), Type, *Ids);
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
	APlayerController* PC = UGameplayStatics::GetPlayerController(this, 0);
	if (!PC)
		return INDEX_NONE;
	// Project each candidate unit to screen; take the closest within a click radius.
	int32 BestId = INDEX_NONE;
	float BestDist = 40.0f; // px click tolerance
	for (const ARtsSimUnitActor* A : UnitActors)
	{
		if (!A || (A->GetFaction() == 0) != bOwn)
			continue; // own = faction 0 (you); enemy = any other faction
		FVector2D Proj;
		if (PC->ProjectWorldLocationToScreen(A->GetActorLocation(), Proj))
		{
			const float D = FVector2D::Distance(Proj, ScreenPos);
			if (D < BestDist) { BestDist = D; BestId = A->GetSimUnitId(); }
		}
	}
	return BestId;
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
		if (U && U->GetFaction() == 0 && PC->ProjectWorldLocationToScreen(U->GetActorLocation(), Proj)
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
		if (A && A->GetFaction() == 0)
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
			Killed.Add(E.Subject);

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
			A->SetSimUnitId(S.Id);
			A->SetFaction(S.Faction);
			A->SnapToSimPosition(S.X, S.Y);
			ActorsById.Add(S.Id, A);
		}
		else
		{
			A->SetSimPosition(S.X, S.Y);
		}
		A->SetHealth(S.Hp, S.MaxHp);
		UnitActors.Add(A);
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

	// First snapshot: centre the camera on your (faction-0) units, like an RTS start.
	if (!bCameraFramedOnPlayer)
	{
		FVector2D Sum(0, 0);
		int32 N = 0;
		for (const FRtsUnitSnapshot& S : Reply.Units)
			if (S.Faction == 0) { Sum += FVector2D(S.X, S.Y) * SimCellUU; ++N; }
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
	bFinished = false;
	bHostAlive = true;
	if (GEngine)
		GEngine->ClearOnScreenDebugMessages();
	UE_LOG(LogTemp, Display, TEXT("RTS_MATCH_RESTART: new match from tick 0"));
	return true;
}
