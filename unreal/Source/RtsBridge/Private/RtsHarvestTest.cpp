// T38: vein blocks shrink with their pool (pure), and the game mode's `harvest` body sends a
// collector to the named vein through the real host. No game rules here.
#include "Misc/AutomationTest.h"
#include "RtsVeinActor.h"
#include "RtsSimHostBridge.h"

#if WITH_DEV_AUTOMATION_TESTS

namespace
{
	const TCHAR* HarvestInit = TEXT(
		"{\"protocolVersion\":1,\"kind\":\"init\",\"scenario\":"
		"{\"schemaVersion\":2,\"seed\":1,\"map\":{\"width\":32,\"height\":32,\"blocked\":[]},"
		"\"veins\":[{\"at\":[6.5,6.5],\"pool\":5000},{\"at\":[24.5,24.5],\"pool\":5000}],"
		"\"refineries\":[{\"faction\":0,\"at\":[4.5,4.5]}],"
		"\"units\":[{\"unit\":\"collector\",\"at\":[10.5,10.5]}]}}");

	FString HarvestStep(int32 Tick, const FString& Body = FString())
	{
		const FString Cmd = Body.IsEmpty() ? FString() : FString::Printf(TEXT("{\"tick\":%d,%s}"), Tick, *Body);
		return FString::Printf(TEXT("{\"protocolVersion\":1,\"kind\":\"step\",\"tick\":%d,\"commands\":[%s]}"), Tick, *Cmd);
	}
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FRtsHarvestTest, "RtsBridge.Host.Harvest",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FRtsHarvestTest::RunTest(const FString& Parameters)
{
	{
		const FVector Full = ARtsVeinActor::VeinScale(5000, 5000);
		const FVector Half = ARtsVeinActor::VeinScale(2500, 5000);
		const FVector Dregs = ARtsVeinActor::VeinScale(1, 5000);
		TestTrue(TEXT("full > half > dregs"), Full.X > Half.X && Half.X > Dregs.X && Full.Z > Half.Z && Half.Z > Dregs.Z);
		TestTrue(TEXT("dregs still visible"), Dregs.X > 0.0);
		TestTrue(TEXT("dry = gone"), ARtsVeinActor::VeinScale(0, 5000).IsZero());
		TestTrue(TEXT("full fits its cell"), Full.X <= 1.0);
	}

	URtsSimHostBridge* Bridge = NewObject<URtsSimHostBridge>();
	FString Err;
	if (!TestTrue(TEXT("start + init"), Bridge->StartAndInit(HarvestInit, Err)))
	{
		AddError(Err);
		Bridge->Shutdown();
		return false;
	}
	const FRtsHostReply& Init = Bridge->GetInitReply();
	TestEqual(TEXT("one collector"), Init.Units.Num(), 1);
	const int32 Cid = Init.Units.Num() == 1 ? Init.Units[0].Id : 0;
	if (Init.Units.Num() == 1)
	{
		TestTrue(TEXT("collector is a harvester"), Init.Units[0].bHarvester);
		TestEqual(TEXT("load capacity"), Init.Units[0].LoadCapacity, 700);
	}
	TestEqual(TEXT("two veins"), Init.Veins.Num(), 2);

	FRtsHostReply R;
	bool bOk = Bridge->Step(HarvestStep(0, FString::Printf(TEXT("\"faction\":0,\"type\":\"harvest\",\"units\":[%d],\"vein\":1"), Cid)), 5.0, R) && R.bOk;
	bool bLoaded = false;
	for (int32 T = 1; T < 600 && bOk; ++T)
	{
		bOk = Bridge->Step(HarvestStep(T), 5.0, R) && R.bOk;
		for (const FRtsUnitSnapshot& U : R.Units)
			bLoaded |= U.bHarvester && U.Load > 0;
	}
	TestTrue(TEXT("steps ok"), bOk);
	TestTrue(TEXT("collector filled"), bLoaded);
	if (TestEqual(TEXT("still two veins"), R.Veins.Num(), 2))
	{
		TestEqual(TEXT("near vein untouched"), R.Veins[0].Pool, 5000);
		TestTrue(TEXT("named vein mined"), R.Veins[1].Pool < 5000);
	}
	Bridge->Shutdown();
	return true;
}

#endif // WITH_DEV_AUTOMATION_TESTS
