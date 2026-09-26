// Placeholder primitive driven ONLY by host snapshots — no local simulation.
#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "RtsSimUnitActor.generated.h"

UCLASS()
class RTSBRIDGE_API ARtsSimUnitActor : public AActor
{
	GENERATED_BODY()

public:
	ARtsSimUnitActor();

	void SetSimPosition(double MapX, double MapY)
	{
		// design doc: map X -> world X, map Y -> world Y, 100 UU per cell, Z from ground.
		SetActorLocation(FVector(MapX * 100.0, MapY * 100.0, 50.0), false);
	}

	UPROPERTY(VisibleAnywhere)
	TObjectPtr<UStaticMeshComponent> Mesh;
};
