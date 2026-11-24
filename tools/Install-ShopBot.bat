@echo off
rem ShopBot installer - double-click. Finds the game, checks BepInEx, copies the mod.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" %*
echo.
pause
