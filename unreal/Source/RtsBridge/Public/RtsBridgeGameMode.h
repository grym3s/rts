// Slice 4: continuously hosts the sim, renders the authoritative roster with faction
// colors, and turns player input into tick-stamped move commands. Host failure is
// surfaced and halts stepping — never a locally fabricated state.
#pragma once

#include "CoreMinimal.h"
#include "GameFramework/GameModeBase.h"
#include "RtsSelection.h"
#include "RtsSimHostBridge.h"
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

	/** T07: start the same match over. No hotkey by design: only the single-player
	 *  win/lose / pause menu (T19) calls this. Live host: `reset` + `init` on the same process;
	 *  dead host: relaunch it. Clears units, selection, groups and the result banner. */
	bool RestartMatch();

	/** T19: the match result screen is showing (match over + a short beat for the last
	 *  deaths). While up, orders are refused; only the screen's buttons act. */
	bool IsMatchScreenUp() const;
	/** 0..1 fade of the result screen (for the HUD). */
	float MatchScreenAlpha() const;
	int32 GetMatchWinner() const { return MatchWinner; }
	int64 GetMatchEndTick() const { return MatchEndTick; }
	/** Units each faction fielded and lost this match (from snapshots and death events). */
	int32 GetFielded(int32 Faction) const { return Fielded.IsValidIndex(Faction) ? Fielded[Faction] : 0; }
	int32 GetLost(int32 Faction) const { return Lost.IsValidIndex(Faction) ? Lost[Faction] : 0; }
	/** Restart is single-player only (standalone), from this screen — never a hotkey. */
	bool CanRestart() const;
	/** Leave the match (ends PIE in the editor, quits a packaged game). */
	void QuitMatch();

	/** T20 debug overlays (F4, read-only views of sim state). Bits combine. */
	enum EDebugOverlay : int32 { DebugStats = 1, DebugPaths = 2, DebugTargets = 4, DebugLabels = 8 };
	void SetDebugMask(int32 Mask);
	int32 GetDebugMask() const { return DebugMask; }
	bool IsDebugOn(int32 Bit) const { return (DebugMask & Bit) != 0; }
	/** Latest host debug row for a unit (null when overlays are off or the unit is gone). */
	const FRtsDebugUnit* GetDebugUnit(int32 SimUnitId) const { return DebugById.Find(SimUnitId); }
	/** Smoothed host World.Step time and bridge round trip, in ms; ticks simulated so far. */
	double GetStepMs() const { return StepMsAvg; }
	double GetRoundTripMs() const { return RoundTripMsAvg; }
	int32 GetSimTick() const { return NextTickToSend; }

	bool IsHostAlive() const { return bHostAlive; }

	/** T35: latest economy rows from the host (credits/power per faction, veins, raw unit rows). */
	const FRtsFactionSnapshot* GetFactionState(int32 Faction) const
	{
		for (const FRtsFactionSnapshot& F : Factions)
			if (F.Faction == Faction)
				return &F;
		return nullptr;
	}
	const TArray<FRtsVeinSnapshot>& GetVeins() const { return Veins; }
	/** The latest snapshot row of a unit (building/queue/harvest fields), or null. */
	const FRtsUnitSnapshot* GetUnitRow(int32 SimUnitId) const { return RowsById.Find(SimUnitId); }
	const TMap<int32, FRtsUnitSnapshot>& GetUnitRows() const { return RowsById; }

	/** T36 build menu. Catalog from the host's init reply (static content). */
	const TArray<FRtsBuildingType>& GetBuildingTypes() const { return BuildingTypes; }
	const TArray<FRtsUnitType>& GetUnitTypes() const { return UnitTypes; }
	const FRtsBuildingType* FindBuildingType(const FString& Id) const
	{
		return BuildingTypes.FindByPredicate([&Id](const FRtsBuildingType& B) { return B.Id == Id; });
	}
	/** One sidebar entry: what you could place now (prereq from the sim's own check, credits
	 *  from the snapshot) and whether it would tip your power negative. */
	struct FBuildOption
	{
		FString BuildingId;
		FString Name;
		int32 Cost = 0;
		bool bPrereqOk = false;
		bool bAffordable = false;
		bool bPowerWarn = false;
		FString PrereqName; // what is missing, when !bPrereqOk
		bool IsEnabled() const { return bPrereqOk && bAffordable; }
	};
	/** Faction-0 build options (refreshed when your buildings change); empty without a base. */
	const TArray<FBuildOption>& GetBuildOptions();
	void RebuildBuildOptions(bool bHaveBase);
	/** You own at least one building (the sidebar shows), or an MCV that could deploy. */
	bool HasOwnBuildings() const;
	bool HasOwnMcv() const;
	bool SelectionHasMcv() const;
	/** Read-only sim verdict for placing BuildingId at a cell (cached per cell and tick). */
	struct FPlaceAnswer
	{
		bool bAllowed = false;
		FString Reason;
		FIntRect Rect;
	};
	bool QueryPlace(const FString& BuildingId, int32 CellX, int32 CellY, FPlaceAnswer& Out);
	/** Queue `place` (faction 0) at a cell; `deploy` for every selected MCV. */
	bool RequestPlace(const FString& BuildingId, int32 CellX, int32 CellY);
	bool RequestDeploySelected();
	/** Select every MCV you own (the sidebar Deploy button with no MCV selected). */
	void SelectOwnMcvs();

	/** T37: the selected own building that can train something (lowest id), else INDEX_NONE. */
	int32 GetSelectedProducer() const;
	/** Faction-0 unit types trained at a building type, catalog order. */
	TArray<FRtsUnitType> TrainableAt(const FString& BuildingTypeId) const;
	/** Queue Count `train` commands (the sim caps the queue and refuses what you can't afford). */
	bool RequestTrain(int32 BuildingSimId, const FString& UnitId, int32 Count);
	/** `cancel` queue slot Index (refund), or the last queued unit of a type. */
	bool RequestCancel(int32 BuildingSimId, int32 Index);
	bool RequestCancelLastOf(int32 BuildingSimId, const FString& UnitId);
	/** `rally` every selected own producing building at a world ground point. */
	bool RequestRallySelected(const FVector& WorldPoint);

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

	/** Which content the match loads (content/scenarios/<id>.json, content/maps/<id>.json).
	 *  T36: the match is the skirmish start (one MCV each) unless RTS_SCENARIO names another
	 *  scenario (e.g. tri-point-30, the 30v30 battle the content tests use). */
	static constexpr const TCHAR* BattleScenarioId = TEXT("tri-point-30");
	static constexpr const TCHAR* SkirmishScenarioId = TEXT("tri-point-skirmish");
	static FString MatchScenarioId();
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

	// Killed units playing their death sequence (T15). Out of ActorsById/UnitActors, so
	// they are never picked, selected or shown on the minimap; each destroys itself.
	TArray<TWeakObjectPtr<ARtsSimUnitActor>> DyingActors;

	FRtsSelection Selection;
	/** The host init line for this match (kept so a restart can replay it). */
	FString InitRequest;
	TArray<FString> TerrainRows;
	// Command bodies (tick-less) queued by input; Tick stamps the current tick and sends them.
	TArray<FString> PendingCommandBodies;
	int32 NextTickToSend = 0;
	bool bHostAlive = false;
	bool bFinished = false;
	bool bMatchReported = false; // log/show the T06 result once
	int32 MatchWinner = -1;
	int64 MatchEndTick = -1;
	double MatchOverRealTime = 0.0;
	static constexpr double MatchScreenDelay = 1.5; // let the last deaths play
	static constexpr double MatchScreenFade = 0.4;
	TArray<int32> Fielded = {0, 0, 0};
	TArray<int32> Lost = {0, 0, 0};
	int32 DebugMask = 0;
	TMap<int32, FRtsDebugUnit> DebugById;
	TArray<FRtsFactionSnapshot> Factions;
	TArray<FRtsVeinSnapshot> Veins;
	TMap<int32, FRtsUnitSnapshot> RowsById;
	TArray<FRtsBuildingType> BuildingTypes;
	TArray<FRtsUnitType> UnitTypes;
	TArray<FBuildOption> BuildOptions;
	FString BuildOptionsKey; // own buildings + done state the options were computed for
	FString PlaceCacheKey;
	FPlaceAnswer PlaceCache;
	bool IsMcvRow(const FRtsUnitSnapshot& R) const { return !R.DeploysTo.IsEmpty(); }
	/** Box and select-all take units only; buildings are clicked one at a time. */
	bool IsBuildingId(int32 Id) const { const FRtsUnitSnapshot* R = RowsById.Find(Id); return R && R->IsBuilding(); }
	double StepMsAvg = 0.0;
	double RoundTripMsAvg = 0.0;
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
