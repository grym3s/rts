// T25: the host's terrain-aware scenario round trip: unit rows carry their domain, and the
// placement reasons for water buildings read as plain text. No game rules here.
#include "Misc/AutomationTest.h"
#include "RtsMinimapHud.h"
#include "RtsSimHostBridge.h"

#if WITH_DEV_AUTOMATION_TESTS

namespace
{
	// 16 x 4: land x < 8, sea x >= 8.
	const TCHAR* DomainInit = TEXT(
		"{\"protocolVersion\":1,\"kind\":\"init\",\"scenario\":"
		"{\"schemaVersion\":2,\"seed\":1,\"map\":{\"width\":16,\"height\":4,\"terrain\":"
		"[\"........~~~~~~~~\",\"........~~~~~~~~\",\"........~~~~~~~~\",\"........~~~~~~~~\"]},"
		"\"units\":[{\"unit\":\"rifleman\",\"at\":[2.5,1.5]},{\"unit\":\"cutter\",\"at\":[12.5,1.5]},"
		"{\"unit\":\"falcon\",\"at\":[4.5,2.5]}]}}");
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
	if (TestEqual(TEXT("three units"), Init.Units.Num(), 3))
	{
		TestEqual(TEXT("rifleman walks"), Init.Units[0].Domain, FString(TEXT("land")));
		TestEqual(TEXT("cutter sails"), Init.Units[1].Domain, FString(TEXT("naval")));
		TestEqual(TEXT("falcon flies"), Init.Units[2].Domain, FString(TEXT("air")));
	}
	const FRtsUnitType* Cutter = Init.UnitTypes.FindByPredicate([](const FRtsUnitType& U) { return U.Id == TEXT("cutter"); });
	if (TestNotNull(TEXT("catalog has the cutter"), Cutter))
		TestEqual(TEXT("catalog domain"), Cutter->Domain, FString(TEXT("naval")));
	Bridge->Shutdown();
	return true;
}

#endif // WITH_DEV_AUTOMATION_TESTS
