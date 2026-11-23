#include "RtsRtsCameraActor.h"
#include "Camera/CameraComponent.h"

ARtsRtsCameraActor::ARtsRtsCameraActor()
{
	PrimaryActorTick.bCanEverTick = false;
	Camera = CreateDefaultSubobject<UCameraComponent>(TEXT("Camera"));
	RootComponent = Camera;
	ApplyTransform();
}

void ARtsRtsCameraActor::ApplyTransform()
{
	PitchDeg = FMath::Clamp(PitchDeg, MinPitch, MaxPitch);
	Distance = FMath::Clamp(Distance, MinDistance, MaxDistance);
	if (Bounds.bIsValid)
	{
		Target.X = FMath::Clamp(Target.X, Bounds.Min.X, Bounds.Max.X);
		Target.Y = FMath::Clamp(Target.Y, Bounds.Min.Y, Bounds.Max.Y);
	}
	// Orbit: sit Distance back along the view axis from the ground target.
	const FRotator Rot(PitchDeg, YawDeg, 0.0f);
	SetActorLocationAndRotation(Target - Rot.Vector() * Distance, Rot);
}

void ARtsRtsCameraActor::Scroll(const FVector& Axis2D)
{
	const FVector Fwd = FRotator(0.0f, YawDeg, 0.0f).RotateVector(FVector::ForwardVector);
	const FVector Right = FRotator(0.0f, YawDeg, 0.0f).RotateVector(FVector::RightVector);
	// Speed scales with zoom so the view feels anchored at any distance.
	const float Speed = Distance * 1.5f;
	Target += (Fwd * Axis2D.X + Right * Axis2D.Y) * Speed * GetWorld()->GetDeltaSeconds();
	ApplyTransform();
}

void ARtsRtsCameraActor::Zoom(float DeltaZoom)
{
	Distance *= (1.0f - DeltaZoom * 0.1f);
	ApplyTransform();
}

void ARtsRtsCameraActor::JumpTo(const FVector2D& WorldXY)
{
	Target.X = WorldXY.X;
	Target.Y = WorldXY.Y;
	ApplyTransform();
}

void ARtsRtsCameraActor::SetBounds(const FBox2D& InBounds)
{
	Bounds = InBounds;
	ApplyTransform();
}
