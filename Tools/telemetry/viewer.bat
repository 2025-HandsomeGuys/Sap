@echo off
cd /d "%~dp0"
echo.
echo   Telemetry Viewer + Journal
echo   Keep this window open while playing. Closing it stops the server.
echo.
where python >NUL 2>NUL
if %errorlevel%==0 (
  python viewer_server.py %*
) else (
  py viewer_server.py %*
)
pause
