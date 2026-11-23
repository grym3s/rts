// Placeholder unit representation (slice 4): cube tinted per faction + selection ring.
// Position comes ONLY from host snapshots (smoothed between them); no movement logic here.
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

	/** New authoritative snapshot position (map coords): glide to it over one sim tick. */
	void SetSimPosition(double X, double Y);
	/** Place exactly (first sight of the unit), no glide. */
	void SnapToSimPosition(double X, double Y);
	virtual void Tick(float DeltaSeconds) override;

	void SetFaction(int32 Faction);
	int32 GetFaction() const { return Faction; }
	/** Shared faction palette (unit tint + minimap dots). */
	static FLinearColor FactionColor(int32 Faction);
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
	int32 Faction = 0;

	// Presentation-only smoothing between 20 t/s snapshots: render lags the sim by at most
	// one tick; it never extrapolates past the latest snapshot. Picking/minimap read the
	// rendered location.
	FVector InterpFrom = FVector::ZeroVector;
	FVector InterpTo = FVector::ZeroVector;
	float InterpElapsed = 0.0f;
	float TargetYaw = 0.0f;
	static constexpr float SimTickSeconds = 1.0f / 20.0f;
	static constexpr float TurnSpeed = 12.0f; // RInterpTo speed for facing
};
