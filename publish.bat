@echo off
chcp 65001 >nul
setlocal
set "ROOT=%~dp0"
set "SELFCONT=--self-contained true"
set "OUT=%ROOT%publish\win-x64"
set "ZIP=%ROOT%publish\LogFilter-win-x64.zip"
if /i "%1"=="slim" goto slim
goto build

:slim
set "SELFCONT=--self-contained false"
set "OUT=%ROOT%publish\win-x64-slim"
set "ZIP=%ROOT%publish\LogFilter-win-x64-slim.zip"

:build
echo 正在发布，请稍候...
dotnet publish "%ROOT%LogFilterApp.csproj" -c Release -r win-x64 %SELFCONT% -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o "%OUT%"
if errorlevel 1 goto fail
if exist "%ZIP%" del /q "%ZIP%"
powershell -NoProfile -Command "Compress-Archive -Path '%OUT%\LogFilterApp.exe' -DestinationPath '%ZIP%' -Force"
echo.
echo 发布完成
echo   EXE : %OUT%\LogFilterApp.exe
echo   ZIP : %ZIP%
echo.
echo 把 exe 或 zip 发给同事即可，双击运行。
echo 首次运行会在 exe 旁边自动生成 config 目录。
goto end

:fail
echo 发布失败，请检查上面的错误信息

:end
pause
