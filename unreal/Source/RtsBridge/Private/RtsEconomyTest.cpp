// T35: the bridge carries the T30 economy (credits, power, veins, building/queue/harvest rows)
// exactly as the real host sends it, and the top bar formats it. No game rules here.
#include "Misc/AutomationTest.h"
#include "RtsMinimapHud.h"
#include "RtsSimHostBridge.h"

#if WITH_DEV_AUTOMATION_TESTS

namespace
{
	// One MCV (id 0) and a vein; nothing else.
	const TCHAR* EconomyInit = TEXT(
		"{\"protocolVersion\":1,\"kind\":\"init\",\"scenario\":"
		"{\"schemaVersion\":2,\"seed\":1,\"map\":{\"width\":24,\"height\":24,\"blocked\":[]},"
		"\"veins\":[{\"at\":[16.5,10.5],\"pool\":5000}],"
		"\"units\":[{\"unit\":\"mobile-command\",\"at\":[8.5,8.5]}]}}");

	FString StepJson(int32 Tick, const FString& Commands)
	{
		return FString::Printf(TEXT("{\"protocolVersion\":1,\"kind\":\"step\",\"tick\":%d,\"commands\":[%s]}"), Tick, *Commands);
	}
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FRtsEconomyBridgeTest, "RtsBridge.Host.Economy",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FRtsEconomyBridgeTest::RunTest(const FString& Parameters)
{
	// The bar text first (pure).
	{
		const ARtsMinimapHud::FEconomyBarText T = ARtsMinimapHud::EconomyBarText(12500, 100, 40);
		TestEqual(TEXT("credits grouped"), T.Credits, FString(TEXT("Credits  12,500")));
		TestEqual(TEXT("power used / produced"), T.Power, FString(TEXT("Power  40 / 100")));
		TestFalse(TEXT("enough power"), T.bLowPower);
		const ARtsMinimapHud::FEconomyBarText L = ARtsMinimapHud::EconomyBarText(950, 0, 20);
		TestEqual(TEXT("small credits"), L.Credits, FString(TEXT("Credits  950")));
		TestTrue(TEXT("draw > supply = low power"), L.bLowPower);
		TestTrue(TEXT("low power is spelled out"), L.Power.Contains(TEXT("LOW POWER")));
	}

	URtsSimHostBridge* Bridge = NewObject<URtsSimHostBridge>();
	FString Err;
	if (!TestTrue(TEXT("start + init"), Bridge->StartAndInit(EconomyInit, Err)))
	{
		AddError(Err);
		Bridge->Shutdown();
		return false;
	}

	FRtsHostReply R;
	// Tick 0: deploy the MCV.
	if (!Bridge->Step(StepJson(0, TEXT("{\"tick\":0,\"faction\":0,\"type\":\"deploy\",\"unit\":0}")), 5.0, R) || !R.bOk)
	{
		AddError(FString::Printf(TEXT("deploy step: %s"), *R.Error));
		Bridge->Shutdown();
		return false;
	}
	TestEqual(TEXT("two factions"), R.Factions.Num(), 2);
	if (R.Factions.Num() == 2)
	{
		TestEqual(TEXT("starting credits"), R.Factions[0].Credits, 5000);
		TestEqual(TEXT("no power yet"), R.Factions[0].PowerSupply, 0);
	}
	TestEqual(TEXT("one vein"), R.Veins.Num(), 1);
	if (R.Veins.Num() == 1)
	{
		TestEqual(TEXT("vein pool"), R.Veins[0].Pool, 5000);
		TestEqual(TEXT("vein x decoded"), R.Veins[0].X, 16.5);
	}
	const FRtsUnitSnapshot* Yard = R.Units.FindByPredicate([](const FRtsUnitSnapshot& U) { return U.IsBuilding(); });
	if (TestNotNull(TEXT("the MCV became a yard"), Yard))
	{
		TestEqual(TEXT("yard id"), Yard->BuildingId, FString(TEXT("coalition-construction-yard")));
		TestEqual(TEXT("3x3 footprint"), Yard->FootprintW * 10 + Yard->FootprintH, 33);
		TestTrue(TEXT("construction total > 0"), Yard->ConstructionTotal > 0);
		TestTrue(TEXT("construction under way"), Yard->ConstructionRemaining > 0 && Yard->ConstructionRemaining <= Yard->ConstructionTotal);
	}

	// Build out the yard, place a refinery (no reactor: low power once it stands), wait for its harvester.
	int32 T = 1;
	bool bOk = true;
	for (; T <= 205 && bOk; ++T)
		bOk = Bridge->Step(StepJson(T, TEXT("")), 5.0, R) && R.bOk;
	bOk = bOk && Bridge->Step(StepJson(T, FString::Printf(
		TEXT("{\"tick\":%d,\"faction\":0,\"type\":\"place\",\"building\":\"coalition-refinery\",\"at\":[12,9]}"), T)), 5.0, R) && R.bOk;
	++T;
	TestTrue(TEXT("refinery cost spent"), R.Factions.Num() == 2 && R.Factions[0].Credits == 5000 - 1500);
	const FRtsUnitSnapshot* Harvester = nullptr;
	for (int32 i = 0; i < 700 && bOk && !Harvester; ++i, ++T)
	{
		bOk = Bridge->Step(StepJson(T, TEXT("")), 5.0, R) && R.bOk;
		Harvester = R.Units.FindByPredicate([](const FRtsUnitSnapshot& U) { return U.bHarvester; });
	}
	TestTrue(TEXT("steps ok"), bOk);
	TestNotNull(TEXT("the finished refinery shipped a harvester"), Harvester);
	// Power is recomputed at the start of the next tick (step 1), so read it one step later.
	bOk = bOk && Bridge->Step(StepJson(T, TEXT("")), 5.0, R) && R.bOk;
	++T;
	if (R.Factions.Num() == 2)
	{
		TestEqual(TEXT("refinery draws power"), R.Factions[0].PowerDraw, 20);
	}
	// Let it fill: load rises and it turns for home.
	bool bLoaded = false, bCarried = false;
	for (int32 i = 0; i < 400 && bOk && !bCarried; ++i, ++T)
	{
		bOk = Bridge->Step(StepJson(T, TEXT("")), 5.0, R) && R.bOk;
		for (const FRtsUnitSnapshot& U : R.Units)
			if (U.bHarvester)
			{
				bLoaded |= U.Load > 0;
				bCarried |= U.bCarrying;
			}
	}
	TestTrue(TEXT("harvester load shows"), bLoaded);
	TestTrue(TEXT("full harvester shows carrying"), bCarried);

	Bridge->Shutdown();
	return true;
}

#endif // WITH_DEV_AUTOMATION_TESTS
