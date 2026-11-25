// T19: the result screen's layout and hit test (pure, no viewport needed).
// Play again exists only when restart is offered (single player); Quit always.
#include "Misc/AutomationTest.h"
#include "RtsMinimapHud.h"

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

#endif // WITH_DEV_AUTOMATION_TESTS
