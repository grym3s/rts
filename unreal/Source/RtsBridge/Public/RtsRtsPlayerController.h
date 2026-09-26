// Slice 4 input: WASD/arrows scroll, Q/E rotate, wheel zooms, right-click issues a
// move command for the current selection, left-click selects under cursor, A selects all.
// Everything funnels through the game mode; the controller never touches sim state.
#pragma once

#include "CoreMinimal.h"
#include "GameFramework/PlayerController.h"
#include "RtsRtsPlayerController.generated.h"

class ARtsRtsCameraActor;

UCLASS()
class RTSBRIDGE_API ARtsRtsPlayerController : public APlayerController
{
	GENERATED_BODY()

public:
	virtual void SetupInputComponent() override;
	virtual void Tick(float DeltaSeconds) override;

private:
	UPROPERTY()
	TObjectPtr<ARtsRtsCameraActor> RtsCamera;

	void OnMoveUp(float V);
	void OnMoveRight(float V);
	void OnZoom(float V);
	void OnRotate(float V);
	void OnRightClick();
	void OnLeftClick();
	void OnSelectAll();

	float ScrollX = 0.0f, ScrollY = 0.0f;
	float RotDir = 0.0f;
};
