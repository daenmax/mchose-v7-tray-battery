@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
title MCHOSE V7 托盘电量 - 清理

echo ============================================
echo   MCHOSE V7 托盘电量 - 清理编译产物
echo ============================================
echo.

if exist "build" (
    echo 删除 build\ ...
    rmdir /s /q "build" >nul 2>nul
    if exist "build" echo [警告] build\ 未能完全删除，可能程序正在运行。
)

if exist "dist" (
    echo 删除 dist\ ...
    rmdir /s /q "dist" >nul 2>nul
    if exist "dist" echo [警告] dist\ 未能完全删除。
)

echo 删除各级 bin\ 和 obj\ ...
for /d /r "%~dp0" %%d in (bin obj) do (
    if exist "%%d" (
        echo   %%d
        rmdir /s /q "%%d" >nul 2>nul
    )
)

echo.
echo 清理完成。
echo.
pause
exit /b 0
