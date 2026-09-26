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

	UPROPERTY(BlueprintReadOnly)
	TArray<FRtsUnitSnapshot> Units;

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
	bool bPipeOpen = false;

	/** Read until one complete '\n'-terminated line or deadline; false on timeout/host-exit. */
	bool ReadResponseLine(FString& OutLine, double DeadlineSeconds);
};
