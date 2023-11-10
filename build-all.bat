@echo off

set "blddir=%~d0%~p0"

if not exist "%blddir%detours\src" (
    pushd "%blddir%detours"
    git submodule update --init detours
    if errorlevel 1 exit /b 1
    popd
)

set "bldconf=%1"

echo "Building detours and detours.dll"
cmd /c %blddir%build-detours.bat x86 %bldconf%
if errorlevel 1 exit /b 2

cmd /c %blddir%build-detours.bat x64 %bldconf%
if errorlevel 1 exit /b 3

echo "Patching Detours header for metadata generation"
powershell -Command "@(Get-Content '%blddir%\detours\src\detours.h')[0..866+920..1234] | Set-Content '%blddir%\detours\include\detours.h'"

echo "Building Detours metadata"
pushd "%blddir%detours-meta"
dotnet clean
dotnet build /p:BuildConfig="%bldconf%"
if errorlevel 1 (
    popd
    exit /b 3
)
popd

echo "Building withdll"

if /I "%bldconf%" == "Debug" (
    set withdllConfig=Debug
) else (
    set withdllConfig=Release
)

pushd "%blddir%withdll"
dotnet publish -r win-x64 -c %withdllConfig% -o "%blddir%withdll\bin.x64%bldconf%"
if errorlevel 1 (
    popd
    exit /b 3
)
popd
