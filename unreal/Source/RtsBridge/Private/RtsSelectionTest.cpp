// Headless checks of FRtsSelection (no world, no host): same behaviour as the Godot
// SelectionController — click/shift-toggle, box replace/add, control groups, prune.
#include "Misc/AutomationTest.h"
#include "RtsSelection.h"

#if WITH_DEV_AUTOMATION_TESTS

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FRtsSelectionTest, "RtsBridge.Selection.Rules",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FRtsSelectionTest::RunTest(const FString& Parameters)
{
	FRtsSelection S;

	S.Click(1, false);
	TestTrue(TEXT("click selects only that unit"), S.Contains(1) && S.GetSelected().Num() == 1);
	S.Click(2, true);
	TestTrue(TEXT("shift-click adds"), S.Contains(1) && S.Contains(2));
	S.Click(1, true);
	TestTrue(TEXT("shift-click toggles off"), !S.Contains(1) && S.Contains(2));
	S.Click(INDEX_NONE, true);
	TestEqual(TEXT("shift-click on ground keeps selection"), S.GetSelected().Num(), 1);
	S.Click(INDEX_NONE, false);
	TestEqual(TEXT("click on ground clears"), S.GetSelected().Num(), 0);

	S.BoxSelect({3, 4}, false);
	S.BoxSelect({5}, true);
	TestTrue(TEXT("shift-box adds"), S.Contains(3) && S.Contains(4) && S.Contains(5));
	S.BoxSelect({6}, false);
	TestTrue(TEXT("plain box replaces"), S.Contains(6) && S.GetSelected().Num() == 1);

	S.BoxSelect({7, 8}, false);
	S.AssignGroup(0);
	S.Click(9, false);
	TestTrue(TEXT("recall group 1"), S.RecallGroup(0) && S.Contains(7) && S.Contains(8) && !S.Contains(9));
	TestFalse(TEXT("empty group 2 leaves selection alone"), S.RecallGroup(1));
	TestEqual(TEXT("selection unchanged by empty recall"), S.GetSelected().Num(), 2);

	S.Prune([](int32 Id) { return Id != 7; });
	TestTrue(TEXT("prune drops dead from selection"), !S.Contains(7) && S.Contains(8));
	TestFalse(TEXT("prune drops dead from groups"), S.GetGroup(0).Contains(7));
	return true;
}

#endif
