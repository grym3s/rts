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
	ARtsBridgeGameMode();
	virtual void StartPlay() override;
	virtual void Tick(float DeltaSeconds) override;

	/** Player-side entry points (called by ARtsRtsPlayerController). */
	void RequestMoveTo(const FVector& WorldDestination);   // move selected (faction 0)
	void ToggleSelectUnderCursor(const FVector2D& ScreenPos);
	void SelectAll();

	bool IsHostAlive() const { return bHostAlive; }

	/** Fixture map size in cells (must match the init request's map) and world scale. */
	static constexpr int32 MapCellsW = 32;
	static constexpr int32 MapCellsH = 24;
	static constexpr float CellUU = 100.0f;
	static FBox2D MapWorldBounds() { return FBox2D(FVector2D(0.0, 0.0), FVector2D(MapCellsW * CellUU, MapCellsH * CellUU)); }

	/** Read-only views for the HUD/minimap (presentation of host snapshots). */
	const TArray<TObjectPtr<ARtsSimUnitActor>>& GetUnitActors() const { return UnitActors; }
	bool IsSelected(int32 SimUnitId) const { return SelectedIds.Contains(SimUnitId); }

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
	bool bCameraFramedOnPlayer = false;  // start the view on your own units (once)
	double WaitStart = 0.0;

	void SyncActorsFromReply(FRtsHostReply& Reply);
	void FailHost(const FString& Reason);
};
