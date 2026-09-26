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

#endif // WITH_DEV_AUTOMATION_TESTS
