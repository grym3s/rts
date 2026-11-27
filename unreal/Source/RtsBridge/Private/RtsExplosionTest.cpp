// T18: the explosion timeline is short and bounded (pure, no world): it flashes at once, the
// fireball swells then cools, the shock ring is gone in well under a second, nothing is left at
// the end.
#include "Misc/AutomationTest.h"
#include "RtsExplosionActor.h"

#if WITH_DEV_AUTOMATION_TESTS

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FRtsExplosionTest, "RtsBridge.UI.Explosion",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FRtsExplosionTest::RunTest(const FString& Parameters)
{
	using F = ARtsExplosionActor;
	const F::FFrame Start = F::FrameAt(0.0f);
	TestTrue(TEXT("flash at ignition"), Start.Flash > 0.99f);
	TestTrue(TEXT("white-hot at ignition"), Start.Heat > 0.99f);
	const F::FFrame Peak = F::FrameAt(0.2f);
	TestTrue(TEXT("fireball has swelled by 0.2 s"), Peak.Fireball > 0.9f);
	TestTrue(TEXT("the flash is short"), F::FrameAt(0.3f).Flash == 0.0f);
	TestTrue(TEXT("the shock ring is gone by 0.4 s"), F::FrameAt(0.4f).RingAlpha == 0.0f);
	TestTrue(TEXT("it has cooled by 0.6 s"), F::FrameAt(0.6f).Heat == 0.0f);
	TestTrue(TEXT("the smoke shrinks before the end"), F::FrameAt(1.0f).Fireball < Peak.Fireball * 0.4f);
	const F::FFrame End = F::FrameAt(F::LifeSeconds);
	TestTrue(TEXT("nothing left at the end"), End.Fireball == 0.0f && End.Flash == 0.0f && End.RingAlpha == 0.0f);
	TestTrue(TEXT("about a second in all"), F::LifeSeconds <= 1.5f);
	return true;
}

#endif
