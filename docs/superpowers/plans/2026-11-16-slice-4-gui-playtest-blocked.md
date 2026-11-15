# Slice 4 GUI play-test — blocked report (2026-11-16)

Status: **blocked — no Unreal editor on this host.**

## Requested

Interactive GUI play-test of the slice 4 gameplay loop (GRY-288):
pan/zoom/rotate camera, select, issue a move, confirm host state changes,
confirm host failure does not fabricate state.

## Checked

Host: brian-FAEX1-master (this run), 2026-11-16.

1. Pinned engine path `~/.local/opt/UnrealEngine-5.8.3` — absent.
   `~/.local/opt/` contains only: Antigravity-x64, apktool.jar, jadx, jdk,
   jdk-21.0.12.1+1, llama.cpp. No Unreal.
2. Filesystem search for `UnrealEngine*`, `UnrealEditor-Cmd`,
   `InstalledBuild.txt` under `/home`, `/opt`, `/srv`, `/usr/local`,
   `/run/media`, `/mnt` — no hits.
3. Display: **available**. X.Org `:0` running (`xdpyinfo` OK), active
   Wayland session on tty2, amdgpu PCI `1002:7551` with render nodes
   renderD128/129. A GUI play-test could run on this host if the engine
   were present.
4. Other hosts: magnus (192.168.18.175) and venus (192.168.18.131)
   unreachable from this host (`ssh: no route to host`). The engine's
   original location cannot be verified remotely.
5. Docker images and local archives — no Unreal engine image or archive.

## Conclusion

The pinned Unreal 5.8.3 editor is not installed on this host. Per the
issue instruction, the engine was not installed. The interactive GUI
play-test is blocked on this host.

## Verified earlier (not re-claimed here)

`RtsBridge.Host.RoundTrip` passes headless (nullrhi): real host launch,
init, tick-0 move, 120 steps, clean shutdown. That is the headless
coverage recorded in slice 4; it does not replace the interactive GUI
play-test.

## To unblock

- Restore or install the pinned Unreal 5.8.3 editor on a host with a
  display (this host qualifies: X `:0` + amdgpu), then run the play-test
  per the issue work list.
- Or make magnus/venus reachable and verify the engine there.

No push to main, no merge, no deployment.
