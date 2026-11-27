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
	InitReply = Reply;
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
				// T30 economy fields; absent ones keep their defaults (the host omits nulls).
				UO->TryGetStringField(TEXT("buildingId"), U.BuildingId);
				UO->TryGetNumberField(TEXT("constructionRemaining"), U.ConstructionRemaining);
				UO->TryGetNumberField(TEXT("constructionTotal"), U.ConstructionTotal);
				const TArray<TSharedPtr<FJsonValue>>* Queue = nullptr;
				if (UO->TryGetArrayField(TEXT("productionQueue"), Queue))
					for (const TSharedPtr<FJsonValue>& QV : *Queue)
						U.ProductionQueue.Add(QV->AsString());
				UO->TryGetNumberField(TEXT("queueHeadRemaining"), U.QueueHeadRemaining);
				UO->TryGetNumberField(TEXT("queueHeadTotal"), U.QueueHeadTotal);
				UO->TryGetNumberField(TEXT("footprintW"), U.FootprintW);
				UO->TryGetNumberField(TEXT("footprintH"), U.FootprintH);
				UO->TryGetStringField(TEXT("deploysTo"), U.DeploysTo);
				UO->TryGetBoolField(TEXT("harvest"), U.bHarvester);
				UO->TryGetNumberField(TEXT("load"), U.Load);
				UO->TryGetBoolField(TEXT("carrying"), U.bCarrying);
				UO->TryGetNumberField(TEXT("loadCapacity"), U.LoadCapacity);
				UO->TryGetStringField(TEXT("domain"), U.Domain); // T25: absent = land
				UO->TryGetBoolField(TEXT("engineer"), U.bEngineer); // T26: absent = no
				UO->TryGetBoolField(TEXT("canCapture"), U.bCanCapture); // T28: absent = no
				UO->TryGetBoolField(TEXT("wrecked"), U.bWrecked);
				UO->TryGetBoolField(TEXT("mindControl"), U.bMindControl); // all optional
				UO->TryGetBoolField(TEXT("repairs"), U.bRepairs);
				UO->TryGetNumberField(TEXT("channelTarget"), U.ChannelTarget);
				UO->TryGetNumberField(TEXT("channel"), U.Channel);
				UO->TryGetNumberField(TEXT("channelNeed"), U.ChannelNeed);
				UO->TryGetNumberField(TEXT("controlledBy"), U.ControlledBy);
				{
					double Raw = 0.0;
					if (UO->TryGetNumberField(TEXT("sight"), Raw)) U.Sight = Raw / 4294967296.0;
					if (UO->TryGetNumberField(TEXT("radar"), Raw)) U.Radar = Raw / 4294967296.0;
					if (UO->TryGetNumberField(TEXT("strikeRadius"), Raw)) U.StrikeRadius = Raw / 4294967296.0;
					const TArray<TSharedPtr<FJsonValue>>* StrikeArr = nullptr;
					if (UO->TryGetArrayField(TEXT("strike"), StrikeArr) && StrikeArr->Num() == 2)
					{
						U.bStrike = true;
						U.StrikeX = (*StrikeArr)[0]->AsNumber() / 4294967296.0;
						U.StrikeY = (*StrikeArr)[1]->AsNumber() / 4294967296.0;
					}
				}
				UO->TryGetNumberField(TEXT("inside"), U.Inside);
				UO->TryGetBoolField(TEXT("stealth"), U.bStealth);
				UO->TryGetBoolField(TEXT("cloaked"), U.bCloaked);
				UO->TryGetNumberField(TEXT("detected"), U.Detected);
				UO->TryGetNumberField(TEXT("shield"), U.Shield);
				UO->TryGetNumberField(TEXT("sabotaged"), U.Sabotaged);
				UO->TryGetBoolField(TEXT("sabotages"), U.bSabotages);
				UO->TryGetBoolField(TEXT("hijacks"), U.bHijacks);
				UO->TryGetNumberField(TEXT("carrier"), U.Carrier);
				UO->TryGetStringField(TEXT("holds"), U.Holds);
				UO->TryGetStringField(TEXT("carries"), U.Carries);
				UO->TryGetNumberField(TEXT("capacity"), U.Capacity);
				UO->TryGetNumberField(TEXT("used"), U.Used);
				UO->TryGetBoolField(TEXT("teleport"), U.bTeleport);
				UO->TryGetNumberField(TEXT("charge"), U.Charge);
				UO->TryGetNumberField(TEXT("chargeNeed"), U.ChargeNeed);
				UO->TryGetNumberField(TEXT("strikeIn"), U.StrikeIn);
				UO->TryGetStringField(TEXT("control"), U.Control);
				UO->TryGetNumberField(TEXT("holdFaction"), U.HoldFaction);
				UO->TryGetNumberField(TEXT("hold"), U.Hold);
				UO->TryGetNumberField(TEXT("holdNeed"), U.HoldNeed);
				UO->TryGetNumberField(TEXT("bridge"), U.Bridge); // T28: bridge huts only
				UO->TryGetStringField(TEXT("type"), U.Type); // T29 owned verbs (all optional)
				UO->TryGetBoolField(TEXT("marked"), U.bMarked);
				UO->TryGetBoolField(TEXT("conscriptable"), U.bConscriptable);
				UO->TryGetNumberField(TEXT("phaseTicks"), U.PhaseTicks);
				UO->TryGetNumberField(TEXT("phaseCooldown"), U.PhaseCooldown);
				// T37: rally point [x, y] Q32.32 raw, present only on buildings that have one.
				const TArray<TSharedPtr<FJsonValue>>* RallyArr = nullptr;
				if (UO->TryGetArrayField(TEXT("rally"), RallyArr) && RallyArr->Num() == 2)
				{
					U.bHasRally = true;
					U.RallyX = (*RallyArr)[0]->AsNumber() / 4294967296.0;
					U.RallyY = (*RallyArr)[1]->AsNumber() / 4294967296.0;
				}
				OutReply.Units.Add(U);
			}
		}
		const TArray<TSharedPtr<FJsonValue>>* FactionsArr = nullptr;
		if ((*StateObj)->TryGetArrayField(TEXT("factions"), FactionsArr))
			for (const TSharedPtr<FJsonValue>& FV : *FactionsArr)
			{
				const TSharedPtr<FJsonObject> FO = FV->AsObject();
				if (!FO.IsValid())
					continue;
				FRtsFactionSnapshot F;
				FO->TryGetNumberField(TEXT("faction"), F.Faction);
				FO->TryGetNumberField(TEXT("credits"), F.Credits);
				FO->TryGetNumberField(TEXT("powerSupply"), F.PowerSupply);
				FO->TryGetNumberField(TEXT("powerDraw"), F.PowerDraw);
				const TArray<TSharedPtr<FJsonValue>>* TreesArr = nullptr; // T28: absent = own tree only
				if (FO->TryGetArrayField(TEXT("trees"), TreesArr))
					for (const TSharedPtr<FJsonValue>& T : *TreesArr)
						F.Trees.Add(static_cast<int32>(T->AsNumber()));
				OutReply.Factions.Add(F);
			}
		const TArray<TSharedPtr<FJsonValue>>* VeinsArr = nullptr;
		if ((*StateObj)->TryGetArrayField(TEXT("veins"), VeinsArr))
			for (const TSharedPtr<FJsonValue>& VV : *VeinsArr)
			{
				const TSharedPtr<FJsonObject> VO = VV->AsObject();
				if (!VO.IsValid())
					continue;
				FRtsVeinSnapshot V;
				VO->TryGetNumberField(TEXT("id"), V.Id);
				double RawVX = 0.0, RawVY = 0.0;
				VO->TryGetNumberField(TEXT("x"), RawVX);
				VO->TryGetNumberField(TEXT("y"), RawVY);
				V.X = RawVX / 4294967296.0;
				V.Y = RawVY / 4294967296.0;
				VO->TryGetNumberField(TEXT("pool"), V.Pool);
				OutReply.Veins.Add(V);
			}
		const TArray<TSharedPtr<FJsonValue>>* BridgesArr = nullptr;
		if ((*StateObj)->TryGetArrayField(TEXT("bridges"), BridgesArr))
			for (const TSharedPtr<FJsonValue>& BV : *BridgesArr)
			{
				const TSharedPtr<FJsonObject> BO = BV->AsObject();
				if (!BO.IsValid())
					continue;
				FRtsBridgeSnapshot B;
				BO->TryGetNumberField(TEXT("id"), B.Id);
				double RawHp = 0.0, RawMax = 0.0;
				BO->TryGetNumberField(TEXT("hp"), RawHp);
				BO->TryGetNumberField(TEXT("maxHp"), RawMax);
				B.Hp = RawHp / 4294967296.0;
				B.MaxHp = RawMax / 4294967296.0;
				BO->TryGetBoolField(TEXT("broken"), B.bBroken);
				BO->TryGetBoolField(TEXT("causeway"), B.bCauseway);
				const TArray<TSharedPtr<FJsonValue>>* CellsArr = nullptr;
				if (BO->TryGetArrayField(TEXT("cells"), CellsArr))
					for (const TSharedPtr<FJsonValue>& CV : *CellsArr)
					{
						const TArray<TSharedPtr<FJsonValue>>& XY = CV->AsArray();
						if (XY.Num() == 2)
							B.Cells.Add(FIntPoint(int32(XY[0]->AsNumber()), int32(XY[1]->AsNumber())));
					}
				OutReply.Bridges.Add(MoveTemp(B));
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

	// T36: catalogs (init) and a place-query answer (query).
	const TSharedPtr<FJsonObject>* CatalogObj = nullptr;
	if (Root->TryGetObjectField(TEXT("catalog"), CatalogObj))
	{
		const TArray<TSharedPtr<FJsonValue>>* Bs = nullptr;
		if ((*CatalogObj)->TryGetArrayField(TEXT("buildings"), Bs))
			for (const TSharedPtr<FJsonValue>& BV : *Bs)
			{
				const TSharedPtr<FJsonObject> BO = BV->AsObject();
				if (!BO.IsValid())
					continue;
				FRtsBuildingType B;
				BO->TryGetStringField(TEXT("id"), B.Id);
				BO->TryGetStringField(TEXT("name"), B.Name);
				BO->TryGetNumberField(TEXT("faction"), B.Faction);
				BO->TryGetStringField(TEXT("category"), B.Category);
				BO->TryGetNumberField(TEXT("cost"), B.Cost);
				BO->TryGetNumberField(TEXT("buildTicks"), B.BuildTicks);
				BO->TryGetStringField(TEXT("prereq"), B.Prereq);
				BO->TryGetNumberField(TEXT("power"), B.Power);
				BO->TryGetNumberField(TEXT("footprintW"), B.FootprintW);
				BO->TryGetNumberField(TEXT("footprintH"), B.FootprintH);
				BO->TryGetStringField(TEXT("description"), B.Description);
				OutReply.BuildingTypes.Add(MoveTemp(B));
			}
		const TArray<TSharedPtr<FJsonValue>>* Us = nullptr;
		if ((*CatalogObj)->TryGetArrayField(TEXT("units"), Us))
			for (const TSharedPtr<FJsonValue>& UV : *Us)
			{
				const TSharedPtr<FJsonObject> UO = UV->AsObject();
				if (!UO.IsValid())
					continue;
				FRtsUnitType U;
				UO->TryGetStringField(TEXT("id"), U.Id);
				UO->TryGetStringField(TEXT("name"), U.Name);
				UO->TryGetNumberField(TEXT("faction"), U.Faction);
				UO->TryGetNumberField(TEXT("cost"), U.Cost);
				UO->TryGetNumberField(TEXT("buildTicks"), U.BuildTicks);
				UO->TryGetStringField(TEXT("builtFrom"), U.BuiltFrom);
				UO->TryGetStringField(TEXT("deploysTo"), U.DeploysTo);
				UO->TryGetBoolField(TEXT("harvest"), U.bHarvest);
				UO->TryGetStringField(TEXT("domain"), U.Domain);
				UO->TryGetStringField(TEXT("description"), U.Description);
				OutReply.UnitTypes.Add(MoveTemp(U));
			}
	}
	const TSharedPtr<FJsonObject>* QueryObj = nullptr;
	if (Root->TryGetObjectField(TEXT("query"), QueryObj))
	{
		OutReply.bHasQuery = true;
		(*QueryObj)->TryGetBoolField(TEXT("allowed"), OutReply.bQueryAllowed);
		(*QueryObj)->TryGetStringField(TEXT("reason"), OutReply.QueryReason);
		int32 QX = 0, QY = 0, QW = 0, QH = 0;
		(*QueryObj)->TryGetNumberField(TEXT("x"), QX);
		(*QueryObj)->TryGetNumberField(TEXT("y"), QY);
		(*QueryObj)->TryGetNumberField(TEXT("w"), QW);
		(*QueryObj)->TryGetNumberField(TEXT("h"), QH);
		OutReply.QueryRect = FIntRect(QX, QY, QX + QW, QY + QH);
	}
	return true;
}

bool URtsSimHostBridge::QueryPlace(int32 Faction, const FString& BuildingId, int32 CellX, int32 CellY, FRtsHostReply& OutReply)
{
	const FString Req = FString::Printf(
		TEXT("{\"protocolVersion\":1,\"kind\":\"query\",\"query\":{\"type\":\"place\",\"faction\":%d,\"building\":\"%s\",\"at\":[%d,%d]}}"),
		Faction, *BuildingId, CellX, CellY);
	return Step(Req, 5.0, OutReply) && OutReply.bOk && OutReply.bHasQuery;
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
