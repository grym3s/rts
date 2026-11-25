#include "RtsSimHostBridge.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"
#include "HAL/PlatformFileManager.h"
#include "HAL/PlatformMisc.h"
#include "HAL/PlatformTime.h"
#include "Misc/Paths.h"
#include "Misc/Parse.h"

bool URtsSimHostBridge::StartAndInit(const FString& InitRequestJson, FString& OutError)
{
	PendingBytes.Reset(); // a fresh host starts a fresh stream
	// --- locate the host binary: RTS_HOST_BIN env override, else repo/packaged dist ---
	FString HostBin = FPlatformMisc::GetEnvironmentVariable(TEXT("RTS_HOST_BIN"));
	if (HostBin.IsEmpty())
	{
		const FString Rid =
		#if PLATFORM_LINUX
			TEXT("linux-x64");
		#else
			TEXT("win-x64");
		#endif
		HostBin = FPaths::ProjectDir() / TEXT("dist") / Rid / TEXT("sim-host");
		#if PLATFORM_WINDOWS
		HostBin += TEXT(".exe");
#endif
	}
	if (!FPaths::FileExists(HostBin))
	{
		OutError = FString::Printf(TEXT("sim host binary not found: %s (set RTS_HOST_BIN or run make host-publish)"), *HostBin);
		return false;
	}

	// Pipe roles per engine CreateProc semantics (Unix dup2 + Windows STARTUPINFOW agree):
	// PipeWriteChild -> child STDOUT, PipeReadChild -> child STDIN.
	// stdin pipe: parent writes ToChildW, child reads ToChildR.
	// stdout pipe: child writes FromChildW, parent reads FromChildR.
	void* ToChildW = nullptr;
	void* ToChildR = nullptr;
	void* FromChildR = nullptr;
	void* FromChildW = nullptr;
	// bWritePipeLocal matters on Windows only (Unix ignores it): it picks which end stays
	// non-inheritable. For stdin the parent keeps the WRITE end, so the child's read end
	// must stay inheritable — `false` here trips CreateProc's "PipeReadChild is not
	// inheritable" assertion on Win64.
	if (!FPlatformProcess::CreatePipe(ToChildR, ToChildW, /*bWritePipeLocal=*/true))
	{
		OutError = TEXT("CreatePipe(stdin) failed");
		return false;
	}
	if (!FPlatformProcess::CreatePipe(FromChildR, FromChildW, /*bWritePipeLocal=*/false))
	{
		FPlatformProcess::ClosePipe(ToChildR, ToChildW);
		OutError = TEXT("CreatePipe(stdout) failed");
		return false;
	}
	// stderr gets its own pipe. Without it Windows hands the child our stdout pipe for
	// stderr too, and a sim diagnostic (e.g. "UNREACHABLE u=12") lands in the middle of a
	// JSON reply line, which then fails to parse and kills the session.
	void* ErrChildR = nullptr;
	void* ErrChildW = nullptr;
	if (!FPlatformProcess::CreatePipe(ErrChildR, ErrChildW, /*bWritePipeLocal=*/false))
	{
		FPlatformProcess::ClosePipe(ToChildR, ToChildW);
		FPlatformProcess::ClosePipe(FromChildR, FromChildW);
		OutError = TEXT("CreatePipe(stderr) failed");
		return false;
	}

	uint32 Pid = 0;
	const FString HostDir = FPaths::GetPath(HostBin);
	// Parms must be non-null: Windows CreateProcInternal does check(Parms).
	ProcHandle = FPlatformProcess::CreateProc(*HostBin, TEXT(""), false, false, false, &Pid, 0,
		*HostDir, FromChildW /*-> child stdout*/, ToChildR /*-> child stdin*/, ErrChildW /*-> child stderr*/);
	if (!ProcHandle.IsValid())
	{
		FPlatformProcess::ClosePipe(ToChildR, ToChildW);
		FPlatformProcess::ClosePipe(FromChildR, FromChildW);
		FPlatformProcess::ClosePipe(ErrChildR, ErrChildW);
		ToChildW = FromChildR = nullptr;
		OutError = TEXT("CreateProc failed for sim host");
		return false;
	}
	// Parent closes the child-side handles it handed over (ExecProcess convention);
	// we keep only our own two: WritePipe(ToChildW) / ReadPipe(FromChildR).
	FPlatformProcess::ClosePipe(ToChildR, nullptr);
	ToChildR = nullptr;
	FPlatformProcess::ClosePipe(nullptr, FromChildW);
	FromChildW = nullptr;
	FPlatformProcess::ClosePipe(nullptr, ErrChildW);
	ErrChildW = nullptr;
	ErrParent = ErrChildR;
	WriteParent = ToChildW;
	ReadParent = FromChildR;
	bPipeOpen = true;

	// --- init request/response ---
	FRtsHostReply Reply;
	if (!Step(InitRequestJson, 10.0, Reply))
	{
		OutError = Reply.Error.IsEmpty() ? TEXT("init round-trip failed") : Reply.Error;
		return false;
	}
	if (!Reply.bOk)
	{
		OutError = FString::Printf(TEXT("host rejected init: %s"), *Reply.Error);
		return false;
	}
	return true;
}

bool URtsSimHostBridge::Step(const FString& RequestJson, double TimeoutSeconds, FRtsHostReply& OutReply)
{
	OutReply = FRtsHostReply();
	const double SentAt = FPlatformTime::Seconds();
	if (!bPipeOpen)
	{
		OutReply.Error = TEXT("host bridge not started");
		return false;
	}

	// Use the byte overload, not the FString overload: on Unix the FString overload
	// writes BytesAvailable+1 bytes (it appends '\n' itself) but returns
	// BytesWritten == BytesAvailable — always false, failing every successful write.
	// The byte overload writes raw bytes on both platforms, so we append the newline.
	const FTCHARToUTF8 Utf8(*(RequestJson + TEXT("\n")));
	if (!FPlatformProcess::WritePipe(WriteParent, reinterpret_cast<const uint8*>(Utf8.Get()), Utf8.Length(), nullptr))
	{
		OutReply.Error = TEXT("WritePipe failed (host stdin closed?)");
		bPipeOpen = false;
		return false;
	}

	FString ResponseLine;
	if (!ReadResponseLine(ResponseLine, TimeoutSeconds))
	{
		OutReply.Error = TEXT("no response line from host (timeout or host exit)");
		bPipeOpen = false;
		return false;
	}
	OutReply.RoundTripMs = (FPlatformTime::Seconds() - SentAt) * 1000.0;

	TSharedPtr<FJsonObject> Root;
	const TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(ResponseLine);
	if (!FJsonSerializer::Deserialize(Reader, Root) || !Root.IsValid())
	{
		OutReply.Error = FString::Printf(TEXT("host response is not valid JSON (%d chars): %s"),
			ResponseLine.Len(), *ResponseLine.Left(300));
		bPipeOpen = false;
		return false;
	}

	// Validate envelope: protocolVersion==1 and bool ok required (protocol v1 contract).
	int32 Version = 0;
	if (!Root->TryGetNumberField(TEXT("protocolVersion"), Version) || Version != 1)
	{
		OutReply.Error = TEXT("protocolVersion mismatch");
		bPipeOpen = false;
		return false;
	}
	bool bOk = false;
	if (!Root->TryGetBoolField(TEXT("ok"), bOk))
	{
		OutReply.Error = TEXT("response missing 'ok'");
		bPipeOpen = false;
		return false;
	}
	OutReply.bOk = bOk;
	if (!bOk)
	{
		Root->TryGetStringField(TEXT("error"), OutReply.Error);
		return true; // bounded protocol error; world did not advance, pipe still usable
	}

	double TickD = -1.0;
	if (Root->TryGetNumberField(TEXT("tick"), TickD))
		OutReply.Tick = static_cast<int64>(TickD);

	if (const TSharedPtr<FJsonObject>* StateObj = nullptr; Root->TryGetObjectField(TEXT("state"), StateObj))
	{
		(*StateObj)->TryGetStringField(TEXT("hash"), OutReply.Hash);
		const TSharedPtr<FJsonObject>* MatchObj = nullptr;
		if ((*StateObj)->TryGetObjectField(TEXT("match"), MatchObj))
		{
			(*MatchObj)->TryGetBoolField(TEXT("over"), OutReply.bMatchOver);
			(*MatchObj)->TryGetNumberField(TEXT("winner"), OutReply.MatchWinner);
		}
		const TSharedPtr<FJsonObject>* DebugObj = nullptr;
		if ((*StateObj)->TryGetObjectField(TEXT("debug"), DebugObj))
		{
			OutReply.bHasDebug = true;
			double Micros = 0.0;
			(*DebugObj)->TryGetNumberField(TEXT("stepMicros"), Micros);
			OutReply.StepMicros = static_cast<int64>(Micros);
			const TArray<TSharedPtr<FJsonValue>>* DUnits = nullptr;
			if ((*DebugObj)->TryGetArrayField(TEXT("units"), DUnits))
				for (const TSharedPtr<FJsonValue>& DV : *DUnits)
				{
					const TSharedPtr<FJsonObject> DO = DV->AsObject();
					if (!DO.IsValid())
						continue;
					FRtsDebugUnit D;
					DO->TryGetNumberField(TEXT("id"), D.Id);
					DO->TryGetStringField(TEXT("state"), D.State);
					DO->TryGetNumberField(TEXT("target"), D.Target);
					const TArray<TSharedPtr<FJsonValue>>* Pts = nullptr;
					if (DO->TryGetArrayField(TEXT("path"), Pts))
						for (const TSharedPtr<FJsonValue>& PV : *Pts)
						{
							const TArray<TSharedPtr<FJsonValue>>* XY = nullptr;
							if (PV->TryGetArray(XY) && XY->Num() == 2)
								D.Path.Add(FVector2D((*XY)[0]->AsNumber() / 4294967296.0, (*XY)[1]->AsNumber() / 4294967296.0));
						}
					OutReply.DebugUnits.Add(MoveTemp(D));
				}
		}
		const TArray<TSharedPtr<FJsonValue>>* UnitsArr = nullptr;
		if ((*StateObj)->TryGetArrayField(TEXT("units"), UnitsArr))
		{
			for (const TSharedPtr<FJsonValue>& UV : *UnitsArr)
			{
				const TSharedPtr<FJsonObject> UO = UV->AsObject();
				if (!UO.IsValid())
					continue;
				FRtsUnitSnapshot U;
				UO->TryGetNumberField(TEXT("id"), U.Id);
				UO->TryGetNumberField(TEXT("faction"), U.Faction);
				// host sends Q32.32 raw longs; decode exactly (fix64 raw / 2^32).
				double RawX = 0.0, RawY = 0.0;
				UO->TryGetNumberField(TEXT("x"), RawX);
				UO->TryGetNumberField(TEXT("y"), RawY);
				U.X = RawX / 4294967296.0;
				U.Y = RawY / 4294967296.0;
				double RawHp = 0.0, RawMaxHp = 0.0;
				UO->TryGetNumberField(TEXT("hp"), RawHp);
				UO->TryGetNumberField(TEXT("maxHp"), RawMaxHp);
				U.Hp = RawHp / 4294967296.0;
				U.MaxHp = RawMaxHp / 4294967296.0;
				OutReply.Units.Add(U);
			}
		}
	}

	const TArray<TSharedPtr<FJsonValue>>* EventsArr = nullptr;
	if (Root->TryGetArrayField(TEXT("events"), EventsArr))
	{
		for (const TSharedPtr<FJsonValue>& EV : *EventsArr)
		{
			const TSharedPtr<FJsonObject> EO = EV->AsObject();
			if (!EO.IsValid())
				continue;
			FRtsSimEvent E;
			EO->TryGetStringField(TEXT("kind"), E.Kind);
			EO->TryGetNumberField(TEXT("subject"), E.Subject);
			EO->TryGetNumberField(TEXT("target"), E.Target);
			EO->TryGetNumberField(TEXT("faction"), E.Faction);
			double RawAmount = 0.0;
			EO->TryGetNumberField(TEXT("amount"), RawAmount);
			E.Amount = RawAmount / 4294967296.0;
			OutReply.Events.Add(MoveTemp(E));
		}
	}
	return true;
}

bool URtsSimHostBridge::TakeLine(FString& OutLine)
{
	const int32 Nl = PendingBytes.Find(uint8('\n'));
	if (Nl == INDEX_NONE)
		return false;
	// Decode only complete lines: decoding each pipe chunk separately corrupts a UTF-8
	// character split across reads. Bytes after the newline stay for the next call.
	const FUTF8ToTCHAR Conv(reinterpret_cast<const ANSICHAR*>(PendingBytes.GetData()), Nl);
	OutLine = FString(Conv.Length(), Conv.Get());
	OutLine.TrimEndInline(); // Windows .NET writes "\r\n"
	PendingBytes.RemoveAt(0, Nl + 1, EAllowShrinking::No);
	return true;
}

bool URtsSimHostBridge::ReadResponseLine(FString& OutLine, double TimeoutSeconds)
{
	const double Deadline = FPlatformTime::Seconds() + TimeoutSeconds;
	TArray<uint8> Chunk;
	while (true)
	{
		DrainStdErr();
		if (TakeLine(OutLine))
			return true;
		Chunk.Reset();
		if (FPlatformProcess::ReadPipeToArray(ReadParent, Chunk) && Chunk.Num() > 0)
		{
			PendingBytes.Append(Chunk);
			continue; // a large reply arrives in several 4 KB reads: read on without waiting
		}
		if (!FPlatformProcess::IsProcRunning(ProcHandle))
		{
			// Host exited; drain once more then fail — never fabricate a snapshot.
			Chunk.Reset();
			if (FPlatformProcess::ReadPipeToArray(ReadParent, Chunk))
				PendingBytes.Append(Chunk);
			return TakeLine(OutLine);
		}
		if (FPlatformTime::Seconds() >= Deadline)
			return false;
		// Yield, don't Sleep(0.001): on Windows that lasts a whole ~15.6 ms timer tick,
		// which made every step cost ~50 ms on Tri-Point and froze the game.
		FPlatformProcess::YieldThread();
	}
}

void URtsSimHostBridge::Shutdown()
{
	if (bPipeOpen)
	{
		// Closing our write end gives the host stdin EOF; its loop exits cleanly.
		FPlatformProcess::ClosePipe(WriteParent, nullptr);
		WriteParent = nullptr;
		bPipeOpen = false;
	}
	if (ProcHandle.IsValid())
	{
		if (FPlatformProcess::IsProcRunning(ProcHandle))
		{
			// Give the EOF path a moment before forcing.
			const double Deadline = FPlatformTime::Seconds() + 2.0;
			while (FPlatformTime::Seconds() < Deadline && FPlatformProcess::IsProcRunning(ProcHandle))
				FPlatformProcess::Sleep(0.01);
			if (FPlatformProcess::IsProcRunning(ProcHandle))
				FPlatformProcess::TerminateProc(ProcHandle, true);
		}
		FPlatformProcess::WaitForProc(ProcHandle);
		FPlatformProcess::CloseProc(ProcHandle);
	}
	if (ReadParent)
	{
		FPlatformProcess::ClosePipe(ReadParent, nullptr);
		ReadParent = nullptr;
	}
	if (ErrParent)
	{
		DrainStdErr();
		FPlatformProcess::ClosePipe(ErrParent, nullptr);
		ErrParent = nullptr;
	}
}

void URtsSimHostBridge::BeginDestroy()
{
	Shutdown();
	Super::BeginDestroy();
}

void URtsSimHostBridge::DrainStdErr()
{
	// Read whatever the host wrote to stderr and log it line by line. Draining also keeps
	// the child from blocking on a full stderr pipe.
	if (!ErrParent)
		return;
	TArray<uint8> Chunk;
	while (FPlatformProcess::ReadPipeToArray(ErrParent, Chunk) && Chunk.Num() > 0)
	{
		ErrBytes.Append(Chunk);
		Chunk.Reset();
	}
	int32 Nl = INDEX_NONE;
	while ((Nl = ErrBytes.Find(uint8('\n'))) != INDEX_NONE)
	{
		const FUTF8ToTCHAR Conv(reinterpret_cast<const ANSICHAR*>(ErrBytes.GetData()), Nl);
		FString Line(Conv.Length(), Conv.Get());
		Line.TrimEndInline();
		if (!Line.IsEmpty())
			UE_LOG(LogTemp, Verbose, TEXT("RTS_HOST_STDERR: %s"), *Line);
		ErrBytes.RemoveAt(0, Nl + 1, EAllowShrinking::No);
	}
}
