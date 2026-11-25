// T15: the bridge hands presentation everything health bars and deaths need — hp and
// maxHp per unit from snapshots, and a death event naming the unit that leaves the roster.
// No game rules here — only assertions against the real host.
#include "Misc/AutomationTest.h"
#include "RtsSimHostBridge.h"

#if WITH_DEV_AUTOMATION_TESTS

namespace
{
	// Three riflemen (faction 0) against one conscript (faction 1, id 3), already in sight.
	const TCHAR* HealthInit = TEXT(
		"{\"protocolVersion\":1,\"kind\":\"init\",\"scenario\":"
		"{\"schemaVersion\":2,\"seed\":1,\"map\":{\"width\":12,\"height\":8,\"blocked\":[]},"
		"\"units\":[{\"unit\":\"rifleman\",\"at\":[2,3]},{\"unit\":\"rifleman\",\"at\":[2,4]},"
		"{\"unit\":\"rifleman\",\"at\":[2,5]},{\"unit\":\"conscript\",\"at\":[5,4]}]}}");
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FRtsBridgeHealthTest, "RtsBridge.Host.HealthAndDeath",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FRtsBridgeHealthTest::RunTest(const FString& Parameters)
{
	URtsSimHostBridge* Bridge = NewObject<URtsSimHostBridge>();
	FString Err;
	if (!TestTrue(TEXT("start + init"), Bridge->StartAndInit(HealthInit, Err)))
	{
		AddError(Err);
		Bridge->Shutdown();
		return false;
	}

	bool bSawFullHealth = false, bSawDamage = false, bSawShot = false;
	bool bDeathMatchesRoster = false;
	FRtsHostReply R;
	for (int32 T = 0; T < 400 && !bDeathMatchesRoster; ++T)
	{
		const FString Step = FString::Printf(
			TEXT("{\"protocolVersion\":1,\"kind\":\"step\",\"tick\":%d,\"commands\":[]}"), T);
		if (!Bridge->Step(Step, 5.0, R) || !R.bOk)
		{
			AddError(FString::Printf(TEXT("step %d: %s"), T, *R.Error));
			break;
		}
		for (const FRtsUnitSnapshot& U : R.Units)
		{
			if (U.MaxHp > 0.0 && U.Hp == U.MaxHp)
				bSawFullHealth = true;
			if (U.Id == 3 && U.Hp < U.MaxHp && U.Hp > 0.0)
				bSawDamage = true;
		}
		for (const FRtsSimEvent& E : R.Events)
		{
			if (E.Kind == TEXT("shot") && E.Amount > 0.0)
				bSawShot = true;
			if (E.Kind == TEXT("death") && E.Subject == 3)
			{
				bool bStillListed = false;
				for (const FRtsUnitSnapshot& U : R.Units)
					bStillListed |= U.Id == 3;
				bDeathMatchesRoster = !bStillListed && E.Faction == 1;
			}
		}
	}

	TestTrue(TEXT("units start at full health with maxHp > 0"), bSawFullHealth);
	TestTrue(TEXT("conscript hp drops below maxHp before it dies"), bSawDamage);
	TestTrue(TEXT("shot events carry a damage amount"), bSawShot);
	TestTrue(TEXT("death event names the unit that left the roster"), bDeathMatchesRoster);

	Bridge->Shutdown();
	return true;
}

#endif // WITH_DEV_AUTOMATION_TESTS
