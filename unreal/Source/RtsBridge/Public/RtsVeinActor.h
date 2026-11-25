// T38: resource veins on the ground (presentation only): one gold crystal block per vein,
// shrinking as its pool empties (fraction of the largest pool seen for that vein), gone when dry.
// No collision: ground traces pass through to the land, and veins are picked by distance.
#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "RtsSimHostBridge.h"
#include "RtsVeinActor.generated.h"

class UInstancedStaticMeshComponent;

UCLASS()
class RTSBRIDGE_API ARtsVeinActor : public AActor
{
	GENERATED_BODY()

public:
	ARtsVeinActor();

	/** Apply the latest snapshot's veins (positions in cells). Cheap when nothing changed. */
	void UpdateVeins(const TArray<FRtsVeinSnapshot>& Veins, float CellUU);

	/** Pure: block scale (x = y, z) for a vein at Pool of MaxPool; zero when dry. Unit-tested. */
	static FVector VeinScale(int32 Pool, int32 MaxPool);

private:
	UPROPERTY() TObjectPtr<UInstancedStaticMeshComponent> Crystals;
	TMap<int32, int32> InstanceOf; // vein id -> instance index
	TMap<int32, int32> MaxPool;    // vein id -> largest pool seen
	TMap<int32, int32> LastPool;   // vein id -> pool last drawn
};
