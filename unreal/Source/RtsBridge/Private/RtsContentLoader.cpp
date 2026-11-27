#include "RtsContentLoader.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "HAL/PlatformMisc.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"
#include "Policies/CondensedJsonPrintPolicy.h"

FString RtsContent::ContentRoot()
{
	const FString Env = FPlatformMisc::GetEnvironmentVariable(TEXT("RTS_CONTENT_ROOT"));
	if (!Env.IsEmpty())
		return Env;
	// T24: a packaged build carries the data at <project>/rts/content (unreal/package.sh stages it)
	const FString Packaged = FPaths::ConvertRelativePathToFull(FPaths::Combine(FPaths::ProjectDir(), TEXT("rts")));
	if (FPaths::DirectoryExists(FPaths::Combine(Packaged, TEXT("content"))))
		return Packaged;
	return FPaths::ConvertRelativePathToFull(FPaths::Combine(FPaths::ProjectDir(), TEXT("..")));
}

static TSharedPtr<FJsonObject> ReadJson(const FString& Path, FString& OutError)
{
	FString Text;
	if (!FFileHelper::LoadFileToString(Text, *Path))
	{
		OutError = FString::Printf(TEXT("cannot read %s"), *Path);
		return nullptr;
	}
	TSharedPtr<FJsonObject> Obj;
	if (!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Text), Obj) || !Obj.IsValid())
	{
		OutError = FString::Printf(TEXT("malformed JSON in %s"), *Path);
		return nullptr;
	}
	return Obj;
}

FString RtsContent::WithComputerPlayers(const FString& InitRequest, int32 Human)
{
	TSharedPtr<FJsonObject> Init;
	const TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(InitRequest);
	const TSharedPtr<FJsonObject>* Scenario = nullptr;
	if (!FJsonSerializer::Deserialize(Reader, Init) || !Init || !Init->TryGetObjectField(TEXT("scenario"), Scenario))
		return InitRequest;
	TArray<TSharedPtr<FJsonValue>> Ai;
	for (int32 F = 0; F < 3; ++F)
		if (F != Human)
			Ai.Add(MakeShared<FJsonValueNumber>(F));
	(*Scenario)->SetArrayField(TEXT("ai"), Ai);
	FString Out;
	auto Writer = TJsonWriterFactory<TCHAR, TCondensedJsonPrintPolicy<TCHAR>>::Create(&Out);
	return FJsonSerializer::Serialize(Init.ToSharedRef(), Writer) ? Out : InitRequest;
}

bool RtsContent::LoadMatch(const FString& ScenarioId, const FString& MapId, FRtsMatchSetup& Out, FString& OutError)
{
	const FString Root = ContentRoot();
	const FString ScenarioPath = FPaths::Combine(Root, TEXT("content/scenarios"), ScenarioId + TEXT(".json"));
	TSharedPtr<FJsonObject> Scenario = ReadJson(ScenarioPath, OutError);
	if (!Scenario)
		return false;

	const TSharedPtr<FJsonObject>* MapObj = nullptr;
	if (!Scenario->TryGetObjectField(TEXT("map"), MapObj)
		|| !(*MapObj)->TryGetNumberField(TEXT("width"), Out.Width)
		|| !(*MapObj)->TryGetNumberField(TEXT("height"), Out.Height)
		|| Out.Width <= 0 || Out.Height <= 0)
	{
		OutError = FString::Printf(TEXT("%s: map.width/height missing"), *ScenarioPath);
		return false;
	}

	// Same contract as the host and tools/sim-host.tests: init = scenario minus the
	// replay-only fields. The player issues commands live instead of the scripted ones.
	Scenario->RemoveField(TEXT("commands"));
	Scenario->RemoveField(TEXT("ticks"));
	Scenario->RemoveField(TEXT("assert"));
	TSharedRef<FJsonObject> Init = MakeShared<FJsonObject>();
	Init->SetNumberField(TEXT("protocolVersion"), 1);
	Init->SetStringField(TEXT("kind"), TEXT("init"));
	Init->SetObjectField(TEXT("scenario"), Scenario);
	Out.InitRequest.Reset();
	auto Writer = TJsonWriterFactory<TCHAR, TCondensedJsonPrintPolicy<TCHAR>>::Create(&Out.InitRequest);
	if (!FJsonSerializer::Serialize(Init, Writer))
	{
		OutError = TEXT("could not serialise init request");
		return false;
	}

	// Tiles: the scenario's own `terrain` rows (T25: what the sim walks on), else the typed map
	// if it exists and matches, else blocked rects drawn as cliff.
	Out.bTypedMap = false;
	Out.Rows.Reset();
	const TArray<TSharedPtr<FJsonValue>>* SimRows = nullptr;
	if ((*MapObj)->TryGetArrayField(TEXT("terrain"), SimRows) && SimRows->Num() == Out.Height)
	{
		for (const TSharedPtr<FJsonValue>& V : *SimRows)
			Out.Rows.Add(V->AsString());
		Out.bTypedMap = Out.Rows.FindByPredicate([&Out](const FString& R) { return R.Len() != Out.Width; }) == nullptr;
		if (!Out.bTypedMap)
			Out.Rows.Reset();
	}
	FString MapErr;
	const FString MapPath = FPaths::Combine(Root, TEXT("content/maps"), MapId + TEXT(".json"));
	if (TSharedPtr<FJsonObject> Map = (Out.bTypedMap || MapId.IsEmpty()) ? nullptr : ReadJson(MapPath, MapErr))
	{
		const TArray<TSharedPtr<FJsonValue>>* RowVals = nullptr;
		int32 MW = 0, MH = 0;
		if (Map->TryGetNumberField(TEXT("width"), MW) && Map->TryGetNumberField(TEXT("height"), MH)
			&& MW == Out.Width && MH == Out.Height && Map->TryGetArrayField(TEXT("rows"), RowVals)
			&& RowVals->Num() == MH)
		{
			for (const TSharedPtr<FJsonValue>& V : *RowVals)
				Out.Rows.Add(V->AsString());
			Out.bTypedMap = Out.Rows.FindByPredicate([MW](const FString& R) { return R.Len() != MW; }) == nullptr;
			if (!Out.bTypedMap)
				Out.Rows.Reset();
		}
	}
	if (!Out.bTypedMap)
	{
		const FString Open = FString::ChrN(Out.Width, TEXT('.'));
		Out.Rows.Init(Open, Out.Height);
		const TArray<TSharedPtr<FJsonValue>>* Blocked = nullptr;
		if ((*MapObj)->TryGetArrayField(TEXT("blocked"), Blocked))
			for (const TSharedPtr<FJsonValue>& R : *Blocked)
			{
				const TArray<TSharedPtr<FJsonValue>>& A = R->AsArray();
				if (A.Num() != 4)
					continue;
				const int32 X0 = FMath::Clamp(int32(A[0]->AsNumber()), 0, Out.Width - 1);
				const int32 Y0 = FMath::Clamp(int32(A[1]->AsNumber()), 0, Out.Height - 1);
				const int32 X1 = FMath::Clamp(int32(A[2]->AsNumber()), 0, Out.Width - 1);
				const int32 Y1 = FMath::Clamp(int32(A[3]->AsNumber()), 0, Out.Height - 1);
				for (int32 Y = Y0; Y <= Y1; ++Y)
					for (int32 X = X0; X <= X1; ++X)
						Out.Rows[Y][X] = TEXT('#');
			}
	}
	return true;
}
