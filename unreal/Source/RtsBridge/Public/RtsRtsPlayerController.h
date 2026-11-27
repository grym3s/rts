// Slice 4 input, StarCraft-style. Cursor always visible.
//   WASD / arrows / screen-edge: scroll   wheel: zoom   (fixed north-up camera, no rotation)
//   LMB: click = select under cursor, drag = box select (Shift adds/toggles);
//        on the minimap, jump/drag the camera
//   RMB: move selected units (ground point, or the minimap point); on an enemy = attack it
//   F then LMB: attack-move (ground/minimap) or attack (enemy); RMB cancels (Esc = stop PIE)
//   X: stop   Ctrl+A: select all   (A/S belong to WASD, hence F/X)
//   Ctrl+1..3: assign control group   1..3: recall (double-tap: centre camera on it)
//   Match over (T19): result screen; only its Play again / Quit buttons take clicks
//   F4: debug overlays on/off (stats, paths, targets, unit ids + state; T20, read-only)
//   T36 build sidebar (right): LMB an entry = placement mode (ghost follows the cursor, green/red
//        with the reason); LMB ground places (Shift keeps placing); RMB cancels.
//   E: deploy the selected MCV (the sidebar Deploy button does the same)
//   T37 production panel (bottom centre, selected building): LMB unit = train (Shift x5), RMB unit
//        or queue slot = cancel; RMB ground with the building selected = rally point
// Keys are read directly (no config axis mappings), so editor rewrites of
// Config/DefaultInput.ini cannot silently unbind the camera.
// Everything funnels through the game mode; the controller never touches sim state.
#pragma once

#include "CoreMinimal.h"
#include "GameFramework/PlayerController.h"
#include "RtsMinimapHud.h"
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
	/** Re-read the map bounds for the camera clamp (after the game mode loads the map). */
	void RefreshMapBounds();

	/** Screen-space drag box (viewport px) while a box select is in progress, for the HUD. */
	bool GetSelectionBox(FVector2D& OutA, FVector2D& OutB) const;

	/** T36: true while placing; OutCell = cell under the cursor (INT32_MIN,INT32_MIN when off-map). */
	bool GetPlacement(FString& OutBuildingId, FIntPoint& OutCell) const;

private:
	UPROPERTY()
	TObjectPtr<ARtsRtsCameraActor> RtsCamera;

	void OnZoomIn();
	void OnZoomOut();
	void OnRightClick();
	void OnLeftClick();
	void OnLeftRelease();
	void OnSelectAll();
	void OnGroup1() { OnGroupKey(0); }
	void OnGroup2() { OnGroupKey(1); }
	void OnGroup3() { OnGroupKey(2); }
	void OnGroupKey(int32 Group);
	void OnAttackMoveKey();
	void OnStopKey();
	void OnDebugKey();
	void OnDeployKey();
	// ability keys: Q phase, C conscript, U unload, T teleport (then click a node), G fire (then click the ground), F6 fog
	void OnPhaseKey();
	void OnConscriptKey();
	void OnUnloadKey();
	void OnTeleportKey();
	void OnFireKey();
	void OnFogKey();
	void RunAbility(ARtsMinimapHud::EAbility Ability);
	/** LMB on the ability bar; true = the click was the bar's. */
	bool HandleAbilityClick();
	/** Waiting for a click: a Teleport Node to send the selection to, or a superweapon target. */
public:
	enum class ETargeting : uint8 { None, Teleport, Fire };
private:
	ETargeting Targeting = ETargeting::None;
	void SetTargeting(ETargeting T);
	/** LMB while targeting: true = handled (sent, or kept waiting on a bad click). */
	bool HandleTargetingClick();
public:
	ETargeting GetTargeting() const { return Targeting; }
private:
	/** T36: LMB on the build sidebar (pick an entry, Deploy); true = the click was the sidebar's. */
	bool HandleBuildMenuClick();
	/** T37: LMB a unit = train (Shift x5); RMB a unit = cancel its last, RMB a queue slot = cancel it. */
	bool HandleProductionClick(bool bRight);
	/** T36: LMB on the ground while placing: queue it if the sim says yes. */
	void TryPlaceAtCursor();
	void SetPlacing(const FString& BuildingId);
	bool CursorCell(FIntPoint& OutCell) const;
	FString PlacingBuildingId;
	/** Dev builds bind F4 to an engine view mode (BaseInput DebugExecBindings); drop it
	 *  so the T20 overlay key is ours alone. No-op in shipping (no debug bindings). */
	void ReleaseEngineDebugFKeys();
	void SetAttackMoveArmed(bool bArmed);
	/** Order the selection at the cursor: enemy under it = attack, else (attack-)move to
	 *  the ground/minimap point. */
	bool IssueOrderAtCursor(bool bAttack);
	/** T19: true while the match result screen is up (orders are refused). */
	bool MatchScreenUp() const;
	/** The HUD's canvas size (what its panels were laid out in); the window size before the first frame. */
	FVector2D HudViewport() const;
	/** Left click while the result screen is up: run its button, swallow the click. */
	bool HandleMatchScreenClick();

	bool IsShiftDown() const;
	bool IsCtrlDown() const;
	float KeyAxis(FKey Positive, FKey PositiveAlt, FKey Negative, FKey NegativeAlt) const;
	ARtsMinimapHud* MinimapHud() const;
	/** Mouse position on the minimap -> world ground point. */
	bool MouseOnMinimap(FVector2D& OutWorldXY) const;

	bool bDraggingMinimap = false;
	bool bAttackMoveArmed = false; // F pressed: next LMB issues attack-move (crosshair cursor)
	// Left button held on the 3D view: a click until it moves past the box threshold.
	bool bLeftDownOnView = false;
	FVector2D DragStart = FVector2D::ZeroVector;
	// Double-tap detection for group recall.
	int32 LastGroupTapped = INDEX_NONE;
	double LastGroupTapTime = 0.0;
	static constexpr double DoubleTapSeconds = 0.35;
	static constexpr float EdgeScrollPx = 8.0f;
};
