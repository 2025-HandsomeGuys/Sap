@echo off
cd /d "%~dp0"
where python >NUL 2>NUL
if %errorlevel%==0 (
  python upgrade_editor_server.py %*
) else (
  py upgrade_editor_server.py %*
)
pause
