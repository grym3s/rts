// Minimap (presentation only), drawn with the HUD canvas so it needs no UMG assets:
// map field, one dot per unit in its faction colour (selected = white outline), and the
// camera's view footprint. The controller asks it to turn minimap clicks into world points.
#pragma once

#include "CoreMinimal.h"
#include "GameFramework/HUD.h"
#include "RtsMinimapHud.generated.h"

class UTexture2D;

UCLASS()
class RTSBRIDGE_API ARtsMinimapHud : public AHUD
{
	GENERATED_BODY()

public:
	virtual void DrawHUD() override;

	/** If ScreenPos (viewport px) is on the minimap, return the world ground point under it. */
	bool MinimapToWorld(const FVector2D& ScreenPos, FVector2D& OutWorldXY) const;

	/** Order feedback (pillar 1: an acknowledgement on the same frame as the order). */
	enum class EOrderMarker : uint8 { Move, AttackMove, Attack, Stop };
	/** Ground marker at a world point (also shown on the minimap). */
	void AddGroundMarker(const FVector2D& WorldXY, EOrderMarker Kind);
	/** Marker that follows a unit (attack target, stopped units). */
	void AddUnitMarker(int32 SimUnitId, EOrderMarker Kind);

private:
	struct FOrderMarker
	{
		FVector2D WorldXY = FVector2D::ZeroVector;
		int32 UnitId = INDEX_NONE; // != INDEX_NONE: follows that unit
		EOrderMarker Kind = EOrderMarker::Move;
		double StartTime = 0.0;
	};
	TArray<FOrderMarker> Markers;
	/** Terrain image, one texel per cell, built once from the game mode's tile rows. */
	UPROPERTY() TObjectPtr<UTexture2D> TerrainTex;
	UTexture2D* GetTerrainTexture();
	static constexpr double MarkerSeconds = 0.6;
	static FLinearColor MarkerColor(EOrderMarker Kind);
	void DrawMarkers(const FBox2D& MinimapR);
	/** Ground-plane circle projected to the screen (skipped if any point is off-view). */
	void DrawGroundCircle(const FVector2D& CenterXY, float RadiusUU, const FLinearColor& Color, float Thickness);

	/** Minimap rectangle in viewport pixels for a viewport of ViewportSize (bottom-left). */
	static FBox2D MinimapRect(const FVector2D& ViewportSize);
	FVector2D WorldToMinimap(const FBox2D& Rect, const FVector2D& WorldXY) const;
	FVector2D ViewportSize() const;
	/** Clip a world-XY polygon to a box (camera footprint vs map edges). */
	static TArray<FVector2D> ClipToBox(const TArray<FVector2D>& In, const FBox2D& Box);
};
