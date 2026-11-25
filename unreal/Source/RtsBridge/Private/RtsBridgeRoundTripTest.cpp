// In-editor runtime proof of the host bridge: launches the real .NET host,
// runs init + tick-stamped move + steps, and asserts the unit moved.
// No game rules here — only round-trip assertions against the host.
#include "Misc/AutomationTest.h"
#include "RtsSimHostBridge.h"

#if WITH_DEV_AUTOMATION_TESTS

namespace
{
	const TCHAR* RoundTripInit = TEXT(
		"{\"protocolVersion\":1,\"kind\":\"init\",\"scenario\":"
		"{\"schemaVersion\":2,\"seed\":1,\"map\":{\"width\":16,\"height\":16,\"blocked\":[]},"
		"\"units\":[{\"unit\":\"collector\",\"at\":[5,5]}]}}");
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FRtsBridgeRoundTripTest, "RtsBridge.Host.RoundTrip",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FRtsBridgeRoundTripTest::RunTest(const FString& Parameters)
{
	URtsSimHostBridge* Bridge = NewObject<URtsSimHostBridge>();

	FString Err;
	if (!TestTrue(TEXT("start + init round-trip"), Bridge->StartAndInit(RoundTripInit, Err)))
	{
		AddError(FString::Printf(TEXT("StartAndInit: %s"), *Err));
		Bridge->Shutdown();
		return false;
	}

	FRtsHostReply Reply;
	const FString Step0 = TEXT(
		"{\"protocolVersion\":1,\"kind\":\"step\",\"tick\":0,"
		"\"commands\":[{\"tick\":0,\"faction\":0,\"type\":\"move\",\"target\":[10,5]}]}");
	if (!TestTrue(TEXT("step 0 ok"), Bridge->Step(Step0, 5.0, Reply)) ||
		!TestTrue(TEXT("step 0 accepted"), Reply.bOk))
	{
		AddError(FString::Printf(TEXT("Step0: %s"), *Reply.Error));
		Bridge->Shutdown();
		return false;
	}

	FRtsHostReply Last;
	bool bStepped = true;
	for (int32 T = 1; T < 120 && bStepped; ++T)
	{
		const FString Step = FString::Printf(
			TEXT("{\"protocolVersion\":1,\"kind\":\"step\",\"tick\":%d,\"commands\":[]}"), T);
		bStepped = Bridge->Step(Step, 5.0, Last) && Last.bOk;
		if (!bStepped)
			AddError(FString::Printf(TEXT("Step %d: %s"), T, *Last.Error));
	}
	TestTrue(TEXT("120 steps accepted"), bStepped);

	// Host is the only position authority; spawn was x=5, target x=10.
	bool bMovedPast = false;
	for (const FRtsUnitSnapshot& U : Last.Units)
		if (U.X > 5.5)
			bMovedPast = true;
	TestTrue(TEXT("collector advanced past x=5.5 (move round-trip)"), bMovedPast);
	TestTrue(TEXT("state hash present (16 hex digits)"), Last.Hash.Len() == 16);

	Bridge->Shutdown();
	return true;
}

// T07: restart on the same process. reset + init must replay the match exactly
// (same hash after the same steps), and a second init without reset is refused.
IMPLEMENT_SIMPLE_AUTOMATION_TEST(FRtsBridgeRestartTest, "RtsBridge.Host.Restart",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FRtsBridgeRestartTest::RunTest(const FString& Parameters)
{
	URtsSimHostBridge* Bridge = NewObject<URtsSimHostBridge>();
	FString Err;
	if (!TestTrue(TEXT("start + init"), Bridge->StartAndInit(RoundTripInit, Err)))
	{
		AddError(Err);
		Bridge->Shutdown();
		return false;
	}

	auto Play = [&](FString& OutHash) -> bool
	{
		FRtsHostReply R;
		for (int32 T = 0; T < 30; ++T)
		{
			const FString Cmds = T == 0
				? TEXT("[{\"tick\":0,\"faction\":0,\"type\":\"move\",\"target\":[10,5]}]")
				: TEXT("[]");
			const FString Step = FString::Printf(
				TEXT("{\"protocolVersion\":1,\"kind\":\"step\",\"tick\":%d,\"commands\":%s}"), T, *Cmds);
			if (!Bridge->Step(Step, 5.0, R) || !R.bOk)
			{
				AddError(FString::Printf(TEXT("step %d: %s"), T, *R.Error));
				return false;
			}
		}
		OutHash = R.Hash;
		return true;
	};

	FString First, Second;
	bool bOk = Play(First);

	FRtsHostReply R;
	bOk &= TestTrue(TEXT("second init without reset is refused"),
		Bridge->Step(RoundTripInit, 5.0, R) && !R.bOk);
	bOk &= TestTrue(TEXT("reset accepted"),
		Bridge->Step(TEXT("{\"protocolVersion\":1,\"kind\":\"reset\"}"), 5.0, R) && R.bOk);
	bOk &= TestTrue(TEXT("init after reset accepted"), Bridge->Step(RoundTripInit, 10.0, R) && R.bOk);
	bOk = bOk && Play(Second);
	TestEqual(TEXT("restarted match replays the same hash"), Second, First);

	Bridge->Shutdown();
	return bOk;
}

// T20: a step with "debug":true brings back state.debug (state, target, path, step time);
// a plain step does not.
IMPLEMENT_SIMPLE_AUTOMATION_TEST(FRtsBridgeDebugTest, "RtsBridge.Host.Debug",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FRtsBridgeDebugTest::RunTest(const FString& Parameters)
{
	URtsSimHostBridge* Bridge = NewObject<URtsSimHostBridge>();
	FString Err;
	if (!TestTrue(TEXT("start + init"), Bridge->StartAndInit(RoundTripInit, Err)))
	{
		AddError(Err);
		Bridge->Shutdown();
		return false;
	}
	FRtsHostReply R;
	const bool bPlain = Bridge->Step(TEXT(
		"{\"protocolVersion\":1,\"kind\":\"step\",\"tick\":0,"
		"\"commands\":[{\"tick\":0,\"faction\":0,\"type\":\"move\",\"target\":[10,5]}]}"), 5.0, R) && R.bOk;
	TestTrue(TEXT("plain step accepted"), bPlain);
	TestFalse(TEXT("plain step has no debug block"), R.bHasDebug);
	TestTrue(TEXT("round trip is timed"), R.RoundTripMs > 0.0);

	const bool bDebug = Bridge->Step(TEXT(
		"{\"protocolVersion\":1,\"kind\":\"step\",\"tick\":1,\"commands\":[],\"debug\":true}"), 5.0, R) && R.bOk;
	TestTrue(TEXT("debug step accepted"), bDebug);
	TestTrue(TEXT("debug block present"), R.bHasDebug);
	TestTrue(TEXT("step time reported"), R.StepMicros >= 0);
	const FRtsDebugUnit* Mover = R.DebugUnits.Num() > 0 ? &R.DebugUnits[0] : nullptr;
	if (TestNotNull(TEXT("one debug row"), Mover))
	{
		TestEqual(TEXT("mover state"), Mover->State, FString(TEXT("move")));
		TestEqual(TEXT("no target"), Mover->Target, -1);
		TestTrue(TEXT("path ends at the move target (x=10)"),
			Mover->Path.Num() > 0 && FMath::IsNearlyEqual(Mover->Path.Last().X, 10.0, 0.6));
	}
	Bridge->Shutdown();
	return bPlain && bDebug;
}

#endif // WITH_DEV_AUTOMATION_TESTS
