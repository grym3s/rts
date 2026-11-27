// T25: the host's terrain-aware scenario round trip: unit rows carry their domain, and the
// placement reasons for water buildings read as plain text. No game rules here.
#include "Misc/AutomationTest.h"
#include "RtsMinimapHud.h"
#include "RtsSimHostBridge.h"

#if WITH_DEV_AUTOMATION_TESTS

namespace
{
	// 16 x 4: land x < 8, sea x >= 8, a bridge along the bottom row's sea half (T26).
	const TCHAR* DomainInit = TEXT(
		"{\"protocolVersion\":1,\"kind\":\"init\",\"scenario\":"
		"{\"schemaVersion\":2,\"seed\":1,\"map\":{\"width\":16,\"height\":4,\"terrain\":"
		"[\"........~~~~~~~~\",\"........~~~~~~~~\",\"........~~~~~~~~\",\"........========\"]},"
		"\"units\":[{\"unit\":\"rifleman\",\"at\":[2.5,1.5]},{\"unit\":\"cutter\",\"at\":[12.5,1.5]},"
		"{\"unit\":\"falcon\",\"at\":[4.5,2.5]},{\"unit\":\"engineer\",\"at\":[1.5,0.5]}]}}");
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FRtsDomainTest, "RtsBridge.Host.Domain",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FRtsDomainTest::RunTest(const FString& Parameters)
{
	TestEqual(TEXT("water reason"), ARtsMinimapHud::PlaceReasonText(TEXT("water")), FString(TEXT("Must be built on water")));
	TestEqual(TEXT("shore reason"), ARtsMinimapHud::PlaceReasonText(TEXT("shore")), FString(TEXT("Must be next to the shore")));

	URtsSimHostBridge* Bridge = NewObject<URtsSimHostBridge>();
	FString Err;
	if (!TestTrue(TEXT("start + init"), Bridge->StartAndInit(DomainInit, Err)))
	{
		AddError(Err);
		Bridge->Shutdown();
		return false;
	}
	const FRtsHostReply& Init = Bridge->GetInitReply();
	// four units, then T28's neutral hut at the bridge's land end
	if (TestEqual(TEXT("four units and a bridge hut"), Init.Units.Num(), 5))
	{
		TestEqual(TEXT("hut names its bridge"), Init.Units[4].Bridge, 0);
		TestEqual(TEXT("hut is neutral"), Init.Units[4].Faction, -1);
		TestEqual(TEXT("units are not huts"), Init.Units[0].Bridge, -1);
		TestFalse(TEXT("rifleman is no engineer"), Init.Units[0].bEngineer);
		TestTrue(TEXT("engineer flagged"), Init.Units[3].bEngineer);
		TestEqual(TEXT("rifleman walks"), Init.Units[0].Domain, FString(TEXT("land")));
		TestEqual(TEXT("cutter sails"), Init.Units[1].Domain, FString(TEXT("naval")));
		TestEqual(TEXT("falcon flies"), Init.Units[2].Domain, FString(TEXT("air")));
		TestEqual(TEXT("T29 type id"), Init.Units[0].Type, FString(TEXT("rifleman")));
		TestEqual(TEXT("rifleman can't phase"), Init.Units[0].PhaseTicks, -1);
		TestFalse(TEXT("nobody marked at start"), Init.Units[0].bMarked);
	}
	const FRtsUnitType* Cutter = Init.UnitTypes.FindByPredicate([](const FRtsUnitType& U) { return U.Id == TEXT("cutter"); });
	if (TestNotNull(TEXT("catalog has the cutter"), Cutter))
		TestEqual(TEXT("catalog domain"), Cutter->Domain, FString(TEXT("naval")));
	if (TestEqual(TEXT("one bridge"), Init.Bridges.Num(), 1))
	{
		const FRtsBridgeSnapshot& B = Init.Bridges[0];
		TestEqual(TEXT("bridge cells"), B.Cells.Num(), 8);
		TestTrue(TEXT("covers its cell"), B.Covers(8, 3) && !B.Covers(8, 2));
		TestTrue(TEXT("standing, full hp"), !B.bBroken && B.Hp > 0.0 && !B.IsDamaged());
	}
	Bridge->Shutdown();
	return true;
}

#endif // WITH_DEV_AUTOMATION_TESTS
