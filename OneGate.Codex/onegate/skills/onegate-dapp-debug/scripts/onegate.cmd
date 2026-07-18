@echo off
setlocal

set "ONEGATE_SCRIPT=%~dp0onegate.mjs"

if defined ONEGATE_NODE (
  call :is_compatible_node "%ONEGATE_NODE%"
  if errorlevel 1 goto node_error
  set "NODE_EXE=%ONEGATE_NODE%"
  goto run
)

call :is_compatible_node node
if not errorlevel 1 (
  set "NODE_EXE=node"
  goto run
)

for /d %%D in ("%USERPROFILE%\.cache\codex-runtimes\*") do (
  if exist "%%~fD\dependencies\node\bin\node.exe" (
    call :is_compatible_node "%%~fD\dependencies\node\bin\node.exe"
    if not errorlevel 1 (
      set "NODE_EXE=%%~fD\dependencies\node\bin\node.exe"
      goto run
    )
  )
)

:node_error
>&2 echo OneGate requires Node.js 22 or newer with built-in WebSocket support.
>&2 echo Install a current Node.js release or set ONEGATE_NODE to its executable path.
exit /b 127

:run
"%NODE_EXE%" "%ONEGATE_SCRIPT%" %*
exit /b %ERRORLEVEL%

:is_compatible_node
"%~1" -e "const major=Number(process.versions.node.split('.')[0]);process.exit(major>=22&&typeof WebSocket==='function'?0:1)" >nul 2>nul
exit /b %ERRORLEVEL%
