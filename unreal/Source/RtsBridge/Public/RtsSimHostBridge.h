// Process bridge to the .NET sim host (newline-delimited JSON-lines over stdio pipes).
// Uses only FPlatformProcess cross-platform APIs: CreatePipe / CreateProc / WritePipe /
// ReadPipe / ClosePipe — identical call shape on Linux and Windows.
#pragma once

#include "CoreMinimal.h"
#include "HAL/PlatformProcess.h"
#include "RtsSimHostBridge.generated.h"

/** One validated unit from a host snapshot. Positions are sim map coords (double);
 *  presentation maps them at 100 UU per cell (design doc). */
USTRUCT(BlueprintType)
struct FRtsUnitSnapshot
{
	GENERATED_BODY()

public:
	UPROPERTY(BlueprintReadOnly)
	int32 Id = INDEX_NONE;

	UPROPERTY(BlueprintReadOnly)
	int32 Faction = 0;

	UPROPERTY(BlueprintReadOnly)
	double X = 0.0;

	UPROPERTY(BlueprintReadOnly)
	double Y = 0.0;

	/** Health (T15), decoded from Q32.32 like positions. MaxHp is fixed for the unit's life. */
	UPROPERTY(BlueprintReadOnly)
	double Hp = 0.0;

	UPROPERTY(BlueprintReadOnly)
	double MaxHp = 0.0;

	/** T30 economy fields (T35-T38 presentation). Building: catalog id, empty for units. */
	UPROPERTY(BlueprintReadOnly)
	FString BuildingId;

	/** Construction ticks left / total (0 / 0 when done or not a building). */
	UPROPERTY(BlueprintReadOnly)
	int32 ConstructionRemaining = 0;

	UPROPERTY(BlueprintReadOnly)
	int32 ConstructionTotal = 0;

	/** Production queue (unit catalog ids, head first) and the head's ticks left / total. */
	UPROPERTY(BlueprintReadOnly)
	TArray<FString> ProductionQueue;

	UPROPERTY(BlueprintReadOnly)
	int32 QueueHeadRemaining = 0;

	UPROPERTY(BlueprintReadOnly)
	int32 QueueHeadTotal = 0;

	/** Building footprint in cells (T31/T32; 0 for units). The unit sits at its centre. */
	UPROPERTY(BlueprintReadOnly)
	int32 FootprintW = 0;

	UPROPERTY(BlueprintReadOnly)
	int32 FootprintH = 0;

	/** MCV (T32): the yard a `deploy` turns it into; empty otherwise. */
	UPROPERTY(BlueprintReadOnly)
	FString DeploysTo;

	/** T37: the building's rally point in map cells (bHasRally false = none). */
	UPROPERTY(BlueprintReadOnly)
	bool bHasRally = false;

	UPROPERTY(BlueprintReadOnly)
	double RallyX = 0.0;

	UPROPERTY(BlueprintReadOnly)
	double RallyY = 0.0;

	/** Harvester (T33): credits aboard and whether it is heading home full. */
	UPROPERTY(BlueprintReadOnly)
	bool bHarvester = false;

	UPROPERTY(BlueprintReadOnly)
	int32 Load = 0;

	/** T38: a full load in credits (harvesters only; 0 otherwise). */
	UPROPERTY(BlueprintReadOnly)
	int32 LoadCapacity = 0;

	UPROPERTY(BlueprintReadOnly)
	bool bCarrying = false;

	bool IsBuilding() const { return !BuildingId.IsEmpty(); }
};

/** T30: one faction's credits and power ledger (net = supply - draw; low power when < 0). */
USTRUCT(BlueprintType)
struct FRtsFactionSnapshot
{
	GENERATED_BODY()

public:
	UPROPERTY(BlueprintReadOnly)
	int32 Faction = 0;

	UPROPERTY(BlueprintReadOnly)
	int32 Credits = 0;

	UPROPERTY(BlueprintReadOnly)
	int32 PowerSupply = 0;

	UPROPERTY(BlueprintReadOnly)
	int32 PowerDraw = 0;

	bool IsLowPower() const { return PowerSupply - PowerDraw < 0; }
};

/** T30: a resource vein (map coords) and the credits left in it. */
USTRUCT(BlueprintType)
struct FRtsVeinSnapshot
{
	GENERATED_BODY()

public:
	UPROPERTY(BlueprintReadOnly)
	int32 Id = INDEX_NONE;

	UPROPERTY(BlueprintReadOnly)
	double X = 0.0;

	UPROPERTY(BlueprintReadOnly)
	double Y = 0.0;

	UPROPERTY(BlueprintReadOnly)
	int32 Pool = 0;
};

/** T36: a building type from the init catalog (for the build menu). */
USTRUCT(BlueprintType)
struct FRtsBuildingType
{
	GENERATED_BODY()

public:
	UPROPERTY(BlueprintReadOnly) FString Id;
	UPROPERTY(BlueprintReadOnly) FString Name;
	UPROPERTY(BlueprintReadOnly) int32 Faction = 0;
	UPROPERTY(BlueprintReadOnly) FString Category;
	UPROPERTY(BlueprintReadOnly) int32 Cost = 0;
	UPROPERTY(BlueprintReadOnly) int32 BuildTicks = 0;
	/** Building id required first; empty = none. */
	UPROPERTY(BlueprintReadOnly) FString Prereq;
	/** + supply / - draw. */
	UPROPERTY(BlueprintReadOnly) int32 Power = 0;
	UPROPERTY(BlueprintReadOnly) int32 FootprintW = 1;
	UPROPERTY(BlueprintReadOnly) int32 FootprintH = 1;
};

/** T36: a unit type from the init catalog (production menus, T37). */
USTRUCT(BlueprintType)
struct FRtsUnitType
{
	GENERATED_BODY()

public:
	UPROPERTY(BlueprintReadOnly) FString Id;
	UPROPERTY(BlueprintReadOnly) FString Name;
	UPROPERTY(BlueprintReadOnly) int32 Faction = 0;
	UPROPERTY(BlueprintReadOnly) int32 Cost = 0;
	UPROPERTY(BlueprintReadOnly) int32 BuildTicks = 0;
	UPROPERTY(BlueprintReadOnly) FString BuiltFrom;
	UPROPERTY(BlueprintReadOnly) FString DeploysTo;
	UPROPERTY(BlueprintReadOnly) bool bHarvest = false;
};

/** One sim event from a step reply (T09 contract): shot (Amount = damage), death
 *  (Subject = dead unit, Target = killer, Faction = dead unit's), match_end. */
USTRUCT(BlueprintType)
struct FRtsSimEvent
{
	GENERATED_BODY()

public:
	UPROPERTY(BlueprintReadOnly)
	FString Kind;

	UPROPERTY(BlueprintReadOnly)
	int32 Subject = INDEX_NONE;

	UPROPERTY(BlueprintReadOnly)
	int32 Target = INDEX_NONE;

	UPROPERTY(BlueprintReadOnly)
	double Amount = 0.0;

	UPROPERTY(BlueprintReadOnly)
	int32 Faction = -1;
};

/** T20 debug row for one unit (only when the step asked for `debug`). */
USTRUCT(BlueprintType)
struct FRtsDebugUnit
{
	GENERATED_BODY()

public:
	UPROPERTY(BlueprintReadOnly)
	int32 Id = INDEX_NONE;

	/** idle, move, attack-move, attack, guard, returning (host names). */
	UPROPERTY(BlueprintReadOnly)
	FString State;

	UPROPERTY(BlueprintReadOnly)
	int32 Target = INDEX_NONE;

	/** Remaining path waypoints in map coords (cells). */
	UPROPERTY(BlueprintReadOnly)
	TArray<FVector2D> Path;
};

/** Result of one host request/response round trip. */
USTRUCT(BlueprintType)
struct FRtsHostReply
{
	GENERATED_BODY()

public:
	UPROPERTY(BlueprintReadOnly)
	bool bOk = false;

	UPROPERTY(BlueprintReadOnly)
	int64 Tick = -1;

	/** Hex state hash exactly as the host sent it (16 lowercase digits). */
	UPROPERTY(BlueprintReadOnly)
	FString Hash;

	/** Match outcome from state.match (T06). Winner -1 = draw. */
	UPROPERTY(BlueprintReadOnly)
	bool bMatchOver = false;

	UPROPERTY(BlueprintReadOnly)
	int32 MatchWinner = -1;

	UPROPERTY(BlueprintReadOnly)
	TArray<FRtsUnitSnapshot> Units;

	/** T30 economy: per-faction credits/power and the veins (empty when the host sent none). */
	UPROPERTY(BlueprintReadOnly)
	TArray<FRtsFactionSnapshot> Factions;

	UPROPERTY(BlueprintReadOnly)
	TArray<FRtsVeinSnapshot> Veins;

	/** Events of the step this reply answers (empty for init/reset). */
	UPROPERTY(BlueprintReadOnly)
	TArray<FRtsSimEvent> Events;

	/** T20: state.debug was present. StepMicros = host World.Step wall time. */
	UPROPERTY(BlueprintReadOnly)
	bool bHasDebug = false;

	UPROPERTY(BlueprintReadOnly)
	int64 StepMicros = 0;

	UPROPERTY(BlueprintReadOnly)
	TArray<FRtsDebugUnit> DebugUnits;

	/** T36: catalogs (init replies only). */
	UPROPERTY(BlueprintReadOnly)
	TArray<FRtsBuildingType> BuildingTypes;

	UPROPERTY(BlueprintReadOnly)
	TArray<FRtsUnitType> UnitTypes;

	/** T36: answer to a `query` (place): allowed, else reason; the footprint rect in cells. */
	UPROPERTY(BlueprintReadOnly)
	bool bHasQuery = false;

	UPROPERTY(BlueprintReadOnly)
	bool bQueryAllowed = false;

	UPROPERTY(BlueprintReadOnly)
	FString QueryReason;

	UPROPERTY(BlueprintReadOnly)
	FIntRect QueryRect;

	/** Wall time of this request/response round trip, measured by the bridge. */
	UPROPERTY(BlueprintReadOnly)
	double RoundTripMs = 0.0;

	/** Error text/code from the host, or a local protocol/host-failure reason. */
	UPROPERTY(BlueprintReadOnly)
	FString Error;
};

UCLASS()
class RTSBRIDGE_API URtsSimHostBridge : public UObject
{
	GENERATED_BODY()

public:
	/** Locate the host binary (RTS_HOST_BIN env, else <ProjectDir>/dist/<rid>/RtsHost),
	 *  launch it, and send `init`. bSuccess=false + OutError on any launch/protocol failure —
	 *  never a fabricated state. */
	bool StartAndInit(const FString& InitRequestJson, FString& OutError);

	/** Send one step request line, wait for its response line, validate the envelope.
	 *  Any malformed line, host exit, or timeout fails with bOk=false. */
	bool Step(const FString& StepRequestJson, double TimeoutSeconds, FRtsHostReply& OutReply);

	/** T36: read-only `place` query (never advances the world). */
	bool QueryPlace(int32 Faction, const FString& BuildingId, int32 CellX, int32 CellY, FRtsHostReply& OutReply);

	/** The reply to the last successful init (carries the T36 catalogs). */
	const FRtsHostReply& GetInitReply() const { return InitReply; }

	/** Close stdin (host exits on EOF), then terminate if it lingers. Always safe to call. */
	void Shutdown();

	virtual void BeginDestroy() override;

private:
	FProcHandle ProcHandle;
	void* WriteParent = nullptr;  // we write requests -> host stdin
	void* ReadParent = nullptr;   // host stdout -> we read responses
	void* ErrParent = nullptr;    // host stderr -> logged, never parsed
	bool bPipeOpen = false;
	FRtsHostReply InitReply;

	/** Read until one complete '\n'-terminated line or deadline; false on timeout/host-exit. */
	bool ReadResponseLine(FString& OutLine, double DeadlineSeconds);
	/** Pop one complete line from PendingBytes (UTF-8 decoded), if there is one. */
	bool TakeLine(FString& OutLine);
	/** Host stdout bytes not yet consumed as a line (survive across reads). */
	TArray<uint8> PendingBytes;
	/** Log host stderr lines (Verbose); keeps diagnostics out of the reply stream. */
	void DrainStdErr();
	TArray<uint8> ErrBytes;
};
