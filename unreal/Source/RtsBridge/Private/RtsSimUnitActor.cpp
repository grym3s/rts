#include "RtsSimUnitActor.h"
#include "UObject/ConstructorHelpers.h"
#include "Components/StaticMeshComponent.h"

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
}
