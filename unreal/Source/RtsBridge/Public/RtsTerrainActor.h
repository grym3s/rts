// Draws the typed tile map (presentation only): a water plane under the whole map, then
// land / cliff / bridge tiles as merged rectangles, one instanced-box component per tile
// type. Gives ground traces something to hit (RMB move on any tile). No sim knowledge —
// walkability is the host's; this only shows it.
#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "RtsTerrainActor.generated.h"

class UInstancedStaticMeshComponent;
class UStaticMeshComponent;

UCLASS()
class RTSBRIDGE_API ARtsTerrainActor : public AActor
{
	GENERATED_BODY()

public:
	ARtsTerrainActor();

	/** Rows: '.' land, '~' water, '#' cliff, '=' bridge (see RtsContentLoader). */
	void Build(const TArray<FString>& Rows, float CellUU);

	/** Shared tile palette (3D tiles and the minimap). */
	static FLinearColor TileColor(TCHAR Tile);

private:
	UPROPERTY() TObjectPtr<UStaticMeshComponent> Water;
	UPROPERTY() TObjectPtr<UInstancedStaticMeshComponent> Land;
	UPROPERTY() TObjectPtr<UInstancedStaticMeshComponent> Cliff;
	UPROPERTY() TObjectPtr<UInstancedStaticMeshComponent> Bridge;

	UInstancedStaticMeshComponent* MakeLayer(const TCHAR* Name);
	void Tint(UPrimitiveComponent* C, const FLinearColor& Color);
};
