// T18: one representative explosion, procedural (engine basic shapes, no assets): a white-hot
// fireball that swells and cools to grey smoke while it rises, a flat shock ring racing out,
// and a short light flash. About a second, then it destroys itself. Presentation only: spawned
// by the game mode from sim death events and superweapon strikes, never touches sim state.
// Colours stay off the team hues (red / blue / gold): white, warm white, then grey.
#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "RtsExplosionActor.generated.h"

class UStaticMeshComponent;
class UPointLightComponent;
class UMaterialInstanceDynamic;

UCLASS()
class RTSBRIDGE_API ARtsExplosionActor : public AActor
{
	GENERATED_BODY()

public:
	ARtsExplosionActor();
	virtual void Tick(float DeltaSeconds) override;

	/** Start it: SizeUU is the blast radius on the ground (a tank ~ 90, a building its footprint, a strike its radius). */
	void Ignite(float SizeUU);

	/** Pure timeline (tests and tuning): fireball scale factor, its brightness 0..1, ring radius
	 *  factor, ring alpha, light intensity factor, at T seconds after ignition. */
	struct FFrame
	{
		float Fireball = 0.0f;
		float Heat = 0.0f;
		float Ring = 0.0f;
		float RingAlpha = 0.0f;
		float Flash = 0.0f;
		float Rise = 0.0f;
	};
	static FFrame FrameAt(float T);
	static constexpr float LifeSeconds = 1.1f;

private:
	UPROPERTY()
	TObjectPtr<UStaticMeshComponent> Ball;
	UPROPERTY()
	TObjectPtr<UStaticMeshComponent> Shock;
	UPROPERTY()
	TObjectPtr<UPointLightComponent> Light;
	UPROPERTY()
	TObjectPtr<UMaterialInstanceDynamic> BallMid;
	UPROPERTY()
	TObjectPtr<UMaterialInstanceDynamic> RingMid;
	float Size = 100.0f;
	float Elapsed = 0.0f;
	FVector Origin = FVector::ZeroVector;
	void Apply();
};
