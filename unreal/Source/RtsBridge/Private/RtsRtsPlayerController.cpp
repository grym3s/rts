#include "RtsRtsPlayerController.h"
#include "RtsRtsCameraActor.h"
#include "RtsBridgeGameMode.h"
#include "Engine/World.h"
#include "Engine/EngineTypes.h"
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
		SetViewTarget(RtsCamera);
		bEnableMouseOverEvents = true;
	}

	PIC->BindAxis(TEXT("MoveForward"), this, &ARtsRtsPlayerController::OnMoveUp);
	PIC->BindAxis(TEXT("MoveRight"), this, &ARtsRtsPlayerController::OnMoveRight);
	PIC->BindAxis(TEXT("CamZoom"), this, &ARtsRtsPlayerController::OnZoom);
	PIC->BindAxis(TEXT("CamRotate"), this, &ARtsRtsPlayerController::OnRotate);

	PIC->BindKey(EKeys::LeftMouseButton, IE_Pressed, this, &ARtsRtsPlayerController::OnLeftClick);
	PIC->BindKey(EKeys::RightMouseButton, IE_Pressed, this, &ARtsRtsPlayerController::OnRightClick);
	PIC->BindKey(EKeys::A, IE_Pressed, this, &ARtsRtsPlayerController::OnSelectAll);
}

void ARtsRtsPlayerController::OnMoveUp(float V)   { ScrollY = V; }
void ARtsRtsPlayerController::OnMoveRight(float V){ ScrollX = V; }
void ARtsRtsPlayerController::OnRotate(float V)   { RotDir = V; }
void ARtsRtsPlayerController::OnZoom(float V)
{
	if (RtsCamera)
		RtsCamera->Zoom(V);
}

void ARtsRtsPlayerController::Tick(float DeltaSeconds)
{
	Super::Tick(DeltaSeconds);
	if (RtsCamera)
	{
		if (!FMath::IsNearlyZero(ScrollX) || !FMath::IsNearlyZero(ScrollY))
			RtsCamera->Scroll(FVector(ScrollY, ScrollX, 0.0f));
		if (!FMath::IsNearlyZero(RotDir))
			RtsCamera->RotateYaw(RotDir * 90.0f * DeltaSeconds);
	}
}

void ARtsRtsPlayerController::OnRightClick()
{
	ARtsBridgeGameMode* GM = GetWorld()->GetAuthGameMode<ARtsBridgeGameMode>();
	if (!GM || !GM->IsHostAlive())
		return; // no local fallback: a dead host issues nothing
	FHitResult Hit;
	if (GetHitResultUnderCursor(ECC_Visibility, false, Hit))
		GM->RequestMoveTo(Hit.Location);
}

void ARtsRtsPlayerController::OnLeftClick()
{
	if (ARtsBridgeGameMode* GM = GetWorld()->GetAuthGameMode<ARtsBridgeGameMode>())
	{
		FVector2D Screen;
		float MX, MY;
		if (GetMousePosition(MX, MY))
		{
			Screen = FVector2D(MX, MY);
			GM->ToggleSelectUnderCursor(Screen);
		}
	}
}

void ARtsRtsPlayerController::OnSelectAll()
{
	if (ARtsBridgeGameMode* GM = GetWorld()->GetAuthGameMode<ARtsBridgeGameMode>())
		GM->SelectAll();
}
