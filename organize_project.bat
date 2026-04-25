@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

echo プロジェクトのフォルダ構成を「最高にいい感じ」に整理しています...

rem --- 1. 究極のフォルダ構成を作成 ---
if not exist "Src\Forms" mkdir "Src\Forms"
if not exist "Src\Models" mkdir "Src\Models"
if not exist "Src\Services" mkdir "Src\Services"
if not exist "Src\Utils" mkdir "Src\Utils"
if not exist "Data" mkdir "Data"
if not exist "Docs" mkdir "Docs"
if not exist "Legacy_PowerShell" mkdir "Legacy_PowerShell"

rem --- 2. C# ソースコードを Src フォルダに大集約 ---
move "Program.cs" "Src\" >nul 2>&1
move "Form*.cs" "Src\Forms\" >nul 2>&1
move "*Form.cs" "Src\Forms\" >nul 2>&1
move "*Service.cs" "Src\Services\" >nul 2>&1
move "*Repository.cs" "Src\Services\" >nul 2>&1
move "Models.cs" "Src\Models\" >nul 2>&1
move "AppSettings.cs" "Src\Models\" >nul 2>&1
move "ThemeManager.cs" "Src\Utils\" >nul 2>&1
move "DarkModeRenderer.cs" "Src\Utils\" >nul 2>&1
move "Prompt.cs" "Src\Utils\" >nul 2>&1
move "NativeMethods.cs" "Src\Utils\" >nul 2>&1

rem 既に中途半端に作られていたフォルダの中身も Src へお引越し
if exist "Forms\*.cs" move "Forms\*.cs" "Src\Forms\" >nul 2>&1
if exist "Models\*.cs" move "Models\*.cs" "Src\Models\" >nul 2>&1
if exist "Services\*.cs" move "Services\*.cs" "Src\Services\" >nul 2>&1
if exist "Helpers\*.cs" move "Helpers\*.cs" "Src\Utils\" >nul 2>&1
if exist "Native\*.cs" move "Native\*.cs" "Src\Utils\" >nul 2>&1

rem 空になった古いフォルダをお掃除
rd "Forms" >nul 2>&1
rd "Models" >nul 2>&1
rd "Services" >nul 2>&1
rd "Helpers" >nul 2>&1
rd "Native" >nul 2>&1

rem --- 3. 過去のPowerShellスクリプトを専用フォルダに隔離 ---
move "*.ps1" "Legacy_PowerShell\" >nul 2>&1

rem --- 4. 説明書やドキュメントを Docs に整理 ---
move "*.md" "Docs\" >nul 2>&1
move "report_template.html" "Docs\" >nul 2>&1

rem --- 5. ユーザーデータを Data に整理 ---
move "*.csv" "Data\" >nul 2>&1
move "*.json" "Data\" >nul 2>&1

echo.
echo ===================================================
echo 整理が完了しました！ルートフォルダが驚くほどスッキリしました。
echo 変更を反映するため、Run.bat を実行して再ビルドを行ってください。
echo ===================================================
pause