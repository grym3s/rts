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
class ARtsVeinActor;
class ARtsTerrainActor;
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
	/** Weaver: selected repairers mend your own damaged building `TargetSimId`; false = nothing queued. */
	bool RequestRepair(int32 TargetSimId);
	/** Sim id of the player's own unit within click tolerance of ScreenPos, or INDEX_NONE. */
	int32 PickOwnUnitAt(const FVector2D& ScreenPos) const;
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
	 *  deaths), or the faction pick is. While up, orders are refused; only the screen's
	 *  buttons act. */
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

	/** The faction you play: 0 Coalition, 1 Hegemony, 2 Ascendant (sim faction index). Chosen
	 *  on the start screen; everything "yours" (orders, selection, build menu, credits, the
	 *  result) follows it. Static like the map size: one local player per process. */
	static int32 PlayerFaction;
	/** Single player: the faction pick shows before the first tick and again after Play again.
	 *  The sim does not step and orders are refused until a faction is chosen. */
	bool IsFactionPickUp() const { return bChoosingFaction; }
	void ChooseFaction(int32 Faction);
	/** "Coalition" / "Hegemony" / "Ascendant" / "Neutral". */
	static FString FactionName(int32 Faction);
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
	/** T26: bridges from the latest snapshot; the terrain version bumps when one falls or is mended. */
	const TArray<FRtsBridgeSnapshot>& GetBridges() const { return Bridges; }
	int32 GetTerrainVersion() const { return TerrainVersion; }
	/** Bridge whose cell is under this world point, or INDEX_NONE. */
	int32 BridgeAt(const FVector& WorldPoint) const;
	/** T26: an order on a bridge cell. Selected engineers repair a damaged/fallen bridge; with
	 *  bAttack (Ctrl+RMB or attack-move click) selected armed units shoot it. False = not a bridge
	 *  job (the click falls through to a move). */
	bool RequestBridgeAt(const FVector& WorldPoint, bool bAttack);
	/** T28: RMB on an enemy/neutral: capturing engineers take it (building or MCV), the rest attack. */
	bool RequestCaptureOrAttack(int32 TargetSimId);
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
	const FRtsUnitType* FindUnitType(const FString& Id) const
	{
		return UnitTypes.FindByPredicate([&Id](const FRtsUnitType& U) { return U.Id == Id; });
	}
	/** Display name of a unit or building row ("Engineer", "Oil Derrick"), else its id. */
	FString DisplayName(int32 SimUnitId) const;
	/** Hover tooltip text for a unit/building: name, owner + hp, then what you can do with it. */
	TArray<FString> TooltipLines(int32 SimUnitId) const;
	/** Hover tooltip for a bridge or causeway (by bridge id). */
	TArray<FString> BridgeTooltipLines(int32 BridgeId) const;
	/** Any unit or building under the cursor (own or not); buildings by their whole footprint. */
	int32 PickAnyUnitAt(const FVector2D& ScreenPos) const;
	/** Cursor to the ground plane (z = 0). */
	bool ScreenToGround(const FVector2D& ScreenPos, FVector& Out) const;
	static FString OwnerName(int32 Faction);
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
		bool bNeedsWater = false; // naval/offshore: placed on water next to the shore (T25)
		FString Description;      // what it does (content `description`)
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
	/** T38: a live vein within a cell of the point (nearest), else INDEX_NONE. */
	int32 VeinAt(const FVector& WorldPoint) const;
	/** T38: RMB on a vein: selected harvesters get `harvest` (that vein), the rest a plain move. */
	bool RequestHarvestAt(const FVector& WorldPoint);

	// ---- abilities (sim/transport, sim/superweapons, T29 verbs) ----
	/** What the selection can do right now (the ability bar and its keys). */
	struct FSelectionAbilities
	{
		int32 Phase = 0;        // own phase-capable units selected
		int32 PhaseReady = 0;   // of those, ready to phase
		int32 Conscript = 0;    // conscriptable units selected
		int32 Unload = 0;       // selected holders with someone inside
		int32 Teleport = 0;     // units near one of your Teleport Nodes (two or more nodes)
		int32 Fire = 0;         // selected superweapons
		int32 FireReady = 0;    // of those, charged
		int32 Deploy = 0;       // MCVs selected
	};
	FSelectionAbilities GetSelectionAbilities() const;
	bool RequestPhaseSelected();
	bool RequestConscriptSelected();
	bool RequestUnloadSelected();
	/** RMB on an own transport / bunker / tunnel: the selected units that fit get `load`. */
	bool RequestLoad(int32 HolderSimId);
	/** Selected units near another own node appear at Teleport Node `NodeSimId`. */
	bool RequestTeleportTo(int32 NodeSimId);
	/** The selected charged superweapon strikes a world ground point. */
	bool RequestFireAt(const FVector& WorldPoint);
	const FRtsUnitSnapshot* GetUnitRowConst(int32 SimUnitId) const { return RowsById.Find(SimUnitId); }
	const TMap<int32, FRtsUnitSnapshot>& GetRows() const { return RowsById; }

	// ---- fog of war (presentation: the sim has none; the host sends sight and radar) ----
	/** Fog grid of FogCell x FogCell sim cells: 0 never seen, 1 seen before, 2 in sight now. */
	static constexpr int32 FogCell = 2;
	int32 GetFogW() const { return FogW; }
	int32 GetFogH() const { return FogH; }
	uint8 FogAt(int32 X, int32 Y) const { return (X >= 0 && Y >= 0 && X < FogW && Y < FogH) ? Fog[Y * FogW + X] : 0; }
	bool IsFogEnabled() const { return bFogEnabled && !IsMatchScreenUp(); }
	void ToggleFog() { bFogEnabled = !bFogEnabled; }
	/** Whether you can see this row now (own: unless riding inside; enemy: in sight and not
	 *  cloaked from you; enemy buildings: once seen, remembered). */
	bool IsRowVisible(const FRtsUnitSnapshot& R) const;

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

	UPROPERTY()
	TObjectPtr<ARtsVeinActor> VeinActor; // T38
	UPROPERTY()
	TObjectPtr<ARtsTerrainActor> TerrainActor; // T26: rebuilt when a bridge falls or is mended
	TArray<FRtsBridgeSnapshot> Bridges;
	int32 TerrainVersion = 0;

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
	bool bChoosingFaction = false;
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
	void UpdateFog();
	void ResetFog();
	/** T18: explosions for deaths (vehicles, buildings) and superweapon strikes, capped. */
	void SpawnExplosion(const FVector& Where, float SizeUU);
	static constexpr int32 MaxExplosions = 24;
	TArray<TWeakObjectPtr<class ARtsExplosionActor>> Explosions;
	struct FStrikeMemo { bool bStrike = false; int32 StrikeIn = 0; double X = 0, Y = 0, R = 0; };
	TMap<int32, FStrikeMemo> StrikeMemo;
	TArray<uint8> Fog;
	int32 FogW = 0, FogH = 0;
	TSet<int32> SeenBuildings;
	bool bFogEnabled = true;
	FString SelectedIdsJson(TFunctionRef<bool(const FRtsUnitSnapshot&)> Pred) const;
	int32 PickUnitAt(const FVector2D& ScreenPos, bool bOwn) const;
	/** Mode 0 own, 1 not own, 2 any. A unit within 40 px of the cursor wins; else a building
	 *  whose footprint the cursor is over (big buildings are clickable anywhere on them). */
	int32 PickUnitImpl(const FVector2D& ScreenPos, int32 Mode) const;
	/** Queue one order for the selected units; TargetJson is the raw `target` value or empty. */
	bool QueueOrderForSelection(const TCHAR* Type, const FString& TargetJson, bool bSkipHarvesters = false);
	void RefreshSelectionRings();
	void FailHost(const FString& Reason);
};
