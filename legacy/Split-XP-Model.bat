@echo off
setlocal
if "%~1"=="" goto usage
"%~dp0GGUF-SPLIT.EXE" "%~f1"
set "RESULT=%ERRORLEVEL%"
echo.
echo Splitter exit code: %RESULT%
pause
exit /b %RESULT%

:usage
echo Drag the original GGUF file onto this batch file.
echo The numbered shards will appear beside that GGUF.
pause
exit /b 2
