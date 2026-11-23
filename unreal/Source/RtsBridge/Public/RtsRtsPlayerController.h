// Slice 4 input, StarCraft-style. Cursor always visible.
//   WASD / arrows / screen-edge: scroll   wheel: zoom   (fixed north-up camera, no rotation)
//   LMB: select under cursor; on the minimap, jump/drag the camera
//   RMB: move selected units (ground point, or the minimap point)   Ctrl+A: select all
// Keys are read directly (no config axis mappings), so editor rewrites of
// Config/DefaultInput.ini cannot silently unbind the camera.
// Everything funnels through the game mode; the controller never touches sim state.
#pragma once

#include "CoreMinimal.h"
#include "GameFramework/PlayerController.h"
#include "RtsRtsPlayerController.generated.h"

class ARtsRtsCameraActor;
class ARtsMinimapHud;

UCLASS()
class RTSBRIDGE_API ARtsRtsPlayerController : public APlayerController
{
	GENERATED_BODY()

public:
	virtual void BeginPlay() override;
	virtual void SetupInputComponent() override;
	virtual void Tick(float DeltaSeconds) override;

	/** Centre the RTS camera on a world ground point (game mode uses it at match start). */
	void CenterCameraOn(const FVector2D& WorldXY);

private:
	UPROPERTY()
	TObjectPtr<ARtsRtsCameraActor> RtsCamera;

	void OnZoomIn();
	void OnZoomOut();
	void OnRightClick();
	void OnLeftClick();
	void OnLeftRelease();
	void OnSelectAll();

	float KeyAxis(FKey Positive, FKey PositiveAlt, FKey Negative, FKey NegativeAlt) const;
	ARtsMinimapHud* MinimapHud() const;
	/** Mouse position on the minimap -> world ground point. */
	bool MouseOnMinimap(FVector2D& OutWorldXY) const;

	bool bDraggingMinimap = false;
	static constexpr float EdgeScrollPx = 8.0f;
};
