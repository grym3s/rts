// T28: a `capture` body (what the game mode sends on RMB with engineers selected) takes the
// neutral oil derrick and an enemy MCV through the real host; rows and the faction's `trees`
// parse; neutrals draw grey. No game rules here.
#include "Misc/AutomationTest.h"
#include "RtsSimHostBridge.h"
#include "RtsSimUnitActor.h"

#if WITH_DEV_AUTOMATION_TESTS

namespace
{
	const TCHAR* CaptureInit = TEXT(
		"{\"protocolVersion\":1,\"kind\":\"init\",\"scenario\":"
		"{\"schemaVersion\":2,\"seed\":1,\"map\":{\"width\":24,\"height\":24,\"blocked\":[]},"
		"\"structures\":[{\"building\":\"neutral-oil-derrick\",\"at\":[12,12]}],"
		"\"units\":[{\"unit\":\"engineer\",\"at\":[10.5,12.5]},{\"unit\":\"engineer\",\"at\":[3.5,3.5]},"
		"{\"unit\":\"foundry-crawler\",\"at\":[4.5,3.5]}]}}");
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FRtsCaptureTest, "RtsBridge.Host.Capture",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FRtsCaptureTest::RunTest(const FString& Parameters)
{
	const FLinearColor Grey = ARtsSimUnitActor::FactionColor(-1);
	TestTrue(TEXT("neutral is grey, not anyone's colour"), Grey.R == Grey.G && Grey.G == Grey.B
		&& !Grey.Equals(ARtsSimUnitActor::FactionColor(0)));

	URtsSimHostBridge* Bridge = NewObject<URtsSimHostBridge>();
	FString Err;
	if (!TestTrue(TEXT("start + init"), Bridge->StartAndInit(CaptureInit, Err)))
	{
		AddError(Err);
		Bridge->Shutdown();
		return false;
	}
	const FRtsHostReply& Init = Bridge->GetInitReply();
	if (!TestEqual(TEXT("three units and the derrick"), Init.Units.Num(), 4))
	{
		Bridge->Shutdown();
		return false;
	}
	const FRtsUnitSnapshot E1 = Init.Units[0], E2 = Init.Units[1], Mcv = Init.Units[2], Derrick = Init.Units[3];
	TestTrue(TEXT("engineer can capture"), E1.bCanCapture);
	TestFalse(TEXT("an MCV can't"), Mcv.bCanCapture);
	TestEqual(TEXT("derrick is neutral"), Derrick.Faction, -1);
	TestEqual(TEXT("derrick building id"), Derrick.BuildingId, FString(TEXT("neutral-oil-derrick")));
	TestEqual(TEXT("own tree only at start"), Init.Factions[0].Trees.Num(), 0);

	const FString Step = FString::Printf(TEXT(
		"{\"protocolVersion\":1,\"kind\":\"step\",\"tick\":0,\"commands\":["
		"{\"tick\":0,\"faction\":0,\"type\":\"capture\",\"units\":[%d],\"target\":%d},"
		"{\"tick\":0,\"faction\":0,\"type\":\"capture\",\"units\":[%d],\"target\":%d}]}"),
		E1.Id, Derrick.Id, E2.Id, Mcv.Id);
	FRtsHostReply R;
	if (TestTrue(TEXT("step ok"), Bridge->Step(Step, 5.0, R) && R.bOk))
	{
		const FRtsUnitSnapshot* D = R.Units.FindByPredicate([&](const FRtsUnitSnapshot& U) { return U.Id == Derrick.Id; });
		const FRtsUnitSnapshot* M = R.Units.FindByPredicate([&](const FRtsUnitSnapshot& U) { return U.Id == Mcv.Id; });
		TestTrue(TEXT("derrick is ours"), D && D->Faction == 0);
		TestTrue(TEXT("MCV is ours"), M && M->Faction == 0);
		TestEqual(TEXT("two captured events"), R.Events.FilterByPredicate([](const FRtsSimEvent& E) { return E.Kind == TEXT("captured"); }).Num(), 2);
		if (TestEqual(TEXT("faction rows"), R.Factions.Num() >= 1, true))
		{
			TestTrue(TEXT("Hegemony tree opened"), R.Factions[0].HasTree(1));
			TestFalse(TEXT("not Ascendant's"), R.Factions[0].HasTree(2));
		}
	}
	Bridge->Shutdown();
	return true;
}

#endif
