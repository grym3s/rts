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
	/** T28: re-apply the building shade on the next SetBuildingShape (after a change of owner). */
	void InvalidateShape() { ShapeKey = -1; }
	/** Shared faction palette (unit tint + minimap dots). */
	static FLinearColor FactionColor(int32 Faction);
	void SetSelected(bool bSelected);
	int32 GetSimUnitId() const { return SimUnitId; }
	void SetSimUnitId(int32 Id) { SimUnitId = Id; }

	/** Health from the latest snapshot (T15); the HUD draws the bar from it. */
	void SetHealth(double InHp, double InMaxHp) { Hp = InHp; MaxHp = InMaxHp; }
	float GetHealthFraction() const { return MaxHp > 0.0 ? float(FMath::Clamp(Hp / MaxHp, 0.0, 1.0)) : 1.0f; }
	bool IsDamaged() const { return MaxHp > 0.0 && Hp < MaxHp; }

	/** A death event named this unit: topple and darken, lie as a corpse, sink, then
	 *  destroy itself. Presentation only — the sim already removed the unit. */
	void BeginDeath();
	bool IsDying() const { return DeathElapsed >= 0.0f; }

	/** T36: draw as a W x H footprint, height and shade from construction progress (0..1). */
	void SetBuildingShape(int32 W, int32 H, float Progress);
	bool IsBuilding() const { return bIsBuilding; }
	/** T38: harvester load (0..1 of capacity) as a gold block; bCarrying = full and heading home. */
	void SetCargo(float Fraction, bool bCarrying);
	/** T25: aircraft are drawn this high above the ground; everything else sits on it. */
	static constexpr float AirZ = 300.0f;
	void SetAirborne(bool bAir);

	/** T17: swap the placeholder cube for the unit's imported model, if it has one (rifleman). */
	void SetModel(const FString& Type);
	/** T17: a shot event named this unit as the shooter: play the fire clip once. */
	void PlayFire();
	bool HasModel() const { return Body != nullptr; }

private:
	UPROPERTY(VisibleAnywhere)
	TObjectPtr<class USkeletalMeshComponent> Body;
	UPROPERTY()
	TArray<TObjectPtr<UMaterialInstanceDynamic>> BodyMids;
	UPROPERTY()
	TObjectPtr<class UAnimSequence> IdleClip;
	UPROPERTY()
	TObjectPtr<class UAnimSequence> MoveClip;
	UPROPERTY()
	TObjectPtr<class UAnimSequence> FireClip;
	enum class EClip : uint8 { None, Idle, Move, Fire };
	EClip Clip = EClip::None;
	float FireLeft = 0.0f; // seconds of the fire clip still to play
	static constexpr float FireSeconds = 0.5f; // about the sim's shot cadence; the clip is sped up to fit
	void TintBody(float Shade);
	void UpdateClip(float DeltaSeconds, bool bMoving);

	UPROPERTY(VisibleAnywhere)
	TObjectPtr<UStaticMeshComponent> Mesh;

	UPROPERTY(VisibleAnywhere)
	TObjectPtr<UStaticMeshComponent> Ring;

	UPROPERTY()
	TObjectPtr<UMaterialInstanceDynamic> Mid;

	UPROPERTY(VisibleAnywhere)
	TObjectPtr<UStaticMeshComponent> Cargo;

	UPROPERTY()
	TObjectPtr<UMaterialInstanceDynamic> CargoMid;

	int32 SimUnitId = INDEX_NONE;
	float GroundZ = 50.0f; // actor Z on the ground plane (buildings: half their height)
	int32 ShapeKey = -1;
	bool bIsBuilding = false;
	int32 Faction = 0;
	double Hp = 0.0;
	double MaxHp = 0.0;

	// Death sequence (seconds since BeginDeath; < 0 = alive).
	float DeathElapsed = -1.0f;
	FVector DeathFrom = FVector::ZeroVector;
	static constexpr float ToppleSeconds = 0.25f;
	static constexpr float CorpseSeconds = 2.0f;  // lies there, readable as a loss
	static constexpr float SinkSeconds = 1.0f;
	void TickDeath(float DeltaSeconds);

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
