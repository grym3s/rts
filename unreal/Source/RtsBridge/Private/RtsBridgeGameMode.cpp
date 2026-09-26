// Slice 4: host lifecycle + authoritative rendering + input->command conversion.
// Scenario roster/positions/colors are presentation fixtures ONLY; the host owns every
// rule. On any host/protocol failure we stop stepping and surface the error — never
// fabricate state.
#include "RtsBridgeGameMode.h"
#include "RtsSimHostBridge.h"
#include "RtsSimUnitActor.h"
#include "RtsRtsPlayerController.h"
#include "Engine/World.h"
#include "Engine/StaticMeshActor.h"
#include "Engine/DirectionalLight.h"
#include "Components/DirectionalLightComponent.h"
#include "Engine/SkyLight.h"
#include "Components/StaticMeshComponent.h"
#include "Kismet/GameplayStatics.h"
#include "HAL/PlatformTime.h"

// Presentation fixture: one faction-0 squad of 4 collectors on a 16x16 field.
// (Roster contents are a display choice; unit stats/rules come from content/ via the host.)
static const TCHAR* InitRequest = TEXT(
	"{\"protocolVersion\":1,\"kind\":\"init\",\"scenario\":"
	"{\"schemaVersion\":2,\"seed\":1,\"map\":{\"width\":16,\"height\":16,\"blocked\":[]},"
	"\"units\":["
	"{\"unit\":\"collector\",\"at\":[4,4]},{\"unit\":\"collector\",\"at\":[5,4]},"
	"{\"unit\":\"collector\",\"at\":[4,5]},{\"unit\":\"collector\",\"at\":[5,5]}]}}");

// Design scale: 100 UU per map cell.
static constexpr float SimCellUU = 100.0f;

void ARtsBridgeGameMode::StartPlay()
{
	Super::StartPlay();
	PrimaryActorTick.bCanEverTick = true;

	// --- Battlefield: ground plane + sun (placeholder art; real terrain is slice 5+). ---
	FActorSpawnParameters SP;
	SP.Name = TEXT("Battlefield");
	if (AStaticMeshActor* Ground = GetWorld()->SpawnActor<AStaticMeshActor>(
		FVector(8.0 * SimCellUU, 8.0 * SimCellUU, 0.0), FRotator::ZeroRotator, SP))
	{
		static ConstructorHelpers::FObjectFinder<UStaticMesh> PlaneFinder(
			TEXT("/Engine/BasicShapes/Plane.Plane"));
		static ConstructorHelpers::FObjectFinder<UMaterialInterface> GridFinder(
			TEXT("/Engine/EngineMaterials/WorldGridMaterial.WorldGridMaterial"));
		if (PlaneFinder.Succeeded())
			Ground->GetStaticMeshComponent()->SetStaticMesh(PlaneFinder.Object);
		if (GridFinder.Succeeded())
			Ground->GetStaticMeshComponent()->SetMaterial(0, GridFinder.Object);
		Ground->SetActorScale3D(FVector(16.0, 16.0, 1.0)); // 16x16 cells at 100 UU (plane mesh is 100 UU)
		Ground->SetActorEnableCollision(true);
	}
	if (ADirectionalLight* Sun = GetWorld()->SpawnActor<ADirectionalLight>(FVector::ZeroVector, FRotator(-45.0f, 30.0f, 0.0f)))
		Sun->GetComponent()->SetIntensity(8.0f);
	GetWorld()->SpawnActor<ASkyLight>(FVector::ZeroVector, FRotator::ZeroRotator);

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
	UE_LOG(LogTemp, Display, TEXT("RTS_SESSION: host up, stepping live (select: LMB/A, move: RMB)"));
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
		SelectedIds.Add(A->GetSimUnitId());
		A->SetSelected(true);
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
