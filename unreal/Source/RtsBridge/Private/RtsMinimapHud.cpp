#include "RtsMinimapHud.h"
#include "RtsBridgeGameMode.h"
#include "RtsSimUnitActor.h"
#include "RtsRtsPlayerController.h"
#include "RtsTerrainActor.h"
#include "Engine/Texture2D.h"
#include "Engine/Canvas.h"
#include "Engine/World.h"
#include "GameFramework/PlayerController.h"
#include "Engine/Engine.h"
#include "Engine/Font.h"

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

UTexture2D* ARtsMinimapHud::GetTerrainTexture()
{
	if (TerrainTex)
		return TerrainTex;
	const ARtsBridgeGameMode* GM = GetWorld() ? GetWorld()->GetAuthGameMode<ARtsBridgeGameMode>() : nullptr;
	if (!GM || GM->GetTerrainRows().Num() == 0)
		return nullptr;
	const TArray<FString>& Rows = GM->GetTerrainRows();
	const int32 H = Rows.Num(), W = Rows[0].Len();
	UTexture2D* Tex = UTexture2D::CreateTransient(W, H, PF_B8G8R8A8);
	if (!Tex)
		return nullptr;
	Tex->Filter = TF_Nearest; // crisp cells, no blur at minimap scale
	Tex->SRGB = true;
	FColor* Px = static_cast<FColor*>(Tex->GetPlatformData()->Mips[0].BulkData.Lock(LOCK_READ_WRITE));
	for (int32 Y = 0; Y < H; ++Y)
		for (int32 X = 0; X < W; ++X)
		{
			// Brighter than the 3D tiles so the small map reads at a glance.
			FLinearColor C = ARtsTerrainActor::TileColor(X < Rows[Y].Len() ? Rows[Y][X] : TEXT('~')) * 3.0f;
			C.A = 1.0f;
			Px[Y * W + X] = C.ToFColor(true);
		}
	Tex->GetPlatformData()->Mips[0].BulkData.Unlock();
	Tex->UpdateResource();
	TerrainTex = Tex;
	return TerrainTex;
}

FLinearColor ARtsMinimapHud::MarkerColor(EOrderMarker Kind)
{
	switch (Kind)
	{
	case EOrderMarker::Move:       return FLinearColor(0.3f, 1.0f, 0.4f, 1.0f);  // green
	case EOrderMarker::AttackMove: return FLinearColor(1.0f, 0.55f, 0.1f, 1.0f); // orange
	case EOrderMarker::Attack:     return FLinearColor(1.0f, 0.15f, 0.1f, 1.0f); // red
	default:                       return FLinearColor(0.9f, 0.9f, 0.9f, 1.0f);  // stop: white
	}
}

void ARtsMinimapHud::AddGroundMarker(const FVector2D& WorldXY, EOrderMarker Kind)
{
	FOrderMarker M;
	M.WorldXY = WorldXY;
	M.Kind = Kind;
	M.StartTime = GetWorld() ? GetWorld()->GetRealTimeSeconds() : 0.0;
	Markers.Add(M);
}

void ARtsMinimapHud::AddUnitMarker(int32 SimUnitId, EOrderMarker Kind)
{
	FOrderMarker M;
	M.UnitId = SimUnitId;
	M.Kind = Kind;
	M.StartTime = GetWorld() ? GetWorld()->GetRealTimeSeconds() : 0.0;
	Markers.Add(M);
}

void ARtsMinimapHud::DrawGroundCircle(const FVector2D& C, float RadiusUU, const FLinearColor& Color, float Thickness)
{
	if (!PlayerOwner)
		return;
	constexpr int32 Segs = 24;
	FVector2D Pts[Segs];
	for (int32 i = 0; i < Segs; ++i)
	{
		const float A = 2.0f * PI * i / Segs;
		const FVector W(C.X + RadiusUU * FMath::Cos(A), C.Y + RadiusUU * FMath::Sin(A), 2.0);
		if (!PlayerOwner->ProjectWorldLocationToScreen(W, Pts[i]))
			return;
	}
	for (int32 i = 0; i < Segs; ++i)
	{
		const FVector2D& P = Pts[i];
		const FVector2D& Q = Pts[(i + 1) % Segs];
		DrawLine(P.X, P.Y, Q.X, Q.Y, Color, Thickness);
	}
}

void ARtsMinimapHud::DrawMarkers(const FBox2D& MinimapR)
{
	const double Now = GetWorld() ? GetWorld()->GetRealTimeSeconds() : 0.0;
	Markers.RemoveAll([Now](const FOrderMarker& M) { return Now - M.StartTime > MarkerSeconds; });
	const ARtsBridgeGameMode* GM = GetWorld() ? GetWorld()->GetAuthGameMode<ARtsBridgeGameMode>() : nullptr;
	for (const FOrderMarker& M : Markers)
	{
		FVector2D XY = M.WorldXY;
		float UnitRadius = 0.0f;
		if (M.UnitId != INDEX_NONE)
		{
			const ARtsSimUnitActor* U = GM ? GM->FindUnitActor(M.UnitId) : nullptr;
			if (!U)
				continue; // target gone (died): nothing to point at
			XY = FVector2D(U->GetActorLocation().X, U->GetActorLocation().Y);
			UnitRadius = 55.0f;
		}
		const float T = float((Now - M.StartTime) / MarkerSeconds); // 0..1
		FLinearColor Col = MarkerColor(M.Kind);
		Col.A = 1.0f - T;
		if (UnitRadius > 0.0f)
		{
			// Ring around the unit, pulsing in from slightly larger.
			DrawGroundCircle(XY, UnitRadius * (1.4f - 0.4f * T), Col, 2.5f);
		}
		else
		{
			// Ground ping: ring contracts to the point, inner dot stays.
			DrawGroundCircle(XY, FMath::Lerp(70.0f, 15.0f, T), Col, 2.5f);
			DrawGroundCircle(XY, 8.0f, Col, 2.0f);
			// Same ping on the minimap, so minimap orders are acknowledged where you look.
			const FVector2D P = WorldToMinimap(MinimapR, XY);
			const float R = FMath::Lerp(9.0f, 3.0f, T);
			DrawLine(P.X - R, P.Y, P.X + R, P.Y, Col, 2.0f);
			DrawLine(P.X, P.Y - R, P.X, P.Y + R, Col, 2.0f);
		}
	}
}

void ARtsMinimapHud::DrawHUD()
{
	Super::DrawHUD();
	if (!Canvas)
		return;
	const FVector2D Vp(Canvas->SizeX, Canvas->SizeY);
	DrawTracers();
	DrawHealthBars();
	DrawDebugOverlays();
	DrawEconomyBar();

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
	if (UTexture2D* Tex = GetTerrainTexture())
		DrawTexture(Tex, R.Min.X, R.Min.Y, Sz.X, Sz.Y, 0.0f, 0.0f, 1.0f, 1.0f);
	else
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

	DrawMarkers(R);

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
			{
				Poly.Reset(); // no footprint this frame (the result screen below still draws)
				break;
			}
			// A ray at or above the horizon never meets the ground: cap it at a long range.
			const double T = (Dir.Z < -1e-3) ? FMath::Min(-Origin.Z / Dir.Z, 50000.0) : 50000.0;
			const FVector Hit = Origin + Dir * T;
			Poly.Add(FVector2D(Hit.X, Hit.Y));
		}
		if (Poly.Num() == 4)
			Poly = ClipToBox(Poly, ARtsBridgeGameMode::MapWorldBounds());
		for (int32 i = 0; i < Poly.Num(); ++i)
		{
			const FVector2D A = WorldToMinimap(R, Poly[i]);
			const FVector2D B = WorldToMinimap(R, Poly[(i + 1) % Poly.Num()]);
			DrawLine(A.X, A.Y, B.X, B.Y, FLinearColor::White, 1.5f);
		}
	}

	DrawMatchScreen(); // T19: last, over everything
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

void ARtsMinimapHud::DrawHealthBars()
{
	const ARtsBridgeGameMode* GM = GetWorld() ? GetWorld()->GetAuthGameMode<ARtsBridgeGameMode>() : nullptr;
	if (!GM)
		return;
	static constexpr float W = 30.0f, H = 4.0f, AboveUU = 70.0f;
	for (const ARtsSimUnitActor* U : GM->GetUnitActors())
	{
		if (!U || U->IsDying())
			continue;
		const bool bSelected = GM->IsSelected(U->GetSimUnitId());
		if (!U->IsDamaged() && !bSelected)
			continue; // full-health, unselected units stay uncluttered
		const FVector S = Project(U->GetActorLocation() + FVector(0.0, 0.0, AboveUU));
		if (S.Z <= 0.0) // behind the camera
			continue;
		const float F = U->GetHealthFraction();
		// Green above half, through yellow, to red when nearly dead.
		const FLinearColor Fill = F > 0.5f
			? FMath::Lerp(FLinearColor(1.0f, 0.85f, 0.1f), FLinearColor(0.2f, 0.9f, 0.25f), (F - 0.5f) * 2.0f)
			: FMath::Lerp(FLinearColor(0.9f, 0.1f, 0.1f), FLinearColor(1.0f, 0.85f, 0.1f), F * 2.0f);
		const float X = S.X - W * 0.5f, Y = S.Y - H * 0.5f;
		DrawRect(FLinearColor(0.0f, 0.0f, 0.0f, 0.75f), X - 1.0f, Y - 1.0f, W + 2.0f, H + 2.0f);
		DrawRect(Fill, X, Y, W * F, H);
	}
}

void ARtsMinimapHud::AddTracer(const FVector& From, const FVector& To, int32 ShooterFaction)
{
	if (Tracers.Num() >= MaxTracers)
		Tracers.RemoveAt(0, 1, EAllowShrinking::No);
	FTracer T;
	// Gun height, and start/end at the cube's edge rather than its centre.
	const FVector Up(0.0, 0.0, 15.0);
	const FVector Dir = (To - From).GetSafeNormal2D();
	T.From = From + Up + Dir * 30.0;
	T.To = To + Up - Dir * 20.0;
	// Warm white tinted toward the shooter's faction: readable on any terrain.
	T.Color = FMath::Lerp(FLinearColor(1.0f, 0.95f, 0.8f), ARtsSimUnitActor::FactionColor(ShooterFaction), 0.45f);
	T.StartTime = GetWorld() ? GetWorld()->GetRealTimeSeconds() : 0.0;
	Tracers.Add(T);
}

void ARtsMinimapHud::DrawTracers()
{
	if (!PlayerOwner)
		return;
	const double Now = GetWorld() ? GetWorld()->GetRealTimeSeconds() : 0.0;
	Tracers.RemoveAll([Now](const FTracer& T) { return Now - T.StartTime > TracerSeconds; });
	for (const FTracer& T : Tracers)
	{
		FVector2D A, B;
		if (!PlayerOwner->ProjectWorldLocationToScreen(T.From, A) || !PlayerOwner->ProjectWorldLocationToScreen(T.To, B))
			continue;
		const float Age = float((Now - T.StartTime) / TracerSeconds); // 0..1
		// Faint full line: who is shooting whom, fading out.
		FLinearColor Line = T.Color;
		Line.A = 0.55f * (1.0f - Age);
		DrawLine(A.X, A.Y, B.X, B.Y, Line, 1.0f);
		// Bright streak that travels shooter -> target in the first 40% of the life.
		const float Travel = FMath::Clamp(Age / 0.4f, 0.0f, 1.0f);
		if (Travel < 1.0f)
		{
			const FVector2D Head = FMath::Lerp(A, B, Travel);
			const FVector2D Tail = FMath::Lerp(A, B, FMath::Max(0.0f, Travel - 0.35f));
			DrawLine(Tail.X, Tail.Y, Head.X, Head.Y, T.Color, 2.5f);
		}
		// Muzzle flash at the shooter (first quarter of the life).
		if (Age < 0.25f)
		{
			const float S = 7.0f * (1.0f - Age * 4.0f) + 2.0f;
			DrawRect(FLinearColor(1.0f, 0.9f, 0.5f, 0.9f), A.X - S * 0.5f, A.Y - S * 0.5f, S, S);
		}
		// Hit spark on the target once the streak arrives.
		if (Travel >= 1.0f)
		{
			const float HitAge = (Age - 0.4f) / 0.6f; // 0..1
			const float S = 3.0f + 6.0f * HitAge;
			FLinearColor Spark(1.0f, 0.6f, 0.2f, 1.0f - HitAge);
			DrawLine(B.X - S, B.Y, B.X + S, B.Y, Spark, 2.0f);
			DrawLine(B.X, B.Y - S, B.X, B.Y + S, Spark, 2.0f);
		}
	}
}

ARtsMinimapHud::FMatchScreenLayout ARtsMinimapHud::MatchScreenLayout(const FVector2D& Viewport, bool bCanRestart)
{
	FMatchScreenLayout L;
	const FVector2D PanelSize(FMath::Min(460.0, Viewport.X - 32.0), 300.0);
	const FVector2D C = Viewport * 0.5;
	L.Panel = FBox2D(C - PanelSize * 0.5, C + PanelSize * 0.5);
	const FVector2D Btn(160.0, 44.0);
	const double BtnY = L.Panel.Max.Y - 28.0 - Btn.Y;
	const double Gap = 20.0;
	if (bCanRestart)
	{
		L.PlayAgain = FBox2D(FVector2D(C.X - Gap * 0.5 - Btn.X, BtnY), FVector2D(C.X - Gap * 0.5, BtnY + Btn.Y));
		L.Quit = FBox2D(FVector2D(C.X + Gap * 0.5, BtnY), FVector2D(C.X + Gap * 0.5 + Btn.X, BtnY + Btn.Y));
	}
	else
	{
		L.PlayAgain = FBox2D(ForceInit); // not offered
		L.Quit = FBox2D(FVector2D(C.X - Btn.X * 0.5, BtnY), FVector2D(C.X + Btn.X * 0.5, BtnY + Btn.Y));
	}
	return L;
}

ARtsMinimapHud::EMatchScreenButton ARtsMinimapHud::MatchScreenHit(const FVector2D& Viewport, const FVector2D& Pos, bool bCanRestart)
{
	const FMatchScreenLayout L = MatchScreenLayout(Viewport, bCanRestart);
	if (L.PlayAgain.bIsValid && L.PlayAgain.IsInside(Pos))
		return EMatchScreenButton::PlayAgain;
	if (L.Quit.IsInside(Pos))
		return EMatchScreenButton::Quit;
	return EMatchScreenButton::None;
}

void ARtsMinimapHud::DrawCentredText(const FString& Text, UFont* Font, float CentreX, float Y, float Scale, const FLinearColor& Color)
{
	float W = 0.0f, H = 0.0f;
	GetTextSize(Text, W, H, Font, Scale);
	DrawText(Text, Color, CentreX - W * 0.5f, Y, Font, Scale);
}

void ARtsMinimapHud::DrawButton(const FBox2D& R, const FString& Label, bool bHover, float Alpha)
{
	const FVector2D S = R.GetSize();
	const FLinearColor Fill = bHover ? FLinearColor(0.30f, 0.34f, 0.40f, 0.95f * Alpha) : FLinearColor(0.16f, 0.18f, 0.22f, 0.95f * Alpha);
	const FLinearColor Edge(0.85f, 0.85f, 0.85f, (bHover ? 1.0f : 0.5f) * Alpha);
	DrawRect(Fill, R.Min.X, R.Min.Y, S.X, S.Y);
	DrawLine(R.Min.X, R.Min.Y, R.Max.X, R.Min.Y, Edge, 1.5f);
	DrawLine(R.Max.X, R.Min.Y, R.Max.X, R.Max.Y, Edge, 1.5f);
	DrawLine(R.Max.X, R.Max.Y, R.Min.X, R.Max.Y, Edge, 1.5f);
	DrawLine(R.Min.X, R.Max.Y, R.Min.X, R.Min.Y, Edge, 1.5f);
	UFont* Font = GEngine ? GEngine->GetMediumFont() : nullptr;
	float W = 0.0f, H = 0.0f;
	GetTextSize(Label, W, H, Font, 1.0f);
	DrawText(Label, FLinearColor(1, 1, 1, Alpha), R.GetCenter().X - W * 0.5f, R.GetCenter().Y - H * 0.5f, Font, 1.0f);
}

void ARtsMinimapHud::DrawMatchScreen()
{
	const ARtsBridgeGameMode* GM = GetWorld() ? GetWorld()->GetAuthGameMode<ARtsBridgeGameMode>() : nullptr;
	if (!GM || !GM->IsMatchScreenUp() || !Canvas)
		return;
	const float A = GM->MatchScreenAlpha();
	const FVector2D Vp(Canvas->SizeX, Canvas->SizeY);
	const bool bCanRestart = GM->CanRestart();
	const FMatchScreenLayout L = MatchScreenLayout(Vp, bCanRestart);

	// Dim the battlefield (it keeps running underneath), then the panel.
	DrawRect(FLinearColor(0.0f, 0.0f, 0.0f, 0.45f * A), 0.0f, 0.0f, Vp.X, Vp.Y);
	const FVector2D PS = L.Panel.GetSize();
	DrawRect(FLinearColor(0.05f, 0.06f, 0.08f, 0.92f * A), L.Panel.Min.X, L.Panel.Min.Y, PS.X, PS.Y);

	const int32 Winner = GM->GetMatchWinner();
	const FString Title = Winner == 0 ? TEXT("VICTORY") : Winner < 0 ? TEXT("DRAW") : TEXT("DEFEAT");
	const FLinearColor TitleColor = Winner == 0 ? FLinearColor(0.35f, 0.95f, 0.45f, A)
		: Winner < 0 ? FLinearColor(0.9f, 0.85f, 0.4f, A) : FLinearColor(0.95f, 0.3f, 0.25f, A);
	DrawRect(FLinearColor(TitleColor.R, TitleColor.G, TitleColor.B, A), L.Panel.Min.X, L.Panel.Min.Y, PS.X, 4.0f);
	const float Cx = L.Panel.GetCenter().X;
	UFont* Large = GEngine ? GEngine->GetLargeFont() : nullptr;
	UFont* Medium = GEngine ? GEngine->GetMediumFont() : nullptr;
	DrawCentredText(Title, Large, Cx, L.Panel.Min.Y + 26.0f, 2.2f, TitleColor);

	// Match time from the sim's end tick (20 t/s), and each side's losses.
	const int64 Secs = FMath::Max<int64>(0, GM->GetMatchEndTick()) / 20;
	const FLinearColor Body(0.85f, 0.85f, 0.85f, A);
	DrawCentredText(FString::Printf(TEXT("Match time  %lld:%02lld"), Secs / 60, Secs % 60), Medium, Cx, L.Panel.Min.Y + 104.0f, 1.0f, Body);
	DrawCentredText(FString::Printf(TEXT("Your forces lost  %d of %d"), GM->GetLost(0), GM->GetFielded(0)), Medium, Cx, L.Panel.Min.Y + 134.0f, 1.0f, Body);
	DrawCentredText(FString::Printf(TEXT("Enemy forces destroyed  %d of %d"), GM->GetLost(1), GM->GetFielded(1)), Medium, Cx, L.Panel.Min.Y + 160.0f, 1.0f, Body);

	FVector2D Mouse(-1, -1);
	if (PlayerOwner)
	{
		float MX, MY;
		if (PlayerOwner->GetMousePosition(MX, MY))
			Mouse = FVector2D(MX, MY);
	}
	if (L.PlayAgain.bIsValid)
		DrawButton(L.PlayAgain, TEXT("Play again"), L.PlayAgain.IsInside(Mouse), A);
	DrawButton(L.Quit, TEXT("Quit"), L.Quit.IsInside(Mouse), A);
}

void ARtsMinimapHud::DrawDebugOverlays()
{
	const ARtsBridgeGameMode* GM = GetWorld() ? GetWorld()->GetAuthGameMode<ARtsBridgeGameMode>() : nullptr;
	if (!GM || !PlayerOwner || !Canvas)
		return;
	const float Dt = GetWorld()->GetDeltaSeconds();
	if (Dt > 0.0f)
		FpsAvg = FpsAvg <= 0.0 ? 1.0 / Dt : FMath::Lerp(FpsAvg, 1.0 / Dt, 0.05);

	// Paths and targets: the selection, or everyone when nothing is selected.
	const TSet<int32>& Sel = GM->GetSelectedIds();
	auto Shown = [&Sel](int32 Id) { return Sel.Num() == 0 || Sel.Contains(Id); };
	auto Proj = [this](const FVector& W, FVector2D& Out) { return PlayerOwner->ProjectWorldLocationToScreen(W, Out); };
	UFont* Small = GEngine ? GEngine->GetSmallFont() : nullptr;

	int32 PerFaction[3] = {0, 0, 0};
	for (const ARtsSimUnitActor* U : GM->GetUnitActors())
	{
		if (!U)
			continue;
		PerFaction[FMath::Clamp(U->GetFaction(), 0, 2)]++;
		const FRtsDebugUnit* D = GM->GetDebugUnit(U->GetSimUnitId());
		if (!D)
			continue;
		const FVector At = U->GetActorLocation();

		if (GM->IsDebugOn(ARtsBridgeGameMode::DebugPaths) && Shown(D->Id) && D->Path.Num() > 0)
		{
			const FLinearColor PathCol(0.2f, 0.85f, 1.0f, 0.85f);
			FVector2D Prev;
			bool bPrev = Proj(FVector(At.X, At.Y, 5.0), Prev);
			for (const FVector2D& P : D->Path)
			{
				FVector2D S;
				const bool bS = Proj(FVector(P.X * ARtsBridgeGameMode::CellUU, P.Y * ARtsBridgeGameMode::CellUU, 5.0), S);
				if (bS && bPrev)
					DrawLine(Prev.X, Prev.Y, S.X, S.Y, PathCol, 1.5f);
				if (bS)
					DrawRect(PathCol, S.X - 2.0f, S.Y - 2.0f, 4.0f, 4.0f);
				Prev = S;
				bPrev = bS;
			}
		}

		if (GM->IsDebugOn(ARtsBridgeGameMode::DebugTargets) && Shown(D->Id) && D->Target != INDEX_NONE)
			if (const ARtsSimUnitActor* T = GM->FindUnitActor(D->Target))
			{
				FVector2D A, B;
				if (Proj(At, A) && Proj(T->GetActorLocation(), B))
				{
					DrawLine(A.X, A.Y, B.X, B.Y, FLinearColor(1.0f, 0.35f, 0.2f, 0.9f), 1.5f);
					DrawRect(FLinearColor(1.0f, 0.35f, 0.2f, 0.9f), B.X - 3.0f, B.Y - 3.0f, 6.0f, 6.0f);
				}
			}

		if (GM->IsDebugOn(ARtsBridgeGameMode::DebugLabels))
		{
			FVector2D S;
			if (Proj(At - FVector(0.0, 0.0, 45.0), S))
			{
				const FString Label = FString::Printf(TEXT("#%d %s"), D->Id, *D->State);
				float W = 0.0f, H = 0.0f;
				GetTextSize(Label, W, H, Small, 1.0f);
				DrawRect(FLinearColor(0.0f, 0.0f, 0.0f, 0.55f), S.X - W * 0.5f - 2.0f, S.Y, W + 4.0f, H);
				DrawText(Label, FLinearColor::White, S.X - W * 0.5f, S.Y, Small, 1.0f);
			}
		}
	}

	if (GM->IsDebugOn(ARtsBridgeGameMode::DebugStats))
	{
		const TArray<FString> Lines = {
			FString::Printf(TEXT("tick %d   (20 t/s, sim time %.1f s)"), GM->GetSimTick(), GM->GetSimTick() / 20.0),
			FString::Printf(TEXT("sim step %.3f ms   round trip %.3f ms"), GM->GetStepMs(), GM->GetRoundTripMs()),
			FString::Printf(TEXT("units %d   (yours %d, enemy %d)"), GM->GetUnitActors().Num(), PerFaction[0], PerFaction[1] + PerFaction[2]),
			FString::Printf(TEXT("%.0f fps   selected %d"), FpsAvg, Sel.Num()),
			TEXT("F4 debug overlays on/off"),
		};
		float Y = 12.0f;
		const float X = 12.0f;
		float MaxW = 0.0f, LineH = 0.0f;
		for (const FString& L : Lines)
		{
			float W = 0.0f, H = 0.0f;
			GetTextSize(L, W, H, Small, 1.0f);
			MaxW = FMath::Max(MaxW, W);
			LineH = FMath::Max(LineH, H);
		}
		DrawRect(FLinearColor(0.0f, 0.0f, 0.0f, 0.6f), X - 6.0f, Y - 4.0f, MaxW + 12.0f, LineH * Lines.Num() + 8.0f);
		for (int32 i = 0; i < Lines.Num(); ++i)
			DrawText(Lines[i], i == Lines.Num() - 1 ? FLinearColor(0.7f, 0.7f, 0.7f) : FLinearColor(0.4f, 1.0f, 0.5f),
				X, Y + LineH * i, Small, 1.0f);
	}
}

ARtsMinimapHud::FEconomyBarText ARtsMinimapHud::EconomyBarText(int32 Credits, int32 PowerSupply, int32 PowerDraw)
{
	auto Group = [](int32 V)
	{
		FString Digits = FString::Printf(TEXT("%d"), FMath::Abs(V));
		FString Out;
		for (int32 i = 0; i < Digits.Len(); ++i)
		{
			if (i > 0 && (Digits.Len() - i) % 3 == 0)
				Out.AppendChar(TEXT(','));
			Out.AppendChar(Digits[i]);
		}
		return V < 0 ? TEXT("-") + Out : Out;
	};
	FEconomyBarText T;
	T.Credits = FString::Printf(TEXT("Credits  %s"), *Group(Credits));
	T.Power = FString::Printf(TEXT("Power  %d / %d"), PowerDraw, PowerSupply);
	T.bLowPower = PowerSupply - PowerDraw < 0;
	if (T.bLowPower)
		T.Power += TEXT("   LOW POWER");
	return T;
}

void ARtsMinimapHud::DrawEconomyBar()
{
	// T35: your (faction 0) credits and power, top centre. Snapshot values only.
	const ARtsBridgeGameMode* GM = GetWorld() ? GetWorld()->GetAuthGameMode<ARtsBridgeGameMode>() : nullptr;
	const FRtsFactionSnapshot* F = GM ? GM->GetFactionState(0) : nullptr;
	if (!F || !Canvas)
		return;
	const double Now = GetWorld()->GetRealTimeSeconds();
	if (LastCredits != INT32_MIN && F->Credits != LastCredits)
	{
		CreditsDelta = F->Credits - LastCredits;
		CreditsDeltaUntil = Now + 1.2;
	}
	LastCredits = F->Credits;

	const FEconomyBarText T = EconomyBarText(F->Credits, F->PowerSupply, F->PowerDraw);
	UFont* Font = GEngine ? GEngine->GetMediumFont() : nullptr;
	const float Scale = 1.0f;
	float CW = 0.0f, CH = 0.0f, PW = 0.0f, PH = 0.0f;
	GetTextSize(T.Credits, CW, CH, Font, Scale);
	GetTextSize(T.Power, PW, PH, Font, Scale);
	const float Gap = 36.0f, Pad = 10.0f;
	const float W = CW + Gap + PW + Pad * 2.0f;
	const float H = FMath::Max(CH, PH) + 8.0f;
	const float X = (Canvas->SizeX - W) * 0.5f;
	const float Y = 8.0f;
	const FLinearColor Back = T.bLowPower ? FLinearColor(0.35f, 0.02f, 0.02f, 0.8f) : FLinearColor(0.0f, 0.0f, 0.0f, 0.65f);
	DrawRect(Back, X, Y, W, H);
	DrawText(T.Credits, FLinearColor(1.0f, 0.86f, 0.35f), X + Pad, Y + 4.0f, Font, Scale);
	DrawText(T.Power, T.bLowPower ? FLinearColor(1.0f, 0.3f, 0.25f) : FLinearColor(0.55f, 0.85f, 1.0f),
		X + Pad + CW + Gap, Y + 4.0f, Font, Scale);
	if (Now < CreditsDeltaUntil && CreditsDelta != 0)
	{
		const float A = static_cast<float>(FMath::Clamp((CreditsDeltaUntil - Now) / 0.4, 0.0, 1.0));
		const FString D = FString::Printf(TEXT("%+d"), CreditsDelta);
		DrawText(D, CreditsDelta > 0 ? FLinearColor(0.4f, 1.0f, 0.45f, A) : FLinearColor(1.0f, 0.45f, 0.35f, A),
			X + Pad, Y + H + 2.0f, Font, Scale);
	}
}
