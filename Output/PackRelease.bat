@echo off
rem Builds the plugins and packs PoW_English.zip (and QoL.zip) from the Mod folder.
rem The plugin DLLs are not in git: they are built here every time, so a release never ships old ones.
setlocal
cd /d "%~dp0"

set "PLUGINS=Mod\BepInEx\plugins"

rem MSBuild of Visual Studio (any edition with the .NET desktop workload), found with vswhere, else on the PATH
set "MSBUILD="
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if exist "%VSWHERE%" for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -prerelease -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) do set "MSBUILD=%%i"
if not defined MSBUILD for /f "tokens=*" %%i in ('where msbuild 2^>nul') do set "MSBUILD=%%i"
if not defined MSBUILD (
    echo MSBuild not found. Install Visual Studio with the ".NET desktop development" workload.
    goto :failed
)

rem The old DLLs go first: if a build fails, nothing old can be packed instead
del /q "%PLUGINS%\PoW_ModAPI.dll" "%PLUGINS%\PoW_EnglishPatch.dll" "%PLUGINS%\PathOfWuxiaEndingEnglish.dll" 2>nul

rem Through the solution: it sets each plugin's platform and puts the NuGet packages where the projects look for them
echo Building the plugins with %MSBUILD%
"%MSBUILD%" ..\PoW_Loader.sln "-t:PoW_ModAPI;PoW_EnglishPatch;PathOfWuxiaEndingEnglish" -restore -p:RestorePackagesConfig=true -p:Configuration=Release "-p:Platform=Any CPU" -nologo -verbosity:minimal
if errorlevel 1 (
    echo Building the plugins failed.
    goto :failed
)

for %%d in (PoW_ModAPI PoW_EnglishPatch PathOfWuxiaEndingEnglish) do (
    if not exist "%PLUGINS%\%%d.dll" (
        echo %PLUGINS%\%%d.dll was not built.
        goto :failed
    )
)

rem Debug files are not shipped
del /q "%PLUGINS%\PoW_ModAPI.pdb" "%PLUGINS%\PoW_EnglishPatch.pdb" "%PLUGINS%\PathOfWuxiaEndingEnglish.pdb" 2>nul

echo.
echo Plugins in the release:
for %%f in ("%PLUGINS%\PoW_ModAPI.dll" "%PLUGINS%\PoW_EnglishPatch.dll" "%PLUGINS%\PathOfWuxiaEndingEnglish.dll") do echo   %%~tf  %%~zf bytes  %%~nxf
echo.

rem Packing into an existing zip adds to it, old files would stay inside
del /q PoW_English.zip QoL.zip 2>nul

rar > nul 2>&1
if "%errorlevel%" == "9009" (
    echo WinRar not found... Trying 7-Zip
    goto :7zip
) else (
    winrar a -afzip PoW_English.zip Mod
    if exist Qol_Mod winrar a -afzip QoL.zip Qol_Mod
    goto :end
)

:7zip
7z > nul 2>&1
if "%errorlevel%" == "9009" (
    echo 7-Zip not found... Please install either WinRar or 7-Zip and add their binary locations to your environment path
    goto :failed
) else (
    7z a -tzip PoW_English.zip Mod
    if exist Qol_Mod 7z a -tzip QoL.zip Qol_Mod
    goto :end
)

:end
echo Done!
START https://github.com/magxm/PoW-Loader/releases/new
endlocal
exit /b 0

:failed
echo Nothing was packed.
pause
endlocal
exit /b 1
