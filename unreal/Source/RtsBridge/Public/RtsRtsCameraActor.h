// High-angle RTS camera (presentation only), StarCraft-style: fixed north-up angle,
// scrolls in screen directions, wheel zooms along the view axis, the view centre is kept
// inside the map. No sim knowledge.
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

	void Scroll(const FVector& Axis2D);      // X = screen up, Y = screen right; unit-length per axis
	void Zoom(float DeltaZoom);              // + = closer
	/** Centre the view on a world ground point (minimap jump). */
	void JumpTo(const FVector2D& WorldXY);
	/** World-space XY rectangle the view centre may move within (the map). */
	void SetBounds(const FBox2D& InBounds);

private:
	UPROPERTY(VisibleAnywhere)
	TObjectPtr<UCameraComponent> Camera;

	// Design scale: 100 UU per map cell; default framing shows the whole 16x16 fixture field.
	// Yaw -90 => screen up = world -Y, screen right = world +X (minimap uses the same mapping).
	FVector Target = FVector(800.0, 800.0, 0.0);
	float YawDeg = -90.0f;
	float PitchDeg = -55.0f;
	float Distance = 1600.0f;
	FBox2D Bounds = FBox2D(ForceInit);

	static constexpr float MinDistance = 400.0f;
	static constexpr float MaxDistance = 5000.0f;
	static constexpr float MinPitch = -80.0f;
	static constexpr float MaxPitch = -25.0f;

	void ApplyTransform();
};
