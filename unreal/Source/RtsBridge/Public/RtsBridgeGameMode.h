// Slice 4: continuously hosts the sim, renders the authoritative roster with faction
// colors, and turns player input into tick-stamped move commands. Host failure is
// surfaced and halts stepping — never a locally fabricated state.
#pragma once

#include "CoreMinimal.h"
#include "GameFramework/GameModeBase.h"
#include "RtsSelection.h"
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
	bool RequestMoveTo(const FVector& WorldDestination);   // move selected (faction 0)
	bool RequestAttackMoveTo(const FVector& WorldDestination); // attack-move selected
	bool RequestAttackUnit(int32 TargetSimId);             // attack one unit (sim id)
	bool RequestStop();                                    // stop selected
	/** Sim id of a non-own unit within click tolerance of ScreenPos, or INDEX_NONE. */
	int32 PickEnemyUnitAt(const FVector2D& ScreenPos) const;
	bool HasSelection() const { return Selection.GetSelected().Num() > 0; }
	/** Click-select the own unit nearest ScreenPos (empty ground clears; shift toggles). */
	void ClickSelect(const FVector2D& ScreenPos, bool bShift);
	/** Select own units whose screen projection is inside the rect A..B (shift adds). */
	void BoxSelect(const FVector2D& A, const FVector2D& B, bool bShift);
	void SelectAll();
	/** Control groups 0..2 (keys 1–3): Ctrl assigns, plain recalls. */
	void AssignGroup(int32 Group);
	bool RecallGroup(int32 Group);
	/** World-XY centroid of the selected units (for double-tap "jump to group"). */
	bool SelectionCentroid(FVector2D& OutWorldXY) const;

	bool IsHostAlive() const { return bHostAlive; }

	/** Fixture map size in cells (must match the init request's map) and world scale. */
	// Set from the loaded scenario in StartPlay (before any HUD/camera use of them).
	static int32 MapCellsW;
	static int32 MapCellsH;
	static constexpr float CellUU = 100.0f;
	static FBox2D MapWorldBounds() { return FBox2D(FVector2D(0.0, 0.0), FVector2D(MapCellsW * CellUU, MapCellsH * CellUU)); }

	/** Read-only views for the HUD/minimap (presentation of host snapshots). */
	const TArray<TObjectPtr<ARtsSimUnitActor>>& GetUnitActors() const { return UnitActors; }
	bool IsSelected(int32 SimUnitId) const { return Selection.Contains(SimUnitId); }
	const TSet<int32>& GetSelectedIds() const { return Selection.GetSelected(); }
	/** Tile rows of the loaded map ('.' land, '~' water, '#' cliff, '=' bridge). */
	const TArray<FString>& GetTerrainRows() const { return TerrainRows; }

	/** Which content the match loads (content/scenarios/<id>.json, content/maps/<id>.json). */
	static constexpr const TCHAR* ScenarioId = TEXT("tri-point-30");
	static constexpr const TCHAR* MapId = TEXT("tri-point");
	ARtsSimUnitActor* FindUnitActor(int32 SimUnitId) const
	{
		const TObjectPtr<ARtsSimUnitActor>* A = ActorsById.Find(SimUnitId);
		return A ? A->Get() : nullptr;
	}

private:
	UPROPERTY()
	TObjectPtr<URtsSimHostBridge> Bridge;

	UPROPERTY()
	TArray<TObjectPtr<ARtsSimUnitActor>> UnitActors;

	// Actors are keyed by sim id, so a death never hands one unit's actor to another
	// (that would glide it across the map under interpolation). UnitActors = snapshot order.
	UPROPERTY()
	TMap<int32, TObjectPtr<ARtsSimUnitActor>> ActorsById;

	FRtsSelection Selection;
	TArray<FString> TerrainRows;
	// Command bodies (tick-less) queued by input; Tick stamps the current tick and sends them.
	TArray<FString> PendingCommandBodies;
	int32 NextTickToSend = 0;
	bool bHostAlive = false;
	bool bFinished = false;
	bool bMatchReported = false; // log/show the T06 result once
	bool bCameraFramedOnPlayer = false;  // start the view on your own units (once)
	double WaitStart = 0.0;
	double StepAccum = 0.0; // per match (a function static leaked across PIE sessions)

	void SyncActorsFromReply(FRtsHostReply& Reply);
	/** Sim id of the own (faction 0) unit within click tolerance of ScreenPos, or INDEX_NONE. */
	int32 PickOwnUnitAt(const FVector2D& ScreenPos) const;
	int32 PickUnitAt(const FVector2D& ScreenPos, bool bOwn) const;
	/** Queue one order for the selected units; TargetJson is the raw `target` value or empty. */
	bool QueueOrderForSelection(const TCHAR* Type, const FString& TargetJson);
	void RefreshSelectionRings();
	void FailHost(const FString& Reason);
};
