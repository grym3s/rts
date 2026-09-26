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
	if (!FPlatformProcess::CreatePipe(ToChildR, ToChildW, /*bWritePipeLocal=*/false))
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

	uint32 Pid = 0;
	const FString HostDir = FPaths::GetPath(HostBin);
	ProcHandle = FPlatformProcess::CreateProc(*HostBin, nullptr, false, false, false, &Pid, 0,
		*HostDir, FromChildW /*-> child stdout*/, ToChildR /*-> child stdin*/);
	if (!ProcHandle.IsValid())
	{
		FPlatformProcess::ClosePipe(ToChildR, ToChildW);
		FPlatformProcess::ClosePipe(FromChildR, FromChildW);
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

	TSharedPtr<FJsonObject> Root;
	const TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(ResponseLine);
	if (!FJsonSerializer::Deserialize(Reader, Root) || !Root.IsValid())
	{
		OutReply.Error = TEXT("host response is not valid JSON");
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
				OutReply.Units.Add(U);
			}
		}
	}
	return true;
}

bool URtsSimHostBridge::ReadResponseLine(FString& OutLine, double TimeoutSeconds)
{
	const double Deadline = FPlatformTime::Seconds() + TimeoutSeconds;
	FString Buffer;
	while (FPlatformTime::Seconds() < Deadline)
	{
		const FString Chunk = FPlatformProcess::ReadPipe(ReadParent);
		if (!Chunk.IsEmpty())
		{
			Buffer += Chunk;
			int32 NewlineIdx = INDEX_NONE;
			if (Buffer.FindChar(TEXT('\n'), NewlineIdx))
			{
				OutLine = Buffer.Left(NewlineIdx);
				OutLine.TrimEndInline();
				return true;
			}
		}
		else if (!FPlatformProcess::IsProcRunning(ProcHandle))
		{
			// Host exited; drain one last chance then fail — never fabricate a snapshot.
			const FString Tail = FPlatformProcess::ReadPipe(ReadParent);
			Buffer += Tail;
			int32 NewlineIdx = INDEX_NONE;
			if (Buffer.FindChar(TEXT('\n'), NewlineIdx))
			{
				OutLine = Buffer.Left(NewlineIdx);
				OutLine.TrimEndInline();
				return true;
			}
			return false;
		}
		else
		{
			FPlatformProcess::Sleep(0.001);
		}
	}
	return false;
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
}

void URtsSimHostBridge::BeginDestroy()
{
	Shutdown();
	Super::BeginDestroy();
}
