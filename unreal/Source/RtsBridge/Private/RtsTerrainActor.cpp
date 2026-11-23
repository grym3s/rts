#include "RtsTerrainActor.h"
#include "Components/InstancedStaticMeshComponent.h"
#include "Components/StaticMeshComponent.h"
#include "Materials/MaterialInstanceDynamic.h"
#include "Engine/StaticMesh.h"

// Heights (UU). Land tops out at Z=0, where units stand (unit cubes sit at Z=50).
static constexpr float LandTop = 0.0f, LandThick = 20.0f;
static constexpr float CliffTop = 80.0f;
static constexpr float BridgeTop = 2.0f, BridgeThick = 10.0f;
static constexpr float WaterZ = -30.0f;

ARtsTerrainActor::ARtsTerrainActor()
{
	PrimaryActorTick.bCanEverTick = false;
	RootComponent = CreateDefaultSubobject<USceneComponent>(TEXT("Root"));
	RootComponent->SetMobility(EComponentMobility::Movable); // runtime-built: must be Movable to render
	Water = CreateDefaultSubobject<UStaticMeshComponent>(TEXT("Water"));
	Water->SetupAttachment(RootComponent);
	Water->SetMobility(EComponentMobility::Movable);
	Land = MakeLayer(TEXT("Land"));
	Cliff = MakeLayer(TEXT("Cliff"));
	Bridge = MakeLayer(TEXT("Bridge"));
}

UInstancedStaticMeshComponent* ARtsTerrainActor::MakeLayer(const TCHAR* Name)
{
	UInstancedStaticMeshComponent* C = CreateDefaultSubobject<UInstancedStaticMeshComponent>(Name);
	C->SetupAttachment(RootComponent);
	C->SetMobility(EComponentMobility::Movable);
	C->SetCollisionEnabled(ECollisionEnabled::QueryOnly); // ground traces for orders
	C->SetCollisionResponseToAllChannels(ECR_Block);
	return C;
}

FLinearColor ARtsTerrainActor::TileColor(TCHAR Tile)
{
	switch (Tile)
	{
	case TEXT('.'): return FLinearColor(0.035f, 0.07f, 0.035f);  // land: dark green (units read on it)
	case TEXT('#'): return FLinearColor(0.16f, 0.14f, 0.12f);    // cliff: grey-brown
	case TEXT('='): return FLinearColor(0.20f, 0.12f, 0.05f);    // bridge: timber
	default:        return FLinearColor(0.02f, 0.06f, 0.14f);    // water: deep blue
	}
}

void ARtsTerrainActor::Tint(UPrimitiveComponent* C, const FLinearColor& Color)
{
	UMaterialInterface* Base = LoadObject<UMaterialInterface>(nullptr,
		TEXT("/Engine/BasicShapes/BasicShapeMaterial.BasicShapeMaterial"));
	if (!Base)
		return;
	if (UMaterialInstanceDynamic* Mid = UMaterialInstanceDynamic::Create(Base, this))
	{
		Mid->SetVectorParameterValue(TEXT("Color"), Color);
		C->SetMaterial(0, Mid);
	}
}

void ARtsTerrainActor::Build(const TArray<FString>& Rows, float CellUU)
{
	const int32 H = Rows.Num();
	const int32 W = H > 0 ? Rows[0].Len() : 0;
	if (W == 0)
		return;

	UStaticMesh* Cube = LoadObject<UStaticMesh>(nullptr, TEXT("/Engine/BasicShapes/Cube.Cube"));   // 100 UU
	UStaticMesh* Plane = LoadObject<UStaticMesh>(nullptr, TEXT("/Engine/BasicShapes/Plane.Plane")); // 100 UU
	if (!Cube || !Plane)
		return;

	// Water: one plane under everything (also the trace target for clicks on open sea).
	Water->SetStaticMesh(Plane);
	Water->SetWorldLocation(FVector(W * 0.5 * CellUU, H * 0.5 * CellUU, WaterZ));
	Water->SetWorldScale3D(FVector(W * CellUU / 100.0, H * CellUU / 100.0, 1.0));
	Water->SetCollisionEnabled(ECollisionEnabled::QueryOnly);
	Water->SetCollisionResponseToAllChannels(ECR_Block);
	Tint(Water, TileColor(TEXT('~')));

	struct FLayer { TCHAR Tile; UInstancedStaticMeshComponent* C; float Top; float Thick; };
	const FLayer TileLayers[] = {
		{ TEXT('.'), Land, LandTop, LandThick },
		{ TEXT('#'), Cliff, CliffTop, CliffTop - WaterZ },
		{ TEXT('='), Bridge, BridgeTop, BridgeThick },
	};
	for (const FLayer& L : TileLayers)
	{
		L.C->ClearInstances();
		L.C->SetStaticMesh(Cube);
		Tint(L.C, TileColor(L.Tile));
		// Merge row runs, then grow identical runs downward: a few hundred boxes, not 15k.
		TMap<uint64, FIntRect> Open; // key = (x0<<32)|x1 -> rect (Min inclusive, Max inclusive)
		TArray<FIntRect> Done;
		for (int32 Y = 0; Y < H; ++Y)
		{
			TMap<uint64, FIntRect> Next;
			for (int32 X = 0; X < W;)
			{
				if (Rows[Y][X] != L.Tile) { ++X; continue; }
				int32 X1 = X;
				while (X1 + 1 < W && Rows[Y][X1 + 1] == L.Tile)
					++X1;
				const uint64 Key = (uint64(X) << 32) | uint64(X1);
				FIntRect R;
				if (Open.RemoveAndCopyValue(Key, R))
					R.Max.Y = Y;
				else
					R = FIntRect(X, Y, X1, Y);
				Next.Add(Key, R);
				X = X1 + 1;
			}
			for (const TPair<uint64, FIntRect>& P : Open)
				Done.Add(P.Value);
			Open = MoveTemp(Next);
		}
		for (const TPair<uint64, FIntRect>& P : Open)
			Done.Add(P.Value);

		for (const FIntRect& R : Done)
		{
			const double SX = (R.Max.X - R.Min.X + 1), SY = (R.Max.Y - R.Min.Y + 1);
			const FVector Center((R.Min.X + SX * 0.5) * CellUU, (R.Min.Y + SY * 0.5) * CellUU, L.Top - L.Thick * 0.5);
			L.C->AddInstance(FTransform(FRotator::ZeroRotator, Center,
				FVector(SX * CellUU / 100.0, SY * CellUU / 100.0, L.Thick / 100.0)), /*bWorldSpace=*/true);
		}
	}
}
