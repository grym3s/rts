#include "RtsBridgeGameMode.h"
#include "RtsSimHostBridge.h"
#include "RtsSimUnitActor.h"
#include "Engine/World.h"
#include "Kismet/GameplayStatics.h"
#include "HAL/PlatformTime.h"

// Spike fixtures (presentation-side only — rules live in the host):
// one faction-0 collector at (5,5), commanded to move to (10,5).
static const TCHAR* InitRequest = TEXT(
	"{\"protocolVersion\":1,\"kind\":\"init\",\"scenario\":"
	"{\"schemaVersion\":2,\"seed\":1,\"map\":{\"width\":16,\"height\":16,\"blocked\":[]},"
	"\"units\":[{\"unit\":\"collector\",\"at\":[5,5]}]}}");

static const int32 SpikeTicks = 120;

void ARtsBridgeGameMode::StartPlay()
{
	Super::StartPlay();
	PrimaryActorTick.bCanEverTick = true;

	Bridge = NewObject<URtsSimHostBridge>(this);
	FString Err;
	if (!Bridge->StartAndInit(InitRequest, Err))
	{
		FinishAndReport(false, FString::Printf(TEXT("HOST START FAILED: %s"), *Err));
		return;
	}

	// Render tick-0 snapshot, then queue the move for step 0.
	FRtsHostReply Reply;
	const FString Step0 = TEXT(
		"{\"protocolVersion\":1,\"kind\":\"step\",\"tick\":0,"
		"\"commands\":[{\"tick\":0,\"faction\":0,\"type\":\"move\",\"target\":[10,5]}]}");
	if (!Bridge->Step(Step0, 5.0, Reply) || !Reply.bOk)
	{
		FinishAndReport(false, FString::Printf(TEXT("STEP 0 FAILED: %s"), *Reply.Error));
		return;
	}
	bMoveSent = true;
	NextTickToSend = 1;
	SyncActorsFromReply(Reply);
	WaitStart = FPlatformTime::Seconds();
	UE_LOG(LogTemp, Display, TEXT("RtsBridge: host init OK, move sent, hashing %s at tick %lld"), *Reply.Hash, Reply.Tick);
}

void ARtsBridgeGameMode::Tick(float DeltaSeconds)
{
	Super::Tick(DeltaSeconds);
	if (bFinished || !Bridge || !bMoveSent)
		return;

	// One host step per frame; the host alone advances ticks.
	FRtsHostReply Reply;
	const FString Step = FString::Printf(
		TEXT("{\"protocolVersion\":1,\"kind\":\"step\",\"tick\":%d,\"commands\":[]}"), NextTickToSend);
	if (!Bridge->Step(Step, 5.0, Reply))
	{
		FinishAndReport(false, FString::Printf(TEXT("HOST FAILURE at tick %d: %s"), NextTickToSend, *Reply.Error));
		return;
	}
	if (!Reply.bOk)
	{
		FinishAndReport(false, FString::Printf(TEXT("PROTOCOL ERROR at tick %d: %s"), NextTickToSend, *Reply.Error));
		return;
	}
	NextTickToSend++;
	SyncActorsFromReply(Reply);

	if (NextTickToSend >= SpikeTicks)
	{
		// Round-trip proof: the collector must have moved toward (10,5).
		bool bMoved = false;
		for (const ARtsSimUnitActor* A : UnitActors)
			if (A->GetActorLocation().X > 550.0) // started at 500 (map 5 * 100)
				bMoved = true;
		FinishAndReport(bMoved, bMoved
			? FString::Printf(TEXT("MOVE ROUND-TRIP OK: unit advanced past x=550 at tick %lld, hash %s"), Reply.Tick, *Reply.Hash)
			: TEXT("MOVE ROUND-TRIP FAILED: no unit left the spawn column"));
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
	{
		UnitActors.Pop()->Destroy();
	}
	for (int32 i = 0; i < Reply.Units.Num(); ++i)
		UnitActors[i]->SetSimPosition(Reply.Units[i].X, Reply.Units[i].Y);
}

void ARtsBridgeGameMode::FinishAndReport(bool bSuccess, const FString& Message)
{
	bFinished = true;
	if (Bridge)
		Bridge->Shutdown();
	// Single grep-able line for headless verification.
	UE_LOG(LogTemp, Display, TEXT("RTS_SPIKE_RESULT: %s — %s"), bSuccess ? TEXT("PASS") : TEXT("FAIL"), *Message);
}
