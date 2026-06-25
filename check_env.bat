@echo off
echo ========================================
echo  NX Macro Advanced - Environment Check
echo ========================================
echo.

echo [1] Checking .NET SDK...
dotnet --version
if %ERRORLEVEL% neq 0 (
    echo  NOT FOUND.
    echo  Please install .NET 8 SDK:
    echo  https://dotnet.microsoft.com/download/dotnet/8.0
) else (
    echo  OK.
)

echo.
echo [2] Checking solution file...
if exist NXMacroAdvanced\NXMacroAdvanced.csproj (
    echo  NXMacroAdvanced.csproj ... OK
) else (
    echo  NXMacroAdvanced.csproj ... NOT FOUND
    echo  Make sure you run this from the project root folder.
)

echo.
echo [3] Current directory:
cd
echo.
pause
