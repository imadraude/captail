# UI snapshot QA

Renders Captail's real WPF views directly to PNG files without showing or running the application window.

```powershell
dotnet build tools/UiSnapshotQa/UiSnapshotQa.csproj -p:BuildNativeDependencies=false
dotnet run --project tools/UiSnapshotQa/UiSnapshotQa.csproj --no-build -- artifacts/ui-snapshots/current
```

The output covers the main window, settings, editor, player, audio routing, status indicators, and an icon catalog at 100%, 125%, 150%, and 200% scale.
