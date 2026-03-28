@echo off
setlocal EnableDelayedExpansion

set MSVC_VER=14.44.35207
set MSVC_BIN=C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Tools\MSVC\%MSVC_VER%\bin\Hostx64\x64
set MSVC_INC=C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Tools\MSVC\%MSVC_VER%\include
set MSVC_LIB=C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Tools\MSVC\%MSVC_VER%\lib\x64

set SDK_VER=10.0.26100.0
set SDK_INC=C:\Program Files (x86)\Windows Kits\10\Include\%SDK_VER%
set SDK_LIB=C:\Program Files (x86)\Windows Kits\10\Lib\%SDK_VER%

set SRC=%~dp0ZModManager.Bootstrap
set OUT=%~dp0ZModManager\Resources\Bootstrap
set OBJ=%~dp0ZModManager.Bootstrap\obj

if not exist "%OBJ%" mkdir "%OBJ%"
if not exist "%OUT%" mkdir "%OUT%"

echo Compiling dllmain.cpp...
"%MSVC_BIN%\cl.exe" /c /nologo /W3 /EHsc /O2 /Gy /GS- /MT /DNDEBUG /D_WINDOWS /D_USRDLL ^
  /I"%MSVC_INC%" /I"%SDK_INC%\um" /I"%SDK_INC%\shared" /I"%SDK_INC%\ucrt" ^
  /Fo"%OBJ%\dllmain.obj" "%SRC%\dllmain.cpp"

if errorlevel 1 ( echo COMPILE FAILED & pause & exit /b 1 )

echo Linking version.dll...
"%MSVC_BIN%\link.exe" /DLL /NOLOGO /OPT:REF /OPT:ICF ^
  /DEF:"%SRC%\ZModManager.Bootstrap.def" ^
  /OUT:"%OUT%\version.dll" ^
  /LIBPATH:"%MSVC_LIB%" /LIBPATH:"%SDK_LIB%\um\x64" /LIBPATH:"%SDK_LIB%\ucrt\x64" ^
  kernel32.lib "%OBJ%\dllmain.obj"

if errorlevel 1 ( echo LINK FAILED & pause & exit /b 1 )

echo.
echo BUILD SUCCEEDED
dir "%OUT%\version.dll"
