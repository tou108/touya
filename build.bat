@echo off
echo ============================================
echo  NX Macro Advanced - Build Script
echo ============================================
echo.

:: Check .NET 8 SDK
dotnet --version > nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo [ERROR] .NET 8 SDK not found.
    echo Please install from: https://dotnet.microsoft.com/download
    pause
    exit /b 1
)

echo [1/3] Restoring NuGet packages...
dotnet restore NXMacroAdvanced\NXMacroAdvanced.csproj
if %ERRORLEVEL% neq 0 (
    echo [ERROR] NuGet restore failed.
    pause
    exit /b 1
)

echo [2/3] Building Release (win-x64)...
dotnet publish NXMacroAdvanced\NXMacroAdvanced.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o dist\
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Build failed.
    pause
    exit /b 1
)

echo [3/3] Post-build setup...
if not exist dist\tessdata mkdir dist\tessdata

echo.
echo ============================================
echo  Build complete!
echo  Output: dist\NXMacroAdvanced.exe
echo.
echo  [OCR] To enable OCR, place jpn.traineddata
echo        and eng.traineddata in dist\tessdata\
echo  DL: https://github.com/tesseract-ocr/tessdata
echo ============================================
pause
