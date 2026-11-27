// T17: the imported rifleman is where RtsSimUnitActor::SetModel loads it from, with the three
// clips it plays and the uniform slots it tints. Fails if the import (unreal/Scripts/
// import_rifleman.py) is missing or renamed something.
#include "Misc/AutomationTest.h"
#include "Engine/SkeletalMesh.h"
#include "Animation/AnimSequence.h"

#if WITH_DEV_AUTOMATION_TESTS

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FRtsRiflemanAssetTest, "RtsBridge.UI.RiflemanAsset",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FRtsRiflemanAssetTest::RunTest(const FString& Parameters)
{
	const USkeletalMesh* Mesh = LoadObject<USkeletalMesh>(nullptr, TEXT("/Game/Units/Rifleman/rifleman/SkeletalMeshes/rifleman.rifleman"));
	if (!TestNotNull(TEXT("rifleman mesh"), Mesh))
		return false;
	for (const TCHAR* Slot : { TEXT("coalition_plate"), TEXT("coalition_armor") })
		TestTrue(FString::Printf(TEXT("slot %s"), Slot), Mesh->GetMaterials().ContainsByPredicate(
			[Slot](const FSkeletalMaterial& M) { return M.MaterialSlotName == FName(Slot); }));
	for (const TCHAR* Clip : { TEXT("riflemanidle"), TEXT("riflemanmove"), TEXT("riflemanfire") })
	{
		const FString Path = FString::Printf(TEXT("/Game/Units/Rifleman/rifleman/SkeletalMeshes/%s.%s"), Clip, Clip);
		const UAnimSequence* A = LoadObject<UAnimSequence>(nullptr, *Path);
		if (TestNotNull(Clip, A))
			TestTrue(FString::Printf(TEXT("%s has length"), Clip), A->GetPlayLength() > 0.1f);
	}
	return true;
}

#endif
