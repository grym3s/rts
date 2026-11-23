// Minimap (presentation only), drawn with the HUD canvas so it needs no UMG assets:
// map field, one dot per unit in its faction colour (selected = white outline), and the
// camera's view footprint. The controller asks it to turn minimap clicks into world points.
#pragma once

#include "CoreMinimal.h"
#include "GameFramework/HUD.h"
#include "RtsMinimapHud.generated.h"

UCLASS()
class RTSBRIDGE_API ARtsMinimapHud : public AHUD
{
	GENERATED_BODY()

public:
	virtual void DrawHUD() override;

	/** If ScreenPos (viewport px) is on the minimap, return the world ground point under it. */
	bool MinimapToWorld(const FVector2D& ScreenPos, FVector2D& OutWorldXY) const;

private:
	/** Minimap rectangle in viewport pixels for a viewport of ViewportSize (bottom-left). */
	static FBox2D MinimapRect(const FVector2D& ViewportSize);
	FVector2D WorldToMinimap(const FBox2D& Rect, const FVector2D& WorldXY) const;
	FVector2D ViewportSize() const;
	/** Clip a world-XY polygon to a box (camera footprint vs map edges). */
	static TArray<FVector2D> ClipToBox(const TArray<FVector2D>& In, const FBox2D& Box);
};
