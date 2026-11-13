@echo off
setlocal
if "%~1"=="" goto usage
if not exist "%~f1" goto usage
if exist "%~dp1CONVERT.EXE" goto exists
"%~dp0compiler\TCC.EXE" -Wall -o "%~dp1CONVERT.EXE" "%~dp0CONSOLE.C" "%~f1" >"%~dp1BUILD.LOG" 2>&1
set "RESULT=%ERRORLEVEL%"
if not "%RESULT%"=="0" type "%~dp1BUILD.LOG"
echo Compiler exit code: %RESULT%
if "%RESULT%"=="0" echo Created "%~dp1CONVERT.EXE"
echo Log: "%~dp1BUILD.LOG"
pause
exit /b %RESULT%

:exists
echo CONVERT.EXE already exists beside the draft. Move or rename it to build again.
pause
exit /b 1

:usage
echo Drag the generated DRAFT.C onto this batch file to build the sample.
pause
exit /b 2
