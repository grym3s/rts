// Slice 4: host lifecycle + authoritative rendering + input->command conversion.
// Scenario roster/positions/colors are presentation fixtures ONLY; the host owns every
// rule. On any host/protocol failure we stop stepping and surface the error — never
// fabricate state.
#include "RtsBridgeGameMode.h"
#include "RtsSimHostBridge.h"
#include "RtsSimUnitActor.h"
#include "RtsRtsPlayerController.h"
#include "RtsMinimapHud.h"
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

// Presentation fixture: the skirmish-10v10 roster (content/scenarios/skirmish-10v10.json,
// minus its scripted commands) — 10 riflemen (faction 0, you, top-left) vs 10 conscripts
// (faction 1, bottom-right) on a 32x24 field. Map size must match MapCellsW/H.
// (Roster contents are a display choice; unit stats/rules come from content/ via the host.)
static const TCHAR* InitRequest = TEXT(
	"{\"protocolVersion\":1,\"kind\":\"init\",\"scenario\":"
	"{\"schemaVersion\":2,\"seed\":7,\"map\":{\"width\":32,\"height\":24,\"blocked\":[]},"
	"\"units\":["
	"{\"unit\":\"rifleman\",\"at\":[4,6]},{\"unit\":\"rifleman\",\"at\":[5,6]},{\"unit\":\"rifleman\",\"at\":[6,6]},"
	"{\"unit\":\"rifleman\",\"at\":[4,7]},{\"unit\":\"rifleman\",\"at\":[5,7]},{\"unit\":\"rifleman\",\"at\":[6,7]},"
	"{\"unit\":\"rifleman\",\"at\":[4,8]},{\"unit\":\"rifleman\",\"at\":[5,8]},{\"unit\":\"rifleman\",\"at\":[6,8]},"
	"{\"unit\":\"rifleman\",\"at\":[5,9]},"
	"{\"unit\":\"conscript\",\"at\":[26,15]},{\"unit\":\"conscript\",\"at\":[27,15]},{\"unit\":\"conscript\",\"at\":[28,15]},"
	"{\"unit\":\"conscript\",\"at\":[26,16]},{\"unit\":\"conscript\",\"at\":[27,16]},{\"unit\":\"conscript\",\"at\":[28,16]},"
	"{\"unit\":\"conscript\",\"at\":[26,17]},{\"unit\":\"conscript\",\"at\":[27,17]},{\"unit\":\"conscript\",\"at\":[28,17]},"
	"{\"unit\":\"conscript\",\"at\":[27,18]}]}}");

// Design scale: 100 UU per map cell.
static constexpr float SimCellUU = ARtsBridgeGameMode::CellUU;

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

	// --- Battlefield: ground plane + sun (placeholder art; real terrain is slice 5+). ---
	FActorSpawnParameters SP;
	SP.Name = TEXT("Battlefield");
	if (AStaticMeshActor* Ground = GetWorld()->SpawnActor<AStaticMeshActor>(
		FVector(MapCellsW * 0.5 * SimCellUU, MapCellsH * 0.5 * SimCellUU, 0.0), FRotator::ZeroRotator, SP))
	{
		// Runtime-spawned StaticMeshActors are Static mobility: SetStaticMesh/SetMaterial
		// then silently do nothing (black void instead of ground). Make it Movable first.
		Ground->GetStaticMeshComponent()->SetMobility(EComponentMobility::Movable);
		// Runtime load: ConstructorHelpers::FObjectFinder is constructor-only and is a fatal
		// error when StartPlay runs in PIE.
		if (UStaticMesh* Plane = LoadObject<UStaticMesh>(nullptr, TEXT("/Engine/BasicShapes/Plane.Plane")))
			Ground->GetStaticMeshComponent()->SetStaticMesh(Plane);
		// Dark green field so faction colours (red/blue) read clearly against it.
		if (UMaterialInterface* Base = LoadObject<UMaterialInterface>(nullptr,
				TEXT("/Engine/BasicShapes/BasicShapeMaterial.BasicShapeMaterial")))
			if (UMaterialInstanceDynamic* GroundMid = UMaterialInstanceDynamic::Create(Base, Ground))
			{
				GroundMid->SetVectorParameterValue(TEXT("Color"), FLinearColor(0.035f, 0.07f, 0.035f));
				Ground->GetStaticMeshComponent()->SetMaterial(0, GroundMid);
			}
		Ground->SetActorScale3D(FVector(MapCellsW, MapCellsH, 1.0)); // one cell = 100 UU (plane mesh is 100 UU)
		Ground->SetActorEnableCollision(true);
	}
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
		if (!RtsPC)
			UE_LOG(LogTemp, Warning, TEXT("RtsBridge: PlayerController is not ARtsRtsPlayerController — check DefaultEngine.ini"));
	}

	// --- Host: launch + init. Failure is reported, not faked. ---
	Bridge = NewObject<URtsSimHostBridge>(this);
	FString Err;
	if (!Bridge->StartAndInit(InitRequest, Err))
	{
		FailHost(FString::Printf(TEXT("HOST START FAILED: %s"), *Err));
		return;
	}
	bHostAlive = true;
	UE_LOG(LogTemp, Display, TEXT("RTS_SESSION: host up, stepping live (select: LMB/Ctrl+A, move: RMB, minimap: bottom-left)"));
}

void ARtsBridgeGameMode::Tick(float DeltaSeconds)
{
	Super::Tick(DeltaSeconds);
	if (bFinished || !Bridge || !bHostAlive)
		return;

	// Fixed sim cadence: 20 ticks/s (SimWorld.TicksPerSecond). One step per due frame.
	static double Accum = 0.0;
	static const double TickDt = 1.0 / 20.0;
	Accum += DeltaSeconds;
	while (Accum >= TickDt && !bFinished && bHostAlive)
	{
		Accum -= TickDt;
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
	}
}

void ARtsBridgeGameMode::RequestMoveTo(const FVector& WorldDestination)
{
	if (!bHostAlive || SelectedIds.Num() == 0)
		return;
	const double MapX = WorldDestination.X / SimCellUU;
	const double MapY = WorldDestination.Y / SimCellUU;
	FString Ids;
	for (const int32 Id : SelectedIds)
	{
		if (Ids.Len())
			Ids += TEXT(",");
		Ids += FString::Printf(TEXT("%d"), Id);
	}
	// Tick-less body; Tick() wraps it as {"tick":N, <body>} when it sends.
	PendingCommandBodies.Add(FString::Printf(
		TEXT("\"faction\":0,\"type\":\"move\",\"units\":[%s],\"target\":[%.4f,%.4f]"),
		*Ids, MapX, MapY));
}

void ARtsBridgeGameMode::ToggleSelectUnderCursor(const FVector2D& ScreenPos)
{
	APlayerController* PC = UGameplayStatics::GetPlayerController(this, 0);
	if (!PC)
		return;
	// Project each unit to screen; select the closest within a click radius.
	int32 BestIdx = INDEX_NONE;
	float BestDist = 60.0f; // px click tolerance
	for (int32 i = 0; i < UnitActors.Num(); ++i)
	{
		if (UnitActors[i]->GetFaction() != 0)
			continue; // you only select your own (faction 0) units
		FVector2D Proj;
		if (PC->ProjectWorldLocationToScreen(UnitActors[i]->GetActorLocation(), Proj))
		{
			const float D = FVector2D::Distance(Proj, ScreenPos);
			if (D < BestDist) { BestDist = D; BestIdx = i; }
		}
	}
	SelectedIds.Reset();
	if (BestIdx != INDEX_NONE)
		SelectedIds.Add(UnitActors[BestIdx]->GetSimUnitId());
	for (int32 i = 0; i < UnitActors.Num(); ++i)
		UnitActors[i]->SetSelected(i == BestIdx);
}

void ARtsBridgeGameMode::SelectAll()
{
	SelectedIds.Reset();
	for (ARtsSimUnitActor* A : UnitActors)
	{
		const bool bMine = A->GetFaction() == 0;
		if (bMine)
			SelectedIds.Add(A->GetSimUnitId());
		A->SetSelected(bMine);
	}
}

void ARtsBridgeGameMode::SyncActorsFromReply(FRtsHostReply& Reply)
{
	while (UnitActors.Num() < Reply.Units.Num())
	{
		ARtsSimUnitActor* A = GetWorld()->SpawnActor<ARtsSimUnitActor>();
		if (!A)
			return;
		UnitActors.Add(A);
	}
	while (UnitActors.Num() > Reply.Units.Num())
		UnitActors.Pop()->Destroy();

	for (int32 i = 0; i < Reply.Units.Num(); ++i)
	{
		const FRtsUnitSnapshot& S = Reply.Units[i];
		ARtsSimUnitActor* A = UnitActors[i];
		if (A->GetSimUnitId() != S.Id)
		{
			A->SetSimUnitId(S.Id);
			A->SetFaction(S.Faction);
		}
		A->SetSimPosition(S.X, S.Y);
		A->SetSelected(SelectedIds.Contains(S.Id));
	}

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
