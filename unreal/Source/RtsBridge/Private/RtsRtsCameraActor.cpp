#include "RtsRtsCameraActor.h"
#include "Camera/CameraComponent.h"

ARtsRtsCameraActor::ARtsRtsCameraActor()
{
	PrimaryActorTick.bCanEverTick = true;
	Camera = CreateDefaultSubobject<UCameraComponent>(TEXT("Camera"));
	RootComponent = Camera;
	ApplyTransform();
}

void ARtsRtsCameraActor::Tick(float DeltaSeconds)
{
	Super::Tick(DeltaSeconds);
}

void ARtsRtsCameraActor::ApplyTransform()
{
	PitchDeg = FMath::Clamp(PitchDeg, MinPitch, MaxPitch);
	Distance = FMath::Clamp(Distance, MinDistance, MaxDistance);
	SetActorLocationAndRotation(Target, FRotator(PitchDeg, YawDeg, 0.0f));
}

void ARtsRtsCameraActor::Scroll(const FVector& Axis2D)
{
	const FVector Fwd = FRotator(0.0f, YawDeg, 0.0f).RotateVector(FVector::ForwardVector);
	const FVector Right = FRotator(0.0f, YawDeg, 0.0f).RotateVector(FVector::RightVector);
	// Speed scales with zoom so the view feels anchored at any distance.
	const float SpeedPx = Distance * 0.6f;
	Target += (Fwd * Axis2D.X + Right * Axis2D.Y) * SpeedPx * GetWorld()->GetDeltaSeconds();
	ApplyTransform();
}

void ARtsRtsCameraActor::Zoom(float DeltaZoom)
{
	Distance *= (1.0f - DeltaZoom * 0.1f);
	ApplyTransform();
}
