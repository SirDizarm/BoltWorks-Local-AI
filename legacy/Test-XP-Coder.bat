@echo off
setlocal
pushd "%~dp0"
if errorlevel 1 goto cannot_open
set "MODE=--staged"
if not "%~1"=="" (
    set "MODEL=%~f1"
    set "MODE=--auto"
    goto model_selected
)
set "MODEL=Qwen2.5-Coder-1.5B-Instruct-IQ4_XS-00001-of-00003.gguf"
if not exist "%MODEL%" set "MODEL=Qwen2.5-Coder-1.5B-Instruct-IQ4_XS.gguf"
if not exist "%MODEL%" set "MODEL=Qwen2.5-Coder-0.5B-Instruct-IQ4_XS-00001-of-00002.gguf"
if not exist "%MODEL%" set "MODEL=Qwen2.5-Coder-0.5B-Instruct-IQ4_XS.gguf"
if not exist "%MODEL%" (
    set "MODEL=SmolLM2-360M-Instruct-IQ4_XS.gguf"
    set "MODE="
)
:model_selected
if not exist "%MODEL%" goto no_model
if not exist "REAAI.EXE" goto no_runner

:new_run
set "RUN=XP-CODE-%RANDOM%-%RANDOM%"
if exist "%RUN%" goto new_run
mkdir "%RUN%"
if errorlevel 1 goto cannot_save
echo Model: "%MODEL%"
echo Results will be saved in "%RUN%" beside this batch file.
echo Leave the window open while the model writes the C draft.
echo.
".\REAAI.EXE" --code "%MODEL%" "Implement int c_to_f(int celsius) for temperatures from -100 to 100. Return celsius times 9 divided by 5 plus 32 using integer arithmetic. Output only the function, without main." %MODE% >"%RUN%\DRAFT.C" 2>"%RUN%\MODEL.LOG"
set "RESULT=%ERRORLEVEL%"
> "%RUN%\RESULTS.TXT" echo Model: "%MODEL%"
>>"%RUN%\RESULTS.TXT" echo Exit code: %RESULT%
type "%RUN%\MODEL.LOG"
echo Exit code: %RESULT%
echo Draft and logs: "%RUN%"
pause
popd
exit /b %RESULT%

:no_model
echo Put a model beside the launcher, or drag a GGUF or the first shard onto this batch file.
goto failed
:no_runner
echo Extract REAAI.EXE beside this batch file.
goto failed
:cannot_save
echo Could not create a results folder beside this batch file.
:failed
popd
:cannot_open
pause
exit /b 1
