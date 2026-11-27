#include "RtsExplosionActor.h"
#include "UObject/ConstructorHelpers.h"
#include "Components/StaticMeshComponent.h"
#include "Components/PointLightComponent.h"
#include "Materials/MaterialInstanceDynamic.h"

ARtsExplosionActor::ARtsExplosionActor()
{
	PrimaryActorTick.bCanEverTick = true;
	PrimaryActorTick.bStartWithTickEnabled = true;
	USceneComponent* Root = CreateDefaultSubobject<USceneComponent>(TEXT("Root"));
	Root->SetMobility(EComponentMobility::Movable);
	RootComponent = Root;
	static ConstructorHelpers::FObjectFinder<UStaticMesh> SphereFinder(TEXT("/Engine/BasicShapes/Sphere.Sphere"));
	static ConstructorHelpers::FObjectFinder<UStaticMesh> CylFinder(TEXT("/Engine/BasicShapes/Cylinder.Cylinder"));
	auto MakeMesh = [this, Root](const TCHAR* Name, UStaticMesh* M)
	{
		UStaticMeshComponent* C = CreateDefaultSubobject<UStaticMeshComponent>(Name);
		C->SetupAttachment(Root);
		C->SetMobility(EComponentMobility::Movable);
		C->SetCollisionEnabled(ECollisionEnabled::NoCollision);
		C->SetCastShadow(false);
		if (M)
			C->SetStaticMesh(M);
		return C;
	};
	Ball = MakeMesh(TEXT("Fireball"), SphereFinder.Succeeded() ? SphereFinder.Object : nullptr);
	Shock = MakeMesh(TEXT("ShockRing"), CylFinder.Succeeded() ? CylFinder.Object : nullptr);
	Light = CreateDefaultSubobject<UPointLightComponent>(TEXT("Flash"));
	Light->SetupAttachment(Root);
	Light->SetMobility(EComponentMobility::Movable);
	Light->SetLightColor(FLinearColor(1.0f, 0.93f, 0.8f));
	Light->SetCastShadows(false);
	Light->SetIntensity(0.0f);
}

ARtsExplosionActor::FFrame ARtsExplosionActor::FrameAt(float T)
{
	FFrame F;
	if (T < 0.0f || T >= LifeSeconds)
		return F;
	// fireball: swells fast (0.15 s), holds, then shrinks away as smoke; heat drops from
	// white-hot to grey over the first half
	const float Grow = FMath::Clamp(T / 0.15f, 0.0f, 1.0f);
	const float Fade = FMath::Clamp((T - 0.45f) / (LifeSeconds - 0.45f), 0.0f, 1.0f);
	F.Fireball = FMath::InterpEaseOut(0.25f, 1.0f, Grow, 2.0f) * (1.0f - 0.85f * Fade);
	F.Heat = 1.0f - FMath::Clamp(T / 0.55f, 0.0f, 1.0f);
	F.Rise = FMath::Clamp(T / LifeSeconds, 0.0f, 1.0f);
	// shock ring: races out to 1.6x the blast in 0.35 s and fades as it goes
	const float RingT = FMath::Clamp(T / 0.35f, 0.0f, 1.0f);
	F.Ring = FMath::InterpEaseOut(0.2f, 1.6f, RingT, 2.0f);
	F.RingAlpha = T < 0.35f ? 1.0f - RingT : 0.0f;
	// flash: peaks at once and is gone in 0.25 s
	F.Flash = FMath::Clamp(1.0f - T / 0.25f, 0.0f, 1.0f);
	return F;
}

void ARtsExplosionActor::Ignite(float SizeUU)
{
	Size = FMath::Max(20.0f, SizeUU);
	Elapsed = 0.0f;
	Origin = GetActorLocation();
	auto Tint = [this](UStaticMeshComponent* C) -> UMaterialInstanceDynamic*
	{
		UStaticMesh* M = C ? C->GetStaticMesh() : nullptr;
		UMaterialInterface* Base = M && M->GetStaticMaterials().Num() > 0 ? M->GetStaticMaterials()[0].MaterialInterface.Get() : nullptr;
		UMaterialInstanceDynamic* Mid = Base ? UMaterialInstanceDynamic::Create(Base, this) : nullptr;
		if (Mid)
			C->SetMaterial(0, Mid);
		return Mid;
	};
	BallMid = Tint(Ball);
	RingMid = Tint(Shock);
	Light->SetAttenuationRadius(Size * 6.0f);
	Apply();
}

void ARtsExplosionActor::Apply()
{
	const FFrame F = FrameAt(Elapsed);
	// basic shapes are 100 UU across: scale = diameter / 100
	const float D = Size * 2.0f / 100.0f;
	Ball->SetRelativeScale3D(FVector(D * F.Fireball * 0.8f, D * F.Fireball * 0.8f, D * F.Fireball * 0.6f));
	Ball->SetRelativeLocation(FVector(0.0f, 0.0f, Size * 0.25f + Size * 0.8f * F.Rise));
	Ball->SetVisibility(F.Fireball > 0.01f);
	if (BallMid)
	{
		// white-hot to warm white to smoke grey (brightness, not hue, tells the stage)
		const FLinearColor Hot(1.0f, 0.97f, 0.9f), Warm(0.95f, 0.82f, 0.62f), Smoke(0.22f, 0.21f, 0.2f);
		const FLinearColor C = F.Heat > 0.5f ? FMath::Lerp(Warm, Hot, (F.Heat - 0.5f) * 2.0f) : FMath::Lerp(Smoke, Warm, F.Heat * 2.0f);
		BallMid->SetVectorParameterValue(TEXT("Color"), C * (1.0f + 6.0f * F.Heat));
	}
	Shock->SetRelativeScale3D(FVector(D * F.Ring, D * F.Ring, 0.02f));
	Shock->SetRelativeLocation(FVector(0.0f, 0.0f, 6.0f));
	Shock->SetVisibility(F.RingAlpha > 0.02f);
	if (RingMid)
		RingMid->SetVectorParameterValue(TEXT("Color"), FLinearColor(0.9f, 0.9f, 0.88f) * F.RingAlpha);
	Light->SetIntensity(F.Flash * 50000.0f * FMath::Clamp(Size / 100.0f, 0.5f, 4.0f));
}

void ARtsExplosionActor::Tick(float DeltaSeconds)
{
	Super::Tick(DeltaSeconds);
	Elapsed += DeltaSeconds;
	if (Elapsed >= LifeSeconds)
	{
		Destroy();
		return;
	}
	Apply();
}
