@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
title MCHOSE V7 托盘电量 - 编译发布

echo ============================================
echo   MCHOSE V7 托盘电量 - 编译发布
echo ============================================
echo.

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [错误] 未找到 dotnet 命令。
    echo        请先安装 .NET 10 SDK：https://dotnet.microsoft.com/
    echo.
    pause
    exit /b 1
)

echo [1/3] 准备输出目录...
if exist "build" rmdir /s /q "build" >nul 2>nul
mkdir "build" 2>nul
if not exist "build" (
    echo [错误] 无法创建 build 目录。
    echo.
    pause
    exit /b 1
)

echo [2/3] 编译并发布单文件 exe...
dotnet publish "src\MchoseBattery\MchoseBattery.csproj" ^
    -c Release -r win-x64 --self-contained false ^
    -p:PublishSingleFile=true -p:DebugType=none ^
    -o "build\_stage"
if errorlevel 1 goto :failed

echo [3/3] 复制 exe 到 build 目录...
copy /y "build\_stage\MchoseBattery.exe" "build\MchoseBattery.exe" >nul 2>nul
if errorlevel 1 (
    echo.
    echo [错误] 复制失败，build\MchoseBattery.exe 可能正在运行。
    echo        请先退出托盘程序，再重新运行本脚本。
    goto :failed
)
rmdir /s /q "build\_stage" >nul 2>nul

echo.
echo 编译完成：
echo   %~dp0build\MchoseBattery.exe
echo.
pause
exit /b 0

:failed
echo.
echo 编译失败，请检查上面的错误信息。
echo.
pause
exit /b 1
