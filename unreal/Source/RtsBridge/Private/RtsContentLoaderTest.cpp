// The match Unreal plays is loaded from content/ and the real host accepts it: the
// Tri-Point scenario becomes a valid init, the typed map matches its size, and every
// spawn is on a walkable tile. No game rules here.
#include "Misc/AutomationTest.h"
#include "RtsContentLoader.h"
#include "RtsSimHostBridge.h"
#include "RtsBridgeGameMode.h"

#if WITH_DEV_AUTOMATION_TESTS

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FRtsContentLoaderTest, "RtsBridge.Content.TriPoint",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FRtsContentLoaderTest::RunTest(const FString& Parameters)
{
	FRtsMatchSetup Setup;
	FString Err;
	if (!TestTrue(TEXT("scenario + map load"), RtsContent::LoadMatch(
			ARtsBridgeGameMode::BattleScenarioId, ARtsBridgeGameMode::MapId, Setup, Err)))
	{
		AddError(Err);
		return false;
	}
	TestTrue(TEXT("typed map found and matches the scenario size"), Setup.bTypedMap);
	TestEqual(TEXT("rows == height"), Setup.Rows.Num(), Setup.Height);
	TestFalse(TEXT("init has no scripted commands"), Setup.InitRequest.Contains(TEXT("\"commands\"")));
	TestFalse(TEXT("init is one line"), Setup.InitRequest.Contains(TEXT("\n")));

	URtsSimHostBridge* Bridge = NewObject<URtsSimHostBridge>();
	if (!Bridge->StartAndInit(Setup.InitRequest, Err))
	{
		AddError(FString::Printf(TEXT("host rejected the init: %s"), *Err));
		Bridge->Shutdown();
		return false;
	}
	FRtsHostReply R;
	const bool bStep = Bridge->Step(TEXT("{\"protocolVersion\":1,\"kind\":\"step\",\"tick\":0,\"commands\":[]}"), 10.0, R) && R.bOk;
	TestTrue(TEXT("first step ok"), bStep);
	int32 PerFaction[2] = { 0, 0 };
	int32 Derricks = 0;
	for (const FRtsUnitSnapshot& U : R.Units)
	{
		if (U.Faction >= 0 && U.Faction < 2)
			++PerFaction[U.Faction];
		if (U.Faction < 0) // T28: the neutral oil derrick stands offshore on the central rig
		{
			Derricks += U.BuildingId == TEXT("neutral-oil-derrick") ? 1 : 0;
			continue;
		}
		const int32 CX = FMath::FloorToInt(U.X), CY = FMath::FloorToInt(U.Y);
		const bool bOnLand = CY >= 0 && CY < Setup.Rows.Num() && CX >= 0 && CX < Setup.Rows[CY].Len()
			&& RtsContent::IsWalkableTile(Setup.Rows[CY][CX]);
		if (!bOnLand)
			AddError(FString::Printf(TEXT("unit %d at (%.1f, %.1f) is not on a walkable tile"), U.Id, U.X, U.Y));
	}
	TestEqual(TEXT("30 Coalition (faction 0)"), PerFaction[0], 30);
	TestEqual(TEXT("30 Hegemony (faction 1)"), PerFaction[1], 30);
	TestEqual(TEXT("one neutral oil derrick on the rig"), Derricks, 1);
	Bridge->Shutdown();
	return true;
}

// Regression for the Tri-Point freeze: replies span several pipe reads, and a 1 ms
// Sleep on Windows lasts ~15.6 ms, so steps cost ~50 ms (> the 50 ms tick) and the frame
// loop spiralled. Drive the full 30v30 fight (both armies attack-move to their shared
// Seam, as the CI scenario does) and require steps far below the tick budget.
IMPLEMENT_SIMPLE_AUTOMATION_TEST(FRtsTriPointStepSpeedTest, "RtsBridge.Content.TriPointStepSpeed",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FRtsTriPointStepSpeedTest::RunTest(const FString& Parameters)
{
	FRtsMatchSetup Setup;
	FString Err;
	if (!RtsContent::LoadMatch(ARtsBridgeGameMode::BattleScenarioId, ARtsBridgeGameMode::MapId, Setup, Err))
	{
		AddError(Err);
		return false;
	}
	URtsSimHostBridge* Bridge = NewObject<URtsSimHostBridge>();
	if (!Bridge->StartAndInit(Setup.InitRequest, Err))
	{
		AddError(Err);
		Bridge->Shutdown();
		return false;
	}
	constexpr int32 Steps = 1200; // a minute of game time: march + the whole Seam fight
	double Worst = 0.0, Total = 0.0;
	FRtsHostReply R;
	for (int32 T = 0; T < Steps; ++T)
	{
		const FString Cmds = T == 1
			? TEXT("{\"tick\":1,\"faction\":0,\"type\":\"attack-move\",\"target\":[65,101]},")
			  TEXT("{\"tick\":1,\"faction\":1,\"type\":\"attack-move\",\"target\":[65,101]}")
			: TEXT("");
		const FString Req = FString::Printf(
			TEXT("{\"protocolVersion\":1,\"kind\":\"step\",\"tick\":%d,\"commands\":[%s]}"), T, *Cmds);
		const double T0 = FPlatformTime::Seconds();
		if (!Bridge->Step(Req, 10.0, R) || !R.bOk)
		{
			AddError(FString::Printf(TEXT("step %d failed: %s"), T, *R.Error));
			Bridge->Shutdown();
			return false;
		}
		const double Dt = FPlatformTime::Seconds() - T0;
		Total += Dt;
		Worst = FMath::Max(Worst, Dt);
	}
	Bridge->Shutdown();
	const double MeanMs = Total / Steps * 1000.0;
	AddInfo(FString::Printf(TEXT("Tri-Point step: mean %.2f ms, worst %.2f ms, %d units left"), MeanMs, Worst * 1000.0, R.Units.Num()));
	TestTrue(TEXT("mean step well under the 50 ms tick (< 10 ms)"), MeanMs < 10.0);
	TestTrue(TEXT("the fight happened (units died)"), R.Units.Num() < 60);
	return true;
}

#endif
