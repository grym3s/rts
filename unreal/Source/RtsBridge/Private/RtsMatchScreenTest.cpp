// T19: the result screen's layout and hit test (pure, no viewport needed).
// Play again exists only when restart is offered (single player); Quit always.
#include "Misc/AutomationTest.h"
#include "RtsMinimapHud.h"
#include "RtsBridgeGameMode.h"

#if WITH_DEV_AUTOMATION_TESTS

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FRtsMatchScreenTest, "RtsBridge.UI.MatchScreen",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FRtsMatchScreenTest::RunTest(const FString& Parameters)
{
	using EB = ARtsMinimapHud::EMatchScreenButton;
	for (const FVector2D Vp : { FVector2D(1920, 1080), FVector2D(1280, 720), FVector2D(400, 700) })
	{
		const auto L = ARtsMinimapHud::MatchScreenLayout(Vp, /*bCanRestart=*/true);
		TestTrue(TEXT("panel fits the viewport"), L.Panel.Min.X >= 0 && L.Panel.Max.X <= Vp.X && L.Panel.Min.Y >= 0 && L.Panel.Max.Y <= Vp.Y);
		TestTrue(TEXT("buttons sit inside the panel"), L.Panel.IsInside(L.PlayAgain) && L.Panel.IsInside(L.Quit));
		TestFalse(TEXT("buttons do not overlap"), L.PlayAgain.Intersect(L.Quit));
		TestEqual(TEXT("centre of Play again hits it"), ARtsMinimapHud::MatchScreenHit(Vp, L.PlayAgain.GetCenter(), true), EB::PlayAgain);
		TestEqual(TEXT("centre of Quit hits it"), ARtsMinimapHud::MatchScreenHit(Vp, L.Quit.GetCenter(), true), EB::Quit);
		TestEqual(TEXT("panel title area is not a button"), ARtsMinimapHud::MatchScreenHit(Vp, L.Panel.GetCenter() - FVector2D(0, 100), true), EB::None);
		TestEqual(TEXT("outside the panel is not a button"), ARtsMinimapHud::MatchScreenHit(Vp, FVector2D(2, 2), true), EB::None);

		// No restart offered (not single player): no Play again, even where it would be.
		const auto N = ARtsMinimapHud::MatchScreenLayout(Vp, /*bCanRestart=*/false);
		TestFalse(TEXT("no Play again without restart"), N.PlayAgain.bIsValid);
		TestEqual(TEXT("old Play again spot is dead without restart"), ARtsMinimapHud::MatchScreenHit(Vp, L.PlayAgain.GetCenter(), false), EB::None);
		TestEqual(TEXT("Quit still works without restart"), ARtsMinimapHud::MatchScreenHit(Vp, N.Quit.GetCenter(), false), EB::Quit);
	}
	return true;
}

// Start screen: one button per faction, button i = sim faction i.
IMPLEMENT_SIMPLE_AUTOMATION_TEST(FRtsFactionPickTest, "RtsBridge.UI.FactionPick",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FRtsFactionPickTest::RunTest(const FString& Parameters)
{
	TestEqual(TEXT("faction 0"), ARtsBridgeGameMode::FactionName(0), FString(TEXT("Coalition")));
	TestEqual(TEXT("faction 1"), ARtsBridgeGameMode::FactionName(1), FString(TEXT("Hegemony")));
	TestEqual(TEXT("faction 2"), ARtsBridgeGameMode::FactionName(2), FString(TEXT("Ascendant")));
	for (const FVector2D Vp : { FVector2D(1920, 1080), FVector2D(1280, 720), FVector2D(400, 700) })
	{
		const auto L = ARtsMinimapHud::FactionPickLayout(Vp);
		TestTrue(TEXT("panel fits the viewport"), L.Panel.Min.X >= 0 && L.Panel.Max.X <= Vp.X && L.Panel.Min.Y >= 0 && L.Panel.Max.Y <= Vp.Y);
		for (int32 i = 0; i < 3; ++i)
		{
			TestTrue(TEXT("button inside the panel"), L.Panel.IsInside(L.Faction[i]));
			TestEqual(TEXT("centre hits its faction"), ARtsMinimapHud::FactionPickHit(Vp, L.Faction[i].GetCenter()), i);
			for (int32 j = i + 1; j < 3; ++j)
				TestFalse(TEXT("buttons do not overlap"), L.Faction[i].Intersect(L.Faction[j]));
		}
		TestEqual(TEXT("title is not a button"), ARtsMinimapHud::FactionPickHit(Vp, FVector2D(L.Panel.GetCenter().X, L.Panel.Min.Y + 30)), -1);
		TestEqual(TEXT("outside is not a button"), ARtsMinimapHud::FactionPickHit(Vp, FVector2D(2, 2)), -1);
	}
	return true;
}

#endif // WITH_DEV_AUTOMATION_TESTS
