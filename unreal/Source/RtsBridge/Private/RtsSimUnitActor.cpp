#include "RtsSimUnitActor.h"
#include "UObject/ConstructorHelpers.h"
#include "Components/StaticMeshComponent.h"
#include "Materials/MaterialInstanceDynamic.h"

// Design scale: 100 UU per map cell (docs/superpowers/specs/2026-09-26-unreal-rts-migration-design.md).
static constexpr float SimCellUU = 100.0f;

ARtsSimUnitActor::ARtsSimUnitActor()
{
	PrimaryActorTick.bCanEverTick = true; // interpolation between snapshots
	PrimaryActorTick.bStartWithTickEnabled = true;
	Mesh = CreateDefaultSubobject<UStaticMeshComponent>(TEXT("Mesh"));
	Mesh->SetMobility(EComponentMobility::Movable); // moves every snapshot; tint set at runtime
	RootComponent = Mesh;
	// Engine-shipped placeholder cube; no project content needed.
	static ConstructorHelpers::FObjectFinder<UStaticMesh> CubeFinder(
		TEXT("/Engine/BasicShapes/Cube.Cube"));
	if (CubeFinder.Succeeded())
	{
		Mesh->SetStaticMesh(CubeFinder.Object);
		Mesh->SetRelativeScale3D(FVector(0.6f));
	}

	// Flat cylinder = selection ring; hidden until selected.
	static ConstructorHelpers::FObjectFinder<UStaticMesh> CylFinder(
		TEXT("/Engine/BasicShapes/Cylinder.Cylinder"));
	Ring = CreateDefaultSubobject<UStaticMeshComponent>(TEXT("Ring"));
	Ring->SetupAttachment(RootComponent);
	Ring->SetRelativeLocation(FVector(0.0f, 0.0f, -49.0f));
	Ring->SetRelativeScale3D(FVector(1.4f, 1.4f, 0.05f));
	Ring->SetCastShadow(false);
	// T38: harvester cargo, a gold block on top, sized by the load (hidden when empty).
	Cargo = CreateDefaultSubobject<UStaticMeshComponent>(TEXT("Cargo"));
	Cargo->SetupAttachment(RootComponent);
	Cargo->SetRelativeLocation(FVector(0.0f, 0.0f, 62.0f));
	Cargo->SetCollisionEnabled(ECollisionEnabled::NoCollision);
	Cargo->SetCastShadow(false);
	Cargo->SetVisibility(false);
	if (CubeFinder.Succeeded())
		Cargo->SetStaticMesh(CubeFinder.Object);
	Ring->SetVisibility(false);
	if (CylFinder.Succeeded())
	{
		Ring->SetStaticMesh(CylFinder.Object);
		// BasicShapeMaterial exposes a "Color" scalar parameter (verified in the asset).
		UMaterialInterface* Base = nullptr;
		if (CylFinder.Object && CylFinder.Object->GetStaticMaterials().Num() > 0)
			Base = CylFinder.Object->GetStaticMaterials()[0].MaterialInterface;
		if (Base)
		{
			if (UMaterialInstanceDynamic* RingMid = UMaterialInstanceDynamic::Create(Base, this))
			{
				RingMid->SetVectorParameterValue(TEXT("Color"), FLinearColor(0.2f, 1.0f, 0.3f));
				Ring->SetMaterial(0, RingMid);
			}
		}
	}
}

static FVector SimToWorld(double X, double Y, double Z)
{
	// Sim map X -> world X, map Y -> world Y, height stays on the ground plane (slice 4).
	return FVector(X * SimCellUU, Y * SimCellUU, Z);
}

void ARtsSimUnitActor::SnapToSimPosition(double X, double Y)
{
	InterpFrom = InterpTo = SimToWorld(X, Y, GroundZ);
	InterpElapsed = SimTickSeconds;
	SetActorLocation(InterpTo);
}

void ARtsSimUnitActor::SetSimPosition(double X, double Y)
{
	// Start the glide from where the unit is drawn now (not the previous snapshot), so
	// two snapshots in one frame (a hitch) never make it jump back.
	InterpFrom = GetActorLocation();
	InterpTo = SimToWorld(X, Y, GroundZ);
	InterpElapsed = 0.0f;
	const FVector D = InterpTo - InterpFrom;
	if (D.SizeSquared2D() > 1.0) // ~1 UU per tick: actually moving, face the motion
		TargetYaw = FMath::RadiansToDegrees(FMath::Atan2(D.Y, D.X));
}

void ARtsSimUnitActor::Tick(float DeltaSeconds)
{
	Super::Tick(DeltaSeconds);
	if (IsDying())
	{
		TickDeath(DeltaSeconds);
		return;
	}
	InterpElapsed = FMath::Min(InterpElapsed + DeltaSeconds, SimTickSeconds);
	const float Alpha = InterpElapsed / SimTickSeconds;
	const FRotator Cur = GetActorRotation();
	const FRotator Want(0.0f, TargetYaw, 0.0f);
	SetActorLocationAndRotation(FMath::Lerp(InterpFrom, InterpTo, Alpha),
		FMath::RInterpTo(Cur, Want, DeltaSeconds, TurnSpeed));
}

FLinearColor ARtsSimUnitActor::FactionColor(int32 InFaction)
{
	static const FLinearColor Palette[] = {
		FLinearColor(0.85f, 0.25f, 0.20f), // faction 0 — red
		FLinearColor(0.20f, 0.45f, 0.90f), // faction 1 — blue
		FLinearColor(0.85f, 0.75f, 0.20f), // faction 2 — gold
	};
	return Palette[FMath::Clamp(InFaction, 0, 2)];
}

void ARtsSimUnitActor::SetFaction(int32 InFaction)
{
	Faction = InFaction;
	if (!Mid)
	{
		// Build the tint from the base shape material directly (runtime load), not from
		// whatever GetMaterial(0) reports, then apply it to every slot of the cube.
		UMaterialInterface* Base = LoadObject<UMaterialInterface>(nullptr,
			TEXT("/Engine/BasicShapes/BasicShapeMaterial.BasicShapeMaterial"));
		if (!Base)
			Base = Mesh->GetMaterial(0);
		if (!Base)
			return;
		Mid = UMaterialInstanceDynamic::Create(Base, this);
		for (int32 i = 0; i < FMath::Max(1, Mesh->GetNumMaterials()); ++i)
			Mesh->SetMaterial(i, Mid);
	}
	Mid->SetVectorParameterValue(TEXT("Color"), FactionColor(Faction));
}

void ARtsSimUnitActor::SetSelected(bool bSelected)
{
	Ring->SetVisibility(bSelected);
}

void ARtsSimUnitActor::BeginDeath()
{
	if (IsDying())
		return;
	DeathElapsed = 0.0f;
	DeathFrom = GetActorLocation();
	Hp = 0.0;
	Ring->SetVisibility(false);
	Cargo->SetVisibility(false);
	SetActorEnableCollision(false); // never picked or box-selected again
	if (Mid)
		Mid->SetVectorParameterValue(TEXT("Color"), FactionColor(Faction) * 0.25f);
}

void ARtsSimUnitActor::TickDeath(float DeltaSeconds)
{
	DeathElapsed += DeltaSeconds;
	// Topple onto its side, then hold, then sink fully below the ground plane.
	const float Topple = FMath::Clamp(DeathElapsed / ToppleSeconds, 0.0f, 1.0f);
	const float SinkT = FMath::Clamp((DeathElapsed - ToppleSeconds - CorpseSeconds) / SinkSeconds, 0.0f, 1.0f);
	const FRotator Rot(80.0f * FMath::InterpEaseOut(0.0f, 1.0f, Topple, 2.0f), GetActorRotation().Yaw, 0.0f);
	const FVector Loc = DeathFrom - FVector(0.0, 0.0, 20.0 * Topple + 90.0 * SinkT);
	SetActorLocationAndRotation(Loc, Rot);
	if (SinkT >= 1.0f)
		Destroy();
}

void ARtsSimUnitActor::SetBuildingShape(int32 W, int32 H, float Progress)
{
	// T36: a building is its footprint (slightly inset), rising from a slab to full height as
	// construction runs, darker until it is finished. Presentation only.
	const float P = FMath::Clamp(Progress, 0.0f, 1.0f);
	const int32 Key = W * 100000 + H * 1000 + FMath::RoundToInt(P * 100.0f);
	if (Key == ShapeKey)
		return;
	ShapeKey = Key;
	bIsBuilding = true;
	const float Height = FMath::Lerp(0.12f, 1.1f, P); // cube is 100 UU: 0.12 = slab, 1.1 = 110 UU
	Mesh->SetRelativeScale3D(FVector(W * 0.92f, H * 0.92f, Height));
	GroundZ = Height * 50.0f; // cube pivot is its centre: sit it on the ground
	FVector L = GetActorLocation();
	L.Z = GroundZ;
	InterpFrom.Z = InterpTo.Z = GroundZ;
	SetActorLocation(L);
	// The ring scales with the root; keep it a thin band just outside the footprint.
	Ring->SetRelativeScale3D(FVector(1.15f, 1.15f, 0.05f / FMath::Max(Height, 0.05f) * 0.6f));
	if (Mid)
	{
		const FLinearColor C = FactionColor(Faction);
		const float Shade = P >= 1.0f ? 1.0f : FMath::Lerp(0.25f, 0.6f, P);
		Mid->SetVectorParameterValue(TEXT("Color"), FLinearColor(C.R * Shade, C.G * Shade, C.B * Shade));
	}
}

void ARtsSimUnitActor::SetCargo(float Fraction, bool bCarrying)
{
	// T38: the load as a gold block on the roof; brighter once full and heading home.
	const float F = FMath::Clamp(Fraction, 0.0f, 1.0f);
	if (F <= 0.0f || IsDying())
	{
		Cargo->SetVisibility(false);
		return;
	}
	if (!CargoMid)
		if (UMaterialInterface* Base = LoadObject<UMaterialInterface>(nullptr, TEXT("/Engine/BasicShapes/BasicShapeMaterial.BasicShapeMaterial")))
			if ((CargoMid = UMaterialInstanceDynamic::Create(Base, this)) != nullptr)
				Cargo->SetMaterial(0, CargoMid);
	if (CargoMid)
		CargoMid->SetVectorParameterValue(TEXT("Color"), bCarrying ? FLinearColor(1.0f, 0.8f, 0.15f) : FLinearColor(0.6f, 0.45f, 0.1f));
	const float S = 0.3f + 0.5f * F; // relative to the unit cube
	Cargo->SetRelativeScale3D(FVector(S, S, 0.25f + 0.35f * F));
	Cargo->SetVisibility(true);
}
