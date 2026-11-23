#include "RtsRtsPlayerController.h"
#include "RtsRtsCameraActor.h"
#include "RtsBridgeGameMode.h"
#include "RtsMinimapHud.h"
#include "Engine/World.h"
#include "Engine/EngineTypes.h"
#include "Engine/LocalPlayer.h"
#include "Engine/GameViewportClient.h"
#include "UnrealClient.h"
#include "Components/InputComponent.h"
#include "InputCoreTypes.h"

void ARtsRtsPlayerController::SetupInputComponent()
{
	Super::SetupInputComponent();
	UInputComponent* PIC = InputComponent;

	// Spawn the RTS camera and take over from the default one.
	FActorSpawnParameters SP;
	SP.Name = TEXT("RtsCamera");
	RtsCamera = GetWorld()->SpawnActor<ARtsRtsCameraActor>(FVector::ZeroVector, FRotator::ZeroRotator, SP);
	if (RtsCamera)
	{
		RtsCamera->SetBounds(ARtsBridgeGameMode::MapWorldBounds());
		bAutoManageActiveCameraTarget = false;
		SetViewTarget(RtsCamera);
		bEnableMouseOverEvents = true;
	}
	// RTS: the cursor stays visible for clicking units, ground, minimap and (later) UI.
	bShowMouseCursor = true;
	bEnableClickEvents = true;

	PIC->BindKey(EKeys::MouseScrollUp, IE_Pressed, this, &ARtsRtsPlayerController::OnZoomIn);
	PIC->BindKey(EKeys::MouseScrollDown, IE_Pressed, this, &ARtsRtsPlayerController::OnZoomOut);
	PIC->BindKey(EKeys::LeftMouseButton, IE_Pressed, this, &ARtsRtsPlayerController::OnLeftClick);
	PIC->BindKey(EKeys::LeftMouseButton, IE_Released, this, &ARtsRtsPlayerController::OnLeftRelease);
	PIC->BindKey(EKeys::RightMouseButton, IE_Pressed, this, &ARtsRtsPlayerController::OnRightClick);
	// Ctrl+A, not A: A is scroll-left.
	PIC->BindKey(FInputChord(EKeys::A, /*bShift=*/false, /*bCtrl=*/true, /*bAlt=*/false, /*bCmd=*/false),
		IE_Pressed, this, &ARtsRtsPlayerController::OnSelectAll);
}

void ARtsRtsPlayerController::BeginPlay()
{
	Super::BeginPlay();
	// No pawn: stop the engine resetting the view to the controller itself (at the origin,
	// looking along the ground = black screen), and point it at the RTS camera.
	bAutoManageActiveCameraTarget = false;
	if (RtsCamera)
		SetViewTarget(RtsCamera);
	// Game-only input mode captures and hides the mouse; an RTS needs a free, visible cursor.
	FInputModeGameAndUI Mode;
	Mode.SetHideCursorDuringCapture(false);
	Mode.SetLockMouseToViewportBehavior(EMouseLockMode::LockInFullscreen);
	SetInputMode(Mode);
	bShowMouseCursor = true;
}

void ARtsRtsPlayerController::CenterCameraOn(const FVector2D& WorldXY)
{
	if (RtsCamera)
		RtsCamera->JumpTo(WorldXY);
}

float ARtsRtsPlayerController::KeyAxis(FKey Positive, FKey PositiveAlt, FKey Negative, FKey NegativeAlt) const
{
	float V = 0.0f;
	if (IsInputKeyDown(Positive) || IsInputKeyDown(PositiveAlt)) V += 1.0f;
	if (IsInputKeyDown(Negative) || IsInputKeyDown(NegativeAlt)) V -= 1.0f;
	return V;
}

ARtsMinimapHud* ARtsRtsPlayerController::MinimapHud() const
{
	return Cast<ARtsMinimapHud>(GetHUD());
}

bool ARtsRtsPlayerController::MouseOnMinimap(FVector2D& OutWorldXY) const
{
	float MX, MY;
	const ARtsMinimapHud* Hud = MinimapHud();
	return Hud && GetMousePosition(MX, MY) && Hud->MinimapToWorld(FVector2D(MX, MY), OutWorldXY);
}

void ARtsRtsPlayerController::OnZoomIn()  { if (RtsCamera) RtsCamera->Zoom(+1.0f); }
void ARtsRtsPlayerController::OnZoomOut() { if (RtsCamera) RtsCamera->Zoom(-1.0f); }

void ARtsRtsPlayerController::Tick(float DeltaSeconds)
{
	Super::Tick(DeltaSeconds);
	if (!RtsCamera)
		return;

	// Dragging on the minimap keeps the view centred under the cursor.
	if (bDraggingMinimap)
	{
		FVector2D W;
		if (MouseOnMinimap(W))
			RtsCamera->JumpTo(W);
		return;
	}

	const bool bCtrl = IsInputKeyDown(EKeys::LeftControl) || IsInputKeyDown(EKeys::RightControl);
	float Up = KeyAxis(EKeys::W, EKeys::Up, EKeys::S, EKeys::Down);
	float Right = bCtrl ? 0.0f : KeyAxis(EKeys::D, EKeys::Right, EKeys::A, EKeys::Left);

	// Screen-edge scrolling (only while the cursor is inside the game viewport).
	float MX, MY;
	int32 VW = 0, VH = 0;
	GetViewportSize(VW, VH);
	const ULocalPlayer* LP = GetLocalPlayer();
	const bool bFocused = LP && LP->ViewportClient && LP->ViewportClient->Viewport
		&& LP->ViewportClient->Viewport->HasFocus();
	if (bFocused && GetMousePosition(MX, MY) && VW > 0 && VH > 0)
	{
		if (MX <= EdgeScrollPx) Right = -1.0f;
		else if (MX >= VW - 1 - EdgeScrollPx) Right = 1.0f;
		if (MY <= EdgeScrollPx) Up = 1.0f;
		else if (MY >= VH - 1 - EdgeScrollPx) Up = -1.0f;
	}

	if (!FMath::IsNearlyZero(Up) || !FMath::IsNearlyZero(Right))
		RtsCamera->Scroll(FVector(Up, Right, 0.0f));
}

void ARtsRtsPlayerController::OnRightClick()
{
	ARtsBridgeGameMode* GM = GetWorld()->GetAuthGameMode<ARtsBridgeGameMode>();
	if (!GM || !GM->IsHostAlive())
		return; // no local fallback: a dead host issues nothing
	FVector2D W;
	if (MouseOnMinimap(W))
	{
		GM->RequestMoveTo(FVector(W.X, W.Y, 0.0));
		return;
	}
	FHitResult Hit;
	if (GetHitResultUnderCursor(ECC_Visibility, false, Hit))
		GM->RequestMoveTo(Hit.Location);
}

void ARtsRtsPlayerController::OnLeftClick()
{
	FVector2D W;
	if (MouseOnMinimap(W))
	{
		bDraggingMinimap = true;
		if (RtsCamera)
			RtsCamera->JumpTo(W);
		return;
	}
	if (ARtsBridgeGameMode* GM = GetWorld()->GetAuthGameMode<ARtsBridgeGameMode>())
	{
		float MX, MY;
		if (GetMousePosition(MX, MY))
			GM->ToggleSelectUnderCursor(FVector2D(MX, MY));
	}
}

void ARtsRtsPlayerController::OnLeftRelease()
{
	bDraggingMinimap = false;
}

void ARtsRtsPlayerController::OnSelectAll()
{
	if (ARtsBridgeGameMode* GM = GetWorld()->GetAuthGameMode<ARtsBridgeGameMode>())
		GM->SelectAll();
}
