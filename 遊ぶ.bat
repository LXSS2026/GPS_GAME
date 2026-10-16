@echo off
rem GPS 試遊版の起動: C# をビルドしてから Godot で都市の画面を開く
cd /d "%~dp0"
echo GPS をビルドしています...
dotnet build game\GPS.csproj -c Debug -v q --nologo
if errorlevel 1 (
  echo ビルドに失敗しました。dotnet SDK が入っているか確かめてください。
  pause
  exit /b 1
)
start "" ".tools\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe" --path game
