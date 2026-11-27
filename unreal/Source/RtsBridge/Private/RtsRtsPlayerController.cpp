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
#include "GameFramework/PlayerInput.h"
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
	// T20: F4 toggles every debug overlay (stats, paths, targets, ids + state). The editor
	// swallows F1-F3 in PIE (help/view modes) and F8/F11 are taken, so one reliable key.
	ReleaseEngineDebugFKeys();
	PIC->BindKey(EKeys::F4, IE_Pressed, this, &ARtsRtsPlayerController::OnDebugKey);
	// T36: E deploys the selected MCV (free key: not WASD, F, X or the group digits).
	PIC->BindKey(EKeys::E, IE_Pressed, this, &ARtsRtsPlayerController::OnDeployKey);
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
	ReleaseEngineDebugFKeys(); // PlayerInput may be (re)built after SetupInputComponent
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
		bool bQueued = bAttack ? GM->RequestAttackMoveTo(P) : GM->RequestMoveTo(P);
		if (!bAttack)
			bQueued |= GM->RequestRallySelected(P); // T37: selected buildings take it as their rally point
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
			// T28: engineers capture what they can; everyone else attacks
			const bool bQueued = GM->RequestCaptureOrAttack(Enemy);
			if (bQueued && Hud)
				Hud->AddUnitMarker(Enemy, EMarker::Attack);
			if (bQueued)
				return true;
			// nothing here can act on it (a neutral derrick with no engineer): an ordinary move
		}
	}
	FHitResult Hit;
	if (!GetHitResultUnderCursor(ECC_Visibility, false, Hit))
		return false;
	// T26: a bridge under the cursor: engineers mend it; Ctrl+RMB or an attack-move click shoots it.
	if (GM->RequestBridgeAt(Hit.Location, bAttack || IsCtrlDown()))
	{
		if (Hud)
			Hud->AddGroundMarker(FVector2D(Hit.Location.X, Hit.Location.Y), EMarker::Attack);
		return true;
	}
	// T38: RMB on a vein with harvesters selected sends them there (the rest just move).
	if (!bAttack && GM->RequestHarvestAt(Hit.Location))
	{
		if (Hud)
			Hud->AddGroundMarker(FVector2D(Hit.Location.X, Hit.Location.Y), EMarker::Move);
		return true;
	}
	return PointOrder(Hit.Location);
}

void ARtsRtsPlayerController::OnRightClick()
{
	if (MatchScreenUp())
		return;
	if (!PlacingBuildingId.IsEmpty())
	{
		SetPlacing(FString()); // RMB cancels placement
		return;
	}
	if (HandleProductionClick(/*bRight=*/true))
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
	if (HandleBuildMenuClick())
		return;
	if (HandleProductionClick(/*bRight=*/false))
		return;
	if (!PlacingBuildingId.IsEmpty())
	{
		FVector2D MW;
		if (MouseOnMinimap(MW))
		{
			if (RtsCamera)
				RtsCamera->JumpTo(MW); // look elsewhere, stay in placement
			return;
		}
		TryPlaceAtCursor();
		return;
	}
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
	if (GM->IsFactionPickUp())
	{
		GM->ChooseFaction(ARtsMinimapHud::FactionPickHit(FVector2D(VX, VY), FVector2D(MX, MY)));
		return true;
	}
	switch (ARtsMinimapHud::MatchScreenHit(FVector2D(VX, VY), FVector2D(MX, MY), GM->CanRestart()))
	{
	case ARtsMinimapHud::EMatchScreenButton::PlayAgain: GM->RestartMatch(); break;
	case ARtsMinimapHud::EMatchScreenButton::Quit: GM->QuitMatch(); break;
	default: break;
	}
	return true;
}

void ARtsRtsPlayerController::OnDebugKey()
{
	constexpr int32 All = ARtsBridgeGameMode::DebugStats | ARtsBridgeGameMode::DebugPaths
		| ARtsBridgeGameMode::DebugTargets | ARtsBridgeGameMode::DebugLabels;
	if (ARtsBridgeGameMode* GM = GetWorld()->GetAuthGameMode<ARtsBridgeGameMode>())
		GM->SetDebugMask(GM->GetDebugMask() == 0 ? All : 0);
}

void ARtsRtsPlayerController::ReleaseEngineDebugFKeys()
{
	if (!PlayerInput)
		return;
	// Engine dev binding F4 = "viewmode lit_detaillighting" fires next to our overlay toggle
	// and greys out the view, so remove it for this player only.
	const int32 Removed = PlayerInput->DebugExecBindings.RemoveAll([](const FKeyBind& B) { return B.Key == EKeys::F4; });
	if (Removed > 0)
		UE_LOG(LogTemp, Display, TEXT("RTS_INPUT: released %d engine debug F-key bindings for the overlays"), Removed);
}

// ---- T36: build sidebar and placement ------------------------------------------------------

bool ARtsRtsPlayerController::CursorCell(FIntPoint& OutCell) const
{
	FHitResult Hit;
	if (!GetHitResultUnderCursor(ECC_Visibility, false, Hit))
		return false;
	const int32 X = FMath::FloorToInt(Hit.Location.X / ARtsBridgeGameMode::CellUU);
	const int32 Y = FMath::FloorToInt(Hit.Location.Y / ARtsBridgeGameMode::CellUU);
	if (X < 0 || Y < 0 || X >= ARtsBridgeGameMode::MapCellsW || Y >= ARtsBridgeGameMode::MapCellsH)
		return false;
	OutCell = FIntPoint(X, Y);
	return true;
}

bool ARtsRtsPlayerController::GetPlacement(FString& OutBuildingId, FIntPoint& OutCell) const
{
	if (PlacingBuildingId.IsEmpty())
		return false;
	OutBuildingId = PlacingBuildingId;
	FVector2D W;
	if (MouseOnMinimap(W) || !CursorCell(OutCell))
		OutCell = FIntPoint(INT32_MIN, INT32_MIN);
	return true;
}

void ARtsRtsPlayerController::SetPlacing(const FString& BuildingId)
{
	PlacingBuildingId = BuildingId;
	if (!BuildingId.IsEmpty())
		SetAttackMoveArmed(false);
	CurrentMouseCursor = BuildingId.IsEmpty() ? EMouseCursor::Default : EMouseCursor::Hand;
}

bool ARtsRtsPlayerController::HandleBuildMenuClick()
{
	ARtsBridgeGameMode* GM = GetWorld()->GetAuthGameMode<ARtsBridgeGameMode>();
	float MX, MY;
	if (!GM || !GetMousePosition(MX, MY))
		return false;
	int32 N = 0;
	bool bDeploy = false;
	if (!ARtsMinimapHud::BuildMenuShape(*GM, N, bDeploy))
		return false;
	int32 VX = 0, VY = 0;
	GetViewportSize(VX, VY);
	const int32 Hit = ARtsMinimapHud::BuildMenuHit(FVector2D(VX, VY), N, bDeploy, FVector2D(MX, MY));
	if (Hit == ARtsMinimapHud::BuildMenuNone)
		return false;
	bLeftDownOnView = false;
	if (Hit == ARtsMinimapHud::BuildMenuDeploy)
	{
		if (!GM->SelectionHasMcv())
			GM->SelectOwnMcvs();
		GM->RequestDeploySelected();
	}
	else if (Hit >= 0)
	{
		const TArray<ARtsBridgeGameMode::FBuildOption>& Opts = GM->GetBuildOptions();
		if (Opts.IsValidIndex(Hit) && Opts[Hit].IsEnabled())
			SetPlacing(Opts[Hit].BuildingId == PlacingBuildingId ? FString() : Opts[Hit].BuildingId);
	}
	return true; // a click on the panel never falls through to the map
}

void ARtsRtsPlayerController::TryPlaceAtCursor()
{
	ARtsBridgeGameMode* GM = GetWorld()->GetAuthGameMode<ARtsBridgeGameMode>();
	FIntPoint Cell;
	if (!GM || !CursorCell(Cell))
		return;
	ARtsBridgeGameMode::FPlaceAnswer A;
	if (!GM->QueryPlace(PlacingBuildingId, Cell.X, Cell.Y, A) || !A.bAllowed)
		return; // the ghost already shows why; stay in placement
	if (GM->RequestPlace(PlacingBuildingId, Cell.X, Cell.Y) && !IsShiftDown())
		SetPlacing(FString());
}

void ARtsRtsPlayerController::OnDeployKey()
{
	if (ARtsBridgeGameMode* GM = GetWorld()->GetAuthGameMode<ARtsBridgeGameMode>())
		GM->RequestDeploySelected();
}

// ---- T37: production panel -----------------------------------------------------------------

bool ARtsRtsPlayerController::HandleProductionClick(bool bRight)
{
	ARtsBridgeGameMode* GM = GetWorld()->GetAuthGameMode<ARtsBridgeGameMode>();
	float MX, MY;
	if (!GM || !GetMousePosition(MX, MY))
		return false;
	const int32 ProducerId = GM->GetSelectedProducer();
	const FRtsUnitSnapshot* B = ProducerId != INDEX_NONE ? GM->GetUnitRow(ProducerId) : nullptr;
	if (!B)
		return false;
	const TArray<FRtsUnitType> Types = GM->TrainableAt(B->BuildingId);
	int32 VX = 0, VY = 0;
	GetViewportSize(VX, VY);
	int32 Index = INDEX_NONE;
	const ARtsMinimapHud::EProductionHit Hit = ARtsMinimapHud::ProductionHit(FVector2D(VX, VY), Types.Num(), FVector2D(MX, MY), Index);
	if (Hit == ARtsMinimapHud::EProductionHit::None)
		return false;
	bLeftDownOnView = false;
	if (Hit == ARtsMinimapHud::EProductionHit::Unit && Types.IsValidIndex(Index))
	{
		if (bRight)
			GM->RequestCancelLastOf(ProducerId, Types[Index].Id);
		else
			GM->RequestTrain(ProducerId, Types[Index].Id, IsShiftDown() ? 5 : 1);
	}
	else if (Hit == ARtsMinimapHud::EProductionHit::Queue && bRight && B->ProductionQueue.IsValidIndex(Index))
		GM->RequestCancel(ProducerId, Index);
	return true; // the panel swallows the click
}
