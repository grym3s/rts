// Slice-3 spike game mode: launch host -> init -> render snapshot -> send one tick-0
// move command -> step once -> re-render from the new snapshot -> clean shutdown.
// All numbers here are presentation fixtures; rules live in the .NET host only.
#pragma once

#include "CoreMinimal.h"
#include "GameFramework/GameModeBase.h"
#include "RtsBridgeGameMode.generated.h"

class URtsSimHostBridge;
class ARtsSimUnitActor;

UCLASS()
class RTSBRIDGE_API ARtsBridgeGameMode : public AGameModeBase
{
	GENERATED_BODY()

public:
	virtual void StartPlay() override;
	virtual void Tick(float DeltaSeconds) override;

private:
	UPROPERTY()
	TObjectPtr<URtsSimHostBridge> Bridge;

	UPROPERTY()
	TArray<TObjectPtr<ARtsSimUnitActor>> UnitActors;

	int32 NextTickToSend = 0;
	bool bMoveSent = false;
	bool bFinished = false;
	double WaitStart = 0.0;

	void SyncActorsFromReply(struct FRtsHostReply& Reply);
	void FinishAndReport(bool bSuccess, const FString& Message);
};
