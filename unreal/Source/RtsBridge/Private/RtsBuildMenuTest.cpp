// T36: the build sidebar's layout/hit test (pure), and the bridge's catalog + read-only place
// query against the real host. No game rules here: every verdict is the sim's.
#include "Misc/AutomationTest.h"
#include "RtsMinimapHud.h"
#include "RtsSimHostBridge.h"

#if WITH_DEV_AUTOMATION_TESTS

namespace
{
	const TCHAR* BuildMenuInit = TEXT(
		"{\"protocolVersion\":1,\"kind\":\"init\",\"scenario\":"
		"{\"schemaVersion\":2,\"seed\":1,\"map\":{\"width\":40,\"height\":40,\"blocked\":[]},"
		"\"units\":[{\"unit\":\"mobile-command\",\"at\":[8.5,8.5]}]}}");

	FString BuildMenuStep(int32 Tick, const FString& Commands)
	{
		return FString::Printf(TEXT("{\"protocolVersion\":1,\"kind\":\"step\",\"tick\":%d,\"commands\":[%s]}"), Tick, *Commands);
	}
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FRtsBuildMenuTest, "RtsBridge.Host.BuildMenu",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FRtsBuildMenuTest::RunTest(const FString& Parameters)
{
	// Layout: right edge, entries stacked under the Deploy button; hits map back to them.
	{
		const FVector2D Vp(1920, 1080);
		const ARtsMinimapHud::FBuildMenuLayout L = ARtsMinimapHud::BuildMenuLayout(Vp, 3, true);
		TestEqual(TEXT("three entries"), L.Entries.Num(), 3);
		TestTrue(TEXT("deploy shown"), L.Deploy.bIsValid);
		TestTrue(TEXT("panel on the right"), L.Panel.Max.X <= Vp.X && L.Panel.Min.X > Vp.X * 0.5);
		TestTrue(TEXT("entries below deploy"), L.Entries[0].Min.Y >= L.Deploy.Max.Y);
		TestTrue(TEXT("entries stack downwards"), L.Entries[1].Min.Y >= L.Entries[0].Max.Y);
		TestEqual(TEXT("hit entry 1"), ARtsMinimapHud::BuildMenuHit(Vp, 3, true, L.Entries[1].GetCenter()), 1);
		TestEqual(TEXT("hit deploy"), ARtsMinimapHud::BuildMenuHit(Vp, 3, true, L.Deploy.GetCenter()), ARtsMinimapHud::BuildMenuDeploy);
		TestEqual(TEXT("gap swallows"), ARtsMinimapHud::BuildMenuHit(Vp, 3, true,
			FVector2D(L.Panel.Min.X + 2.0, L.Entries[0].GetCenter().Y)), ARtsMinimapHud::BuildMenuPanel);
		TestEqual(TEXT("map is not the menu"), ARtsMinimapHud::BuildMenuHit(Vp, 3, true, FVector2D(400, 500)), ARtsMinimapHud::BuildMenuNone);
		const ARtsMinimapHud::FBuildMenuLayout NoDeploy = ARtsMinimapHud::BuildMenuLayout(Vp, 2, false);
		TestFalse(TEXT("no deploy button"), NoDeploy.Deploy.bIsValid);
		TestEqual(TEXT("reason text"), ARtsMinimapHud::PlaceReasonText(TEXT("radius")), FString(TEXT("Too far from your base")));
		TestTrue(TEXT("ok has no text"), ARtsMinimapHud::PlaceReasonText(TEXT("ok")).IsEmpty());
	}

	URtsSimHostBridge* Bridge = NewObject<URtsSimHostBridge>();
	FString Err;
	if (!TestTrue(TEXT("start + init"), Bridge->StartAndInit(BuildMenuInit, Err)))
	{
		AddError(Err);
		Bridge->Shutdown();
		return false;
	}
	const FRtsHostReply& Init = Bridge->GetInitReply();
	const FRtsBuildingType* Refinery = Init.BuildingTypes.FindByPredicate([](const FRtsBuildingType& B) { return B.Id == TEXT("coalition-refinery"); });
	if (TestNotNull(TEXT("catalog has the refinery"), Refinery))
	{
		TestEqual(TEXT("refinery cost"), Refinery->Cost, 1500);
		TestEqual(TEXT("refinery faction"), Refinery->Faction, 0);
		TestFalse(TEXT("refinery has a name"), Refinery->Name.IsEmpty());
	}
	TestTrue(TEXT("catalog has units"), Init.UnitTypes.ContainsByPredicate([](const FRtsUnitType& U) { return U.Id == TEXT("rifleman"); }));

	FRtsHostReply Q;
	TestTrue(TEXT("query before a base"), Bridge->QueryPlace(0, TEXT("coalition-reactor"), 12, 8, Q));
	TestTrue(TEXT("reply has a query"), Q.bHasQuery);
	TestEqual(TEXT("no yard = prereq"), Q.QueryReason, FString(TEXT("prereq")));

	FRtsHostReply R;
	bool bOk = Bridge->Step(BuildMenuStep(0, TEXT("{\"tick\":0,\"faction\":0,\"type\":\"deploy\",\"unit\":0}")), 5.0, R) && R.bOk;
	for (int32 T = 1; T <= 205 && bOk; ++T)
		bOk = Bridge->Step(BuildMenuStep(T, TEXT("")), 5.0, R) && R.bOk;
	TestTrue(TEXT("steps ok"), bOk);

	TestTrue(TEXT("query near the yard"), Bridge->QueryPlace(0, TEXT("coalition-reactor"), 12, 8, Q));
	TestTrue(TEXT("allowed beside the yard"), Q.bQueryAllowed);
	TestTrue(TEXT("footprint rect has area"), Q.QueryRect.Area() > 0);
	TestTrue(TEXT("query on the yard"), Bridge->QueryPlace(0, TEXT("coalition-reactor"), 8, 8, Q));
	TestEqual(TEXT("on the yard = blocked"), Q.QueryReason, FString(TEXT("blocked")));
	TestTrue(TEXT("query far away"), Bridge->QueryPlace(0, TEXT("coalition-reactor"), 35, 35, Q));
	TestEqual(TEXT("far = radius"), Q.QueryReason, FString(TEXT("radius")));

	// The query never advanced the world: tick 206 is still the next step.
	TestTrue(TEXT("next step still accepted"), Bridge->Step(BuildMenuStep(206, TEXT("")), 5.0, R) && R.bOk);

	Bridge->Shutdown();
	return true;
}

#endif // WITH_DEV_AUTOMATION_TESTS
