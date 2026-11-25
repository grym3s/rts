// T37: the production panel's layout/hit test (pure), and train / cancel / rally through the
// real host with the exact bodies the game mode sends. No game rules here.
#include "Misc/AutomationTest.h"
#include "RtsMinimapHud.h"
#include "RtsSimHostBridge.h"

#if WITH_DEV_AUTOMATION_TESTS

namespace
{
	const TCHAR* ProductionInit = TEXT(
		"{\"protocolVersion\":1,\"kind\":\"init\",\"scenario\":"
		"{\"schemaVersion\":2,\"seed\":1,\"map\":{\"width\":32,\"height\":32,\"blocked\":[]},"
		"\"units\":[{\"unit\":\"mobile-command\",\"at\":[8.5,8.5]}]}}");

	FString ProdStep(int32 Tick, const FString& Body = FString())
	{
		const FString Cmd = Body.IsEmpty() ? FString() : FString::Printf(TEXT("{\"tick\":%d,%s}"), Tick, *Body);
		return FString::Printf(TEXT("{\"protocolVersion\":1,\"kind\":\"step\",\"tick\":%d,\"commands\":[%s]}"), Tick, *Cmd);
	}
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FRtsProductionTest, "RtsBridge.Host.Production",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FRtsProductionTest::RunTest(const FString& Parameters)
{
	{
		const FVector2D Vp(1600, 900);
		const ARtsMinimapHud::FProductionLayout L = ARtsMinimapHud::ProductionLayout(Vp, 6);
		TestEqual(TEXT("six unit buttons"), L.Units.Num(), 6);
		TestEqual(TEXT("five queue slots"), L.Queue.Num(), ARtsMinimapHud::ProductionQueueSlots);
		TestTrue(TEXT("bottom centre"), FMath::IsNearlyEqual(L.Panel.GetCenter().X, Vp.X * 0.5, 1.0) && L.Panel.Max.Y <= Vp.Y);
		TestTrue(TEXT("fifth wraps to a second row"), L.Units[4].Min.Y > L.Units[0].Max.Y);
		TestTrue(TEXT("queue under the buttons"), L.Queue[0].Min.Y >= L.Units[4].Max.Y);
		int32 Index = INDEX_NONE;
		TestTrue(TEXT("hit unit 5"), ARtsMinimapHud::ProductionHit(Vp, 6, L.Units[5].GetCenter(), Index) == ARtsMinimapHud::EProductionHit::Unit && Index == 5);
		TestTrue(TEXT("hit queue 2"), ARtsMinimapHud::ProductionHit(Vp, 6, L.Queue[2].GetCenter(), Index) == ARtsMinimapHud::EProductionHit::Queue && Index == 2);
		TestTrue(TEXT("off panel"), ARtsMinimapHud::ProductionHit(Vp, 6, FVector2D(10, 10), Index) == ARtsMinimapHud::EProductionHit::None);
	}

	URtsSimHostBridge* Bridge = NewObject<URtsSimHostBridge>();
	FString Err;
	if (!TestTrue(TEXT("start + init"), Bridge->StartAndInit(ProductionInit, Err)))
	{
		AddError(Err);
		Bridge->Shutdown();
		return false;
	}
	FRtsHostReply R;
	int32 T = 0;
	bool bOk = true;
	auto Step = [&](const FString& Body = FString())
	{
		bOk = bOk && Bridge->Step(ProdStep(T, Body), 5.0, R) && R.bOk;
		++T;
	};
	auto Run = [&](int32 N) { for (int32 i = 0; i < N && bOk; ++i) Step(); };

	Step(TEXT("\"faction\":0,\"type\":\"deploy\",\"unit\":0"));
	Run(205);
	Step(TEXT("\"faction\":0,\"type\":\"place\",\"building\":\"coalition-reactor\",\"at\":[12,8]"));
	Run(450);
	Step(TEXT("\"faction\":0,\"type\":\"place\",\"building\":\"coalition-barracks\",\"at\":[8,12]"));
	Run(450);
	const FRtsUnitSnapshot* Barracks = R.Units.FindByPredicate([](const FRtsUnitSnapshot& U) { return U.BuildingId == TEXT("coalition-barracks"); });
	if (!TestNotNull(TEXT("barracks stands"), Barracks) || !TestTrue(TEXT("steps ok"), bOk))
	{
		Bridge->Shutdown();
		return false;
	}
	TestEqual(TEXT("barracks finished"), Barracks->ConstructionRemaining, 0);
	TestFalse(TEXT("no rally yet"), Barracks->bHasRally);
	const int32 BId = Barracks->Id;

	// Exactly the game mode's bodies: two trains, then a rally.
	const FString Train = FString::Printf(TEXT("\"faction\":0,\"type\":\"train\",\"building\":%d,\"unit\":\"rifleman\""), BId);
	Step(Train);
	Step(Train);
	Step(FString::Printf(TEXT("\"faction\":0,\"type\":\"rally\",\"building\":%d,\"at\":[22.5000,20.5000]"), BId));
	const FRtsUnitSnapshot* Q = R.Units.FindByPredicate([BId](const FRtsUnitSnapshot& U) { return U.Id == BId; });
	if (TestNotNull(TEXT("barracks row"), Q))
	{
		TestEqual(TEXT("two queued"), Q->ProductionQueue.Num(), 2);
		TestTrue(TEXT("rally decoded"), Q->bHasRally && FMath::IsNearlyEqual(Q->RallyX, 22.5) && FMath::IsNearlyEqual(Q->RallyY, 20.5));
		TestTrue(TEXT("head progress"), Q->QueueHeadTotal > 0 && Q->QueueHeadRemaining <= Q->QueueHeadTotal);
	}
	const int32 CreditsBefore = R.Factions.Num() > 0 ? R.Factions[0].Credits : 0;
	Step(FString::Printf(TEXT("\"faction\":0,\"type\":\"cancel\",\"building\":%d,\"index\":1"), BId));
	Q = R.Units.FindByPredicate([BId](const FRtsUnitSnapshot& U) { return U.Id == BId; });
	TestTrue(TEXT("one left after cancel"), Q && Q->ProductionQueue.Num() == 1);
	TestTrue(TEXT("cancel refunded"), R.Factions.Num() > 0 && R.Factions[0].Credits > CreditsBefore);

	Run(500);
	const FRtsUnitSnapshot* Rifle = R.Units.FindByPredicate([](const FRtsUnitSnapshot& U)
	{
		return U.Faction == 0 && !U.IsBuilding() && !U.bHarvester && U.DeploysTo.IsEmpty();
	});
	if (TestNotNull(TEXT("rifleman trained"), Rifle))
		TestTrue(TEXT("rifleman walked to the rally point"), FMath::FloorToInt(Rifle->X) == 22 && FMath::FloorToInt(Rifle->Y) == 20);
	TestTrue(TEXT("steps ok"), bOk);
	Bridge->Shutdown();
	return true;
}

#endif // WITH_DEV_AUTOMATION_TESTS
