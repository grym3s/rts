// Placeholder unit representation (slice 4): cube tinted per faction + selection ring.
// Position comes ONLY from host snapshots; no movement logic here.
#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "RtsSimUnitActor.generated.h"

class UStaticMeshComponent;
class UMaterialInstanceDynamic;

UCLASS()
class RTSBRIDGE_API ARtsSimUnitActor : public AActor
{
	GENERATED_BODY()

public:
	ARtsSimUnitActor();

	/** Sim map coords (double) -> world at 100 UU/cell (design scale). */
	void SetSimPosition(double X, double Y);

	void SetFaction(int32 Faction);
	void SetSelected(bool bSelected);
	int32 GetSimUnitId() const { return SimUnitId; }
	void SetSimUnitId(int32 Id) { SimUnitId = Id; }

private:
	UPROPERTY(VisibleAnywhere)
	TObjectPtr<UStaticMeshComponent> Mesh;

	UPROPERTY(VisibleAnywhere)
	TObjectPtr<UStaticMeshComponent> Ring;

	UPROPERTY()
	TObjectPtr<UMaterialInstanceDynamic> Mid;

	int32 SimUnitId = INDEX_NONE;
};
