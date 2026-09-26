// High-angle RTS camera (presentation only). WASD/arrows scroll relative to yaw,
// wheel zooms along the view axis, Q/E rotate. No sim knowledge.
#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "RtsRtsCameraActor.generated.h"
class UCameraComponent;

UCLASS()
class RTSBRIDGE_API ARtsRtsCameraActor : public AActor
{
	GENERATED_BODY()

public:
	ARtsRtsCameraActor();

	virtual void Tick(float DeltaSeconds) override;

	void Scroll(const FVector& Axis2D);      // world-plane direction, normalized
	void Zoom(float DeltaZoom);              // + = closer
	void RotateYaw(float DeltaDeg) { YawDeg += DeltaDeg; ApplyTransform(); }

private:
	UPROPERTY(VisibleAnywhere)
	TObjectPtr<UCameraComponent> Camera;

	// Design scale: 100 UU per map cell; default framing covers a ~20 cell view.
	FVector Target = FVector(800.0, 800.0, 0.0);
	float YawDeg = -90.0f;
	float PitchDeg = -55.0f;
	float Distance = 1400.0f;

	static constexpr float MinDistance = 400.0f;
	static constexpr float MaxDistance = 3000.0f;
	static constexpr float MinPitch = -80.0f;
	static constexpr float MaxPitch = -25.0f;

	void ApplyTransform();
};
