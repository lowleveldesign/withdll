@echo off

if "%1" == "" (
    set bldarch=x64
) else (
    set "bldarch=%1"
)

if /I "%2" == "DEBUG" (
    set DETOURS_CONFIG=Debug
) else (
    set DETOURS_CONFIG=
)

echo "Building detours (%bldarch%%DETOURS_CONFIG%)"

set "currdir=%~d0%~p0"

if EXIST "c:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvarsall.bat" (
    call "c:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvarsall.bat" %bldarch%
) else (
    call "c:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvarsall.bat" %bldarch%
)
if errorlevel 1 exit /b 1

pushd "%currdir%detours"
nmake
if errorlevel 1 (
    popd
    exit /b 2
)
popd

pushd "%currdir%detours-dll"

if "%DETOURS_CONFIG%" == "Debug" (
    mkdir "bin.%bldarch%%DETOURS_CONFIG%"
    cd "bin.%bldarch%%DETOURS_CONFIG%"

    cl.exe /nologo /LD /TP /DUNICODE /DWIN32 /D_WINDOWS /EHsc /W4 /WX /Zi /Ob0 /Od /RTC1 /Fodetours.obj /Fddetours.pdb ..\detours.cpp ^
        /link /def:..\detours.def "%currdir%detours\lib.%bldarch%%DETOURS_CONFIG%\detours.lib"
) else (
    mkdir "bin.%bldarch%"
    cd "bin.%bldarch%"

    cl.exe /nologo /LD /TP /DUNICODE /DWIN32 /D_WINDOWS /EHsc /W4 /WX /Zi /O2 /Ob1 /DNDEBUG /Fodetours.obj /Fddetours.pdb ..\detours.cpp ^
        /link /def:..\detours.def "%currdir%detours\lib.%bldarch%\detours.lib"
)
if errorlevel 1 (
    popd
    exit /b 2
)

popd
