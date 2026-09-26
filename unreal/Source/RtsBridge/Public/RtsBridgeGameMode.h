// Slice 4: continuously hosts the sim, renders the authoritative roster with faction
// colors, and turns player input into tick-stamped move commands. Host failure is
// surfaced and halts stepping — never a locally fabricated state.
#pragma once

#include "CoreMinimal.h"
#include "GameFramework/GameModeBase.h"
#include "RtsBridgeGameMode.generated.h"

class URtsSimHostBridge;
class ARtsSimUnitActor;
struct FRtsHostReply;

UCLASS()
class RTSBRIDGE_API ARtsBridgeGameMode : public AGameModeBase
{
	GENERATED_BODY()

public:
	virtual void StartPlay() override;
	virtual void Tick(float DeltaSeconds) override;

	/** Player-side entry points (called by ARtsRtsPlayerController). */
	void RequestMoveTo(const FVector& WorldDestination);   // move selected (faction 0)
	void ToggleSelectUnderCursor(const FVector2D& ScreenPos);
	void SelectAll();

	bool IsHostAlive() const { return bHostAlive; }

private:
	UPROPERTY()
	TObjectPtr<URtsSimHostBridge> Bridge;

	UPROPERTY()
	TArray<TObjectPtr<ARtsSimUnitActor>> UnitActors;

	TSet<int32> SelectedIds;
	// Command bodies (tick-less) queued by input; Tick stamps the current tick and sends them.
	TArray<FString> PendingCommandBodies;
	int32 NextTickToSend = 0;
	bool bHostAlive = false;
	bool bFinished = false;
	double WaitStart = 0.0;

	void SyncActorsFromReply(FRtsHostReply& Reply);
	void FailHost(const FString& Reason);
};
