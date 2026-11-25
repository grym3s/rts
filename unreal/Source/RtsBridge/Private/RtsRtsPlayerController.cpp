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
#include "RtsSelection.h"
#include "HAL/PlatformTime.h"

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
	// Plain bindings; the handler reads Ctrl itself (assign vs recall).
	PIC->BindKey(EKeys::One, IE_Pressed, this, &ARtsRtsPlayerController::OnGroup1);
	PIC->BindKey(EKeys::Two, IE_Pressed, this, &ARtsRtsPlayerController::OnGroup2);
	PIC->BindKey(EKeys::Three, IE_Pressed, this, &ARtsRtsPlayerController::OnGroup3);
	// A and S are camera keys (WASD), so attack-move is F and stop is X.
	PIC->BindKey(EKeys::F, IE_Pressed, this, &ARtsRtsPlayerController::OnAttackMoveKey);
	PIC->BindKey(EKeys::X, IE_Pressed, this, &ARtsRtsPlayerController::OnStopKey);
}

bool ARtsRtsPlayerController::IsShiftDown() const
{
	return IsInputKeyDown(EKeys::LeftShift) || IsInputKeyDown(EKeys::RightShift);
}

bool ARtsRtsPlayerController::IsCtrlDown() const
{
	return IsInputKeyDown(EKeys::LeftControl) || IsInputKeyDown(EKeys::RightControl);
}

bool ARtsRtsPlayerController::GetSelectionBox(FVector2D& OutA, FVector2D& OutB) const
{
	float MX, MY;
	if (!bLeftDownOnView || !GetMousePosition(MX, MY))
		return false;
	const FVector2D Cur(MX, MY);
	if (FVector2D::Distance(Cur, DragStart) < FRtsSelection::BoxDragThreshold)
		return false;
	OutA = DragStart;
	OutB = Cur;
	return true;
}

void ARtsRtsPlayerController::OnGroupKey(int32 Group)
{
	ARtsBridgeGameMode* GM = GetWorld()->GetAuthGameMode<ARtsBridgeGameMode>();
	if (!GM)
		return;
	if (IsCtrlDown())
	{
		GM->AssignGroup(Group);
		LastGroupTapped = INDEX_NONE;
		return;
	}
	if (!GM->RecallGroup(Group))
		return;
	const double Now = FPlatformTime::Seconds();
	FVector2D C;
	if (LastGroupTapped == Group && Now - LastGroupTapTime <= DoubleTapSeconds && GM->SelectionCentroid(C))
		CenterCameraOn(C);
	LastGroupTapped = Group;
	LastGroupTapTime = Now;
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

void ARtsRtsPlayerController::RefreshMapBounds()
{
	if (RtsCamera)
		RtsCamera->SetBounds(ARtsBridgeGameMode::MapWorldBounds());
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

	const bool bCtrl = IsCtrlDown();
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

void ARtsRtsPlayerController::SetAttackMoveArmed(bool bArmed)
{
	bAttackMoveArmed = bArmed;
	CurrentMouseCursor = bArmed ? EMouseCursor::Crosshairs : EMouseCursor::Default;
}

void ARtsRtsPlayerController::OnAttackMoveKey()
{
	const ARtsBridgeGameMode* GM = GetWorld()->GetAuthGameMode<ARtsBridgeGameMode>();
	if (GM && GM->HasSelection() && !GM->IsMatchScreenUp())
		SetAttackMoveArmed(true);
}

void ARtsRtsPlayerController::OnStopKey()
{
	SetAttackMoveArmed(false);
	ARtsBridgeGameMode* GM = GetWorld()->GetAuthGameMode<ARtsBridgeGameMode>();
	if (!GM || !GM->RequestStop())
		return;
	if (ARtsMinimapHud* Hud = MinimapHud())
		for (const int32 Id : GM->GetSelectedIds())
			Hud->AddUnitMarker(Id, ARtsMinimapHud::EOrderMarker::Stop);
}

bool ARtsRtsPlayerController::IssueOrderAtCursor(bool bAttack)
{
	using EMarker = ARtsMinimapHud::EOrderMarker;
	ARtsBridgeGameMode* GM = GetWorld()->GetAuthGameMode<ARtsBridgeGameMode>();
	if (!GM || !GM->IsHostAlive())
		return false; // no local fallback: a dead host issues nothing
	ARtsMinimapHud* Hud = MinimapHud();
	// Marker only when the order was actually queued (selection non-empty, host alive).
	auto PointOrder = [&](const FVector& P)
	{
		const bool bQueued = bAttack ? GM->RequestAttackMoveTo(P) : GM->RequestMoveTo(P);
		if (bQueued && Hud)
			Hud->AddGroundMarker(FVector2D(P.X, P.Y), bAttack ? EMarker::AttackMove : EMarker::Move);
		return bQueued;
	};
	FVector2D W;
	if (MouseOnMinimap(W))
		return PointOrder(FVector(W.X, W.Y, 0.0));
	// An enemy under the cursor is a direct attack, for both RMB and attack-move clicks.
	float MX, MY;
	if (GetMousePosition(MX, MY))
	{
		const int32 Enemy = GM->PickEnemyUnitAt(FVector2D(MX, MY));
		if (Enemy != INDEX_NONE)
		{
			const bool bQueued = GM->RequestAttackUnit(Enemy);
			if (bQueued && Hud)
				Hud->AddUnitMarker(Enemy, EMarker::Attack);
			return bQueued;
		}
	}
	FHitResult Hit;
	if (!GetHitResultUnderCursor(ECC_Visibility, false, Hit))
		return false;
	return PointOrder(Hit.Location);
}

void ARtsRtsPlayerController::OnRightClick()
{
	if (MatchScreenUp())
		return;
	if (bAttackMoveArmed)
	{
		SetAttackMoveArmed(false); // RMB cancels a pending attack-move
		return;
	}
	IssueOrderAtCursor(/*bAttack=*/false);
}

void ARtsRtsPlayerController::OnLeftClick()
{
	if (HandleMatchScreenClick())
		return;
	if (bAttackMoveArmed)
	{
		IssueOrderAtCursor(/*bAttack=*/true);
		SetAttackMoveArmed(false);
		return;
	}
	FVector2D W;
	if (MouseOnMinimap(W))
	{
		bDraggingMinimap = true;
		if (RtsCamera)
			RtsCamera->JumpTo(W);
		return;
	}
	// Click or box is decided on release, by how far the mouse moved.
	float MX, MY;
	if (GetMousePosition(MX, MY))
	{
		bLeftDownOnView = true;
		DragStart = FVector2D(MX, MY);
	}
}

void ARtsRtsPlayerController::OnLeftRelease()
{
	bDraggingMinimap = false;
	if (!bLeftDownOnView)
		return;
	bLeftDownOnView = false;
	ARtsBridgeGameMode* GM = GetWorld()->GetAuthGameMode<ARtsBridgeGameMode>();
	float MX, MY;
	if (!GM || !GetMousePosition(MX, MY))
		return;
	const FVector2D End(MX, MY);
	if (FVector2D::Distance(End, DragStart) < FRtsSelection::BoxDragThreshold)
		GM->ClickSelect(DragStart, IsShiftDown());
	else
		GM->BoxSelect(DragStart, End, IsShiftDown());
}

void ARtsRtsPlayerController::OnSelectAll()
{
	if (ARtsBridgeGameMode* GM = GetWorld()->GetAuthGameMode<ARtsBridgeGameMode>())
		GM->SelectAll();
}

bool ARtsRtsPlayerController::MatchScreenUp() const
{
	const ARtsBridgeGameMode* GM = GetWorld()->GetAuthGameMode<ARtsBridgeGameMode>();
	return GM && GM->IsMatchScreenUp();
}

bool ARtsRtsPlayerController::HandleMatchScreenClick()
{
	ARtsBridgeGameMode* GM = GetWorld()->GetAuthGameMode<ARtsBridgeGameMode>();
	if (!GM || !GM->IsMatchScreenUp())
		return false;
	// T19: the result screen swallows every left click; only its buttons act.
	SetAttackMoveArmed(false);
	bLeftDownOnView = false;
	float MX, MY;
	int32 VX = 0, VY = 0;
	GetViewportSize(VX, VY);
	if (!GetMousePosition(MX, MY))
		return true;
	switch (ARtsMinimapHud::MatchScreenHit(FVector2D(VX, VY), FVector2D(MX, MY), GM->CanRestart()))
	{
	case ARtsMinimapHud::EMatchScreenButton::PlayAgain: GM->RestartMatch(); break;
	case ARtsMinimapHud::EMatchScreenButton::Quit: GM->QuitMatch(); break;
	default: break;
	}
	return true;
}
