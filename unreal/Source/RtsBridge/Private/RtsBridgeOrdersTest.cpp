// The order bodies Unreal sends (attack = unit id, attack-move = [x,y], stop = no target)
// are accepted by the real host. Same wire shape as ARtsBridgeGameMode::QueueOrderForSelection.
// No game rules here — only that the host takes each order and keeps stepping.
#include "Misc/AutomationTest.h"
#include "RtsSimHostBridge.h"

#if WITH_DEV_AUTOMATION_TESTS

namespace
{
	const TCHAR* OrdersInit = TEXT(
		"{\"protocolVersion\":1,\"kind\":\"init\",\"scenario\":"
		"{\"schemaVersion\":2,\"seed\":1,\"map\":{\"width\":16,\"height\":16,\"blocked\":[]},"
		"\"units\":[{\"unit\":\"rifleman\",\"at\":[4,5]},{\"unit\":\"conscript\",\"at\":[11,5]}]}}");

	bool StepWith(FAutomationTestBase& Test, URtsSimHostBridge* Bridge, int32 Tick, const FString& Commands, FRtsHostReply& Out)
	{
		const FString Req = FString::Printf(
			TEXT("{\"protocolVersion\":1,\"kind\":\"step\",\"tick\":%d,\"commands\":[%s]}"), Tick, *Commands);
		if (Bridge->Step(Req, 5.0, Out) && Out.bOk)
			return true;
		Test.AddError(FString::Printf(TEXT("step %d rejected: %s"), Tick, *Out.Error));
		return false;
	}
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FRtsBridgeOrdersTest, "RtsBridge.Host.Orders",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FRtsBridgeOrdersTest::RunTest(const FString& Parameters)
{
	URtsSimHostBridge* Bridge = NewObject<URtsSimHostBridge>();
	FString Err;
	if (!Bridge->StartAndInit(OrdersInit, Err))
	{
		AddError(FString::Printf(TEXT("StartAndInit: %s"), *Err));
		Bridge->Shutdown();
		return false;
	}

	FRtsHostReply R;
	bool bOk = StepWith(*this, Bridge, 0, FString(), R);
	int32 Mine = INDEX_NONE, Enemy = INDEX_NONE;
	for (const FRtsUnitSnapshot& U : R.Units)
		(U.Faction == 0 ? Mine : Enemy) = U.Id;
	if (!TestTrue(TEXT("one unit per faction"), bOk && Mine != INDEX_NONE && Enemy != INDEX_NONE))
	{
		Bridge->Shutdown();
		return false;
	}

	auto Body = [Mine](int32 Tick, const TCHAR* Type, const FString& Target)
	{
		FString B = FString::Printf(TEXT("{\"tick\":%d,\"faction\":0,\"type\":\"%s\",\"units\":[%d]"), Tick, Type, Mine);
		if (!Target.IsEmpty())
			B += TEXT(",\"target\":") + Target;
		return B + TEXT("}");
	};

	bOk = bOk && StepWith(*this, Bridge, 1, Body(1, TEXT("attack"), FString::Printf(TEXT("%d"), Enemy)), R);
	bOk = bOk && StepWith(*this, Bridge, 2, Body(2, TEXT("stop"), FString()), R);
	bOk = bOk && StepWith(*this, Bridge, 3, Body(3, TEXT("attack-move"), TEXT("[10.0000,5.0000]")), R);
	for (int32 T = 4; T < 40 && bOk; ++T)
		bOk = StepWith(*this, Bridge, T, FString(), R);
	TestTrue(TEXT("attack, stop and attack-move accepted; host keeps stepping"), bOk);

	Bridge->Shutdown();
	return true;
}

#endif
