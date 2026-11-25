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

	/** Close stdin (host exits on EOF), then terminate if it lingers. Always safe to call. */
	void Shutdown();

	virtual void BeginDestroy() override;

private:
	FProcHandle ProcHandle;
	void* WriteParent = nullptr;  // we write requests -> host stdin
	void* ReadParent = nullptr;   // host stdout -> we read responses
	void* ErrParent = nullptr;    // host stderr -> logged, never parsed
	bool bPipeOpen = false;

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
