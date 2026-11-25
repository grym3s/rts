#include "RtsVeinActor.h"
#include "Components/InstancedStaticMeshComponent.h"
#include "Materials/MaterialInstanceDynamic.h"
#include "Engine/StaticMesh.h"

ARtsVeinActor::ARtsVeinActor()
{
	PrimaryActorTick.bCanEverTick = false;
	RootComponent = CreateDefaultSubobject<USceneComponent>(TEXT("Root"));
	RootComponent->SetMobility(EComponentMobility::Movable);
	Crystals = CreateDefaultSubobject<UInstancedStaticMeshComponent>(TEXT("Crystals"));
	Crystals->SetupAttachment(RootComponent);
	Crystals->SetMobility(EComponentMobility::Movable);
	Crystals->SetCollisionEnabled(ECollisionEnabled::NoCollision);
	Crystals->SetCastShadow(false);
}

FVector ARtsVeinActor::VeinScale(int32 Pool, int32 MaxPool)
{
	if (Pool <= 0 || MaxPool <= 0)
		return FVector::ZeroVector;
	const float F = FMath::Clamp(float(Pool) / float(MaxPool), 0.0f, 1.0f);
	const float XY = 0.3f + 0.5f * F; // a full vein nearly fills its cell, a dregs one is a pebble
	const float Z = 0.08f + 0.32f * F;
	return FVector(XY, XY, Z);
}

void ARtsVeinActor::UpdateVeins(const TArray<FRtsVeinSnapshot>& Veins, float CellUU)
{
	if (!Crystals->GetStaticMesh())
	{
		UStaticMesh* Cube = LoadObject<UStaticMesh>(nullptr, TEXT("/Engine/BasicShapes/Cube.Cube"));
		if (!Cube)
			return;
		Crystals->SetStaticMesh(Cube);
		if (UMaterialInterface* Base = LoadObject<UMaterialInterface>(nullptr, TEXT("/Engine/BasicShapes/BasicShapeMaterial.BasicShapeMaterial")))
			if (UMaterialInstanceDynamic* Mid = UMaterialInstanceDynamic::Create(Base, this))
			{
				Mid->SetVectorParameterValue(TEXT("Color"), FLinearColor(0.95f, 0.7f, 0.12f));
				Crystals->SetMaterial(0, Mid);
			}
	}
	bool bDirty = false;
	for (const FRtsVeinSnapshot& V : Veins)
	{
		int32& Max = MaxPool.FindOrAdd(V.Id, 0);
		Max = FMath::Max(Max, V.Pool);
		const int32* Last = LastPool.Find(V.Id);
		if (Last && *Last == V.Pool && InstanceOf.Contains(V.Id))
			continue;
		LastPool.Add(V.Id, V.Pool);
		const FVector S = VeinScale(V.Pool, Max);
		const FTransform Xf(FRotator(0.0f, 45.0f, 0.0f), FVector(V.X * CellUU, V.Y * CellUU, S.Z * 50.0f), S);
		if (const int32* Index = InstanceOf.Find(V.Id))
			Crystals->UpdateInstanceTransform(*Index, Xf, /*bWorldSpace=*/true, /*bMarkRenderStateDirty=*/false);
		else
			InstanceOf.Add(V.Id, Crystals->AddInstance(Xf, /*bWorldSpace=*/true));
		bDirty = true;
	}
	if (bDirty)
		Crystals->MarkRenderStateDirty();
}
