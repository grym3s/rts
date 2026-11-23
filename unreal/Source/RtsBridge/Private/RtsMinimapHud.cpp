#include "RtsMinimapHud.h"
#include "RtsBridgeGameMode.h"
#include "RtsSimUnitActor.h"
#include "RtsRtsPlayerController.h"
#include "Engine/Canvas.h"
#include "Engine/World.h"
#include "GameFramework/PlayerController.h"

// Minimap orientation matches the camera (yaw -90): left->right = world +X, top->bottom = world +Y.

FVector2D ARtsMinimapHud::ViewportSize() const
{
	int32 W = 0, H = 0;
	if (PlayerOwner)
		PlayerOwner->GetViewportSize(W, H);
	return FVector2D(W, H);
}

FBox2D ARtsMinimapHud::MinimapRect(const FVector2D& Vp)
{
	const float Margin = 14.0f;
	const float Size = FMath::Clamp(FMath::Min(Vp.X, Vp.Y) * 0.28f, 140.0f, 300.0f);
	const float Aspect = float(ARtsBridgeGameMode::MapCellsH) / float(ARtsBridgeGameMode::MapCellsW);
	const float Wpx = Aspect <= 1.0f ? Size : Size / Aspect;
	const float Hpx = Aspect <= 1.0f ? Size * Aspect : Size;
	const FVector2D Min(Margin, Vp.Y - Margin - Hpx);
	return FBox2D(Min, Min + FVector2D(Wpx, Hpx));
}

FVector2D ARtsMinimapHud::WorldToMinimap(const FBox2D& Rect, const FVector2D& WorldXY) const
{
	const FBox2D Map = ARtsBridgeGameMode::MapWorldBounds();
	const FVector2D T = (WorldXY - Map.Min) / Map.GetSize();
	return Rect.Min + T * Rect.GetSize();
}

bool ARtsMinimapHud::MinimapToWorld(const FVector2D& ScreenPos, FVector2D& OutWorldXY) const
{
	const FVector2D Vp = ViewportSize();
	if (Vp.X <= 0 || Vp.Y <= 0)
		return false;
	const FBox2D Rect = MinimapRect(Vp);
	if (!Rect.IsInside(ScreenPos))
		return false;
	const FBox2D Map = ARtsBridgeGameMode::MapWorldBounds();
	const FVector2D T = (ScreenPos - Rect.Min) / Rect.GetSize();
	OutWorldXY = Map.Min + T * Map.GetSize();
	return true;
}

void ARtsMinimapHud::DrawHUD()
{
	Super::DrawHUD();
	if (!Canvas)
		return;
	const FVector2D Vp(Canvas->SizeX, Canvas->SizeY);

	// Box-select drag rectangle (screen space, under the minimap).
	if (const ARtsRtsPlayerController* RPC = Cast<ARtsRtsPlayerController>(PlayerOwner))
	{
		FVector2D A, B;
		if (RPC->GetSelectionBox(A, B))
		{
			const FVector2D Mn(FMath::Min(A.X, B.X), FMath::Min(A.Y, B.Y));
			const FVector2D Mx(FMath::Max(A.X, B.X), FMath::Max(A.Y, B.Y));
			const FLinearColor Edge(0.3f, 1.0f, 0.4f, 1.0f);
			DrawRect(FLinearColor(0.3f, 1.0f, 0.4f, 0.12f), Mn.X, Mn.Y, Mx.X - Mn.X, Mx.Y - Mn.Y);
			DrawLine(Mn.X, Mn.Y, Mx.X, Mn.Y, Edge, 1.5f);
			DrawLine(Mx.X, Mn.Y, Mx.X, Mx.Y, Edge, 1.5f);
			DrawLine(Mx.X, Mx.Y, Mn.X, Mx.Y, Edge, 1.5f);
			DrawLine(Mn.X, Mx.Y, Mn.X, Mn.Y, Edge, 1.5f);
		}
	}

	const FBox2D R = MinimapRect(Vp);
	const FVector2D Sz = R.GetSize();

	// Frame + field.
	DrawRect(FLinearColor(0.02f, 0.02f, 0.02f, 0.9f), R.Min.X - 3, R.Min.Y - 3, Sz.X + 6, Sz.Y + 6);
	DrawRect(FLinearColor(0.10f, 0.16f, 0.11f, 1.0f), R.Min.X, R.Min.Y, Sz.X, Sz.Y);

	// Units.
	if (const ARtsBridgeGameMode* GM = GetWorld() ? GetWorld()->GetAuthGameMode<ARtsBridgeGameMode>() : nullptr)
	{
		const float Dot = FMath::Max(3.0f, Sz.X / ARtsBridgeGameMode::MapCellsW * 0.45f);
		for (const ARtsSimUnitActor* U : GM->GetUnitActors())
		{
			if (!U)
				continue;
			const FVector L = U->GetActorLocation();
			const FVector2D P = WorldToMinimap(R, FVector2D(L.X, L.Y));
			if (GM->IsSelected(U->GetSimUnitId()))
				DrawRect(FLinearColor::White, P.X - Dot * 0.5f - 1.5f, P.Y - Dot * 0.5f - 1.5f, Dot + 3.0f, Dot + 3.0f);
			DrawRect(ARtsSimUnitActor::FactionColor(U->GetFaction()), P.X - Dot * 0.5f, P.Y - Dot * 0.5f, Dot, Dot);
		}
	}

	// Camera view footprint: viewport corners projected onto the ground plane (Z = 0),
	// then clipped to the map so the box is cut at the edge instead of squashed onto it.
	if (PlayerOwner)
	{
		const FVector2D Corners[4] = { {0, 0}, {Vp.X, 0}, {Vp.X, Vp.Y}, {0, Vp.Y} };
		TArray<FVector2D> Poly;
		for (int32 i = 0; i < 4; ++i)
		{
			FVector Origin, Dir;
			if (!PlayerOwner->DeprojectScreenPositionToWorld(Corners[i].X, Corners[i].Y, Origin, Dir))
				return;
			// A ray at or above the horizon never meets the ground: cap it at a long range.
			const double T = (Dir.Z < -1e-3) ? FMath::Min(-Origin.Z / Dir.Z, 50000.0) : 50000.0;
			const FVector Hit = Origin + Dir * T;
			Poly.Add(FVector2D(Hit.X, Hit.Y));
		}
		Poly = ClipToBox(Poly, ARtsBridgeGameMode::MapWorldBounds());
		for (int32 i = 0; i < Poly.Num(); ++i)
		{
			const FVector2D A = WorldToMinimap(R, Poly[i]);
			const FVector2D B = WorldToMinimap(R, Poly[(i + 1) % Poly.Num()]);
			DrawLine(A.X, A.Y, B.X, B.Y, FLinearColor::White, 1.5f);
		}
	}
}

TArray<FVector2D> ARtsMinimapHud::ClipToBox(const TArray<FVector2D>& In, const FBox2D& Box)
{
	// Sutherland–Hodgman against the four box edges.
	TArray<FVector2D> Out = In;
	for (int32 Edge = 0; Edge < 4 && Out.Num() > 0; ++Edge)
	{
		auto Inside = [&](const FVector2D& P)
		{
			switch (Edge)
			{
			case 0: return P.X >= Box.Min.X;
			case 1: return P.X <= Box.Max.X;
			case 2: return P.Y >= Box.Min.Y;
			default: return P.Y <= Box.Max.Y;
			}
		};
		auto Cross = [&](const FVector2D& A, const FVector2D& B)
		{
			const double Bound = Edge == 0 ? Box.Min.X : Edge == 1 ? Box.Max.X : Edge == 2 ? Box.Min.Y : Box.Max.Y;
			const bool bX = Edge < 2;
			const double DA = (bX ? A.X : A.Y) - Bound, DB = (bX ? B.X : B.Y) - Bound;
			return A + (B - A) * (DA / (DA - DB));
		};
		TArray<FVector2D> Src = MoveTemp(Out);
		Out.Reset();
		for (int32 i = 0; i < Src.Num(); ++i)
		{
			const FVector2D& Cur = Src[i];
			const FVector2D& Prev = Src[(i + Src.Num() - 1) % Src.Num()];
			if (Inside(Cur))
			{
				if (!Inside(Prev)) Out.Add(Cross(Prev, Cur));
				Out.Add(Cur);
			}
			else if (Inside(Prev))
				Out.Add(Cross(Prev, Cur));
		}
	}
	return Out;
}
