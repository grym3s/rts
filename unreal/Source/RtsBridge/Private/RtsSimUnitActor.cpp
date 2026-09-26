#include "RtsSimUnitActor.h"
#include "UObject/ConstructorHelpers.h"
#include "Components/StaticMeshComponent.h"
#include "Materials/MaterialInstanceDynamic.h"

// Design scale: 100 UU per map cell (docs/superpowers/specs/2026-09-26-unreal-rts-migration-design.md).
static constexpr float SimCellUU = 100.0f;

ARtsSimUnitActor::ARtsSimUnitActor()
{
	PrimaryActorTick.bCanEverTick = false;
	Mesh = CreateDefaultSubobject<UStaticMeshComponent>(TEXT("Mesh"));
	RootComponent = Mesh;
	// Engine-shipped placeholder cube; no project content needed.
	static ConstructorHelpers::FObjectFinder<UStaticMesh> CubeFinder(
		TEXT("/Engine/BasicShapes/Cube.Cube"));
	if (CubeFinder.Succeeded())
	{
		Mesh->SetStaticMesh(CubeFinder.Object);
		Mesh->SetRelativeScale3D(FVector(0.5f));
	}

	// Flat cylinder = selection ring; hidden until selected.
	static ConstructorHelpers::FObjectFinder<UStaticMesh> CylFinder(
		TEXT("/Engine/BasicShapes/Cylinder.Cylinder"));
	Ring = CreateDefaultSubobject<UStaticMeshComponent>(TEXT("Ring"));
	Ring->SetupAttachment(RootComponent);
	Ring->SetRelativeLocation(FVector(0.0f, 0.0f, -49.0f));
	Ring->SetRelativeScale3D(FVector(1.4f, 1.4f, 0.05f));
	Ring->SetCastShadow(false);
	Ring->SetVisibility(false);
	if (CylFinder.Succeeded())
	{
		Ring->SetStaticMesh(CylFinder.Object);
		// BasicShapeMaterial exposes a "Color" scalar parameter (verified in the asset).
		UMaterialInterface* Base = nullptr;
		if (CylFinder.Object && CylFinder.Object->GetStaticMaterials().Num() > 0)
			Base = CylFinder.Object->GetStaticMaterials()[0].MaterialInterface;
		if (Base)
		{
			if (UMaterialInstanceDynamic* RingMid = UMaterialInstanceDynamic::Create(Base, this))
			{
				RingMid->SetVectorParameterValue(TEXT("Color"), FLinearColor(0.2f, 1.0f, 0.3f));
				Ring->SetMaterial(0, RingMid);
			}
		}
	}
}

void ARtsSimUnitActor::SetSimPosition(double X, double Y)
{
	// Sim map X -> world X, map Y -> world Y, height stays on the ground plane (slice 4).
	SetActorLocation(FVector(X * SimCellUU, Y * SimCellUU, 50.0));
}

void ARtsSimUnitActor::SetFaction(int32 Faction)
{
	static const FLinearColor Palette[] = {
		FLinearColor(0.85f, 0.25f, 0.20f), // faction 0 — red
		FLinearColor(0.20f, 0.45f, 0.90f), // faction 1 — blue
		FLinearColor(0.85f, 0.75f, 0.20f), // faction 2 — gold
	};
	if (!Mid)
	{
		UMaterialInterface* Base = Mesh->GetMaterial(0);
		if (!Base)
			return;
		Mid = UMaterialInstanceDynamic::Create(Base, this);
		Mesh->SetMaterial(0, Mid);
	}
	Mid->SetVectorParameterValue(TEXT("Color"), Palette[FMath::Clamp(Faction, 0, 2)]);
}

void ARtsSimUnitActor::SetSelected(bool bSelected)
{
	Ring->SetVisibility(bSelected);
}
