@echo off
rem 文字化け防止のためUTF-8に設定
chcp 65001 >nul
setlocal

rem バッチファイルが存在するディレクトリ（カレントディレクトリ）に移動
cd /d "%~dp0"

echo ===================================================
echo TaskManager プロジェクトのクリーンアップを実行します。
echo ===================================================
echo.
echo 【削除される項目】
echo  - bin フォルダ, obj フォルダ
echo  - ゴミファイル (0, file_list.txt)
echo  - 今後発生する一時実行ファイル・中間ファイル (*.pdb, *.tmp 等)
echo  - 使われなくなった古い重複ソースコード (MainForm.cs, *Form.cs 等)
echo.
echo 【保持される項目（削除されません）】
echo  - C#ソースコード (*.cs) とプロジェクト設定 (.sln, .vscode 等)
echo  - ユーザーデータ (*.csv, *.json) とバックアップ (backup フォルダ)
echo  - 実行に必要なツール群 (Run.bat, nuget.exe 等)
echo.

set /p CONFIRM="本当に削除してよろしいですか？ (Y/N): "
if /i not "%CONFIRM%"=="Y" (
    echo.
    echo キャンセルしました。
    pause
    exit /b
)

echo.
echo クリーンアップを開始しています...

rem --- 1. 不要なフォルダの削除 ---
if exist "bin" rd /s /q "bin" >nul 2>&1
if exist "obj" rd /s /q "obj" >nul 2>&1
if exist "packages" rd /s /q "packages" >nul 2>&1

rem --- 2. 現在存在しているゴミファイルの削除 ---
if exist "0" del /f /q "0" >nul 2>&1
if exist "file_list.txt" del /f /q "file_list.txt" >nul 2>&1
if exist "start.bat" del /f /q "start.bat" >nul 2>&1

rem --- 3. 今後生成される一時ファイル・中間ファイルの予防的削除 ---
del /f /q "*_TaskManager.exe" >nul 2>&1
del /f /q "notified_today.log" >nul 2>&1
del /f /q "*.pdb" >nul 2>&1
del /f /q "*.cache" >nul 2>&1
del /f /q "*.tmp" >nul 2>&1

rem --- 4. 使われなくなった古い重複ソースコードの削除 ---
rem "Form*.cs" へ移行したため不要になった古い "*Form.cs" などのレガシーファイル
if exist "ArchiveViewForm.cs" del /f /q "ArchiveViewForm.cs" >nul 2>&1
if exist "EventInputForm.cs" del /f /q "EventInputForm.cs" >nul 2>&1
if exist "MemoInputForm.cs" del /f /q "MemoInputForm.cs" >nul 2>&1
if exist "ReportForm.cs" del /f /q "ReportForm.cs" >nul 2>&1
if exist "SettingsForm.cs" del /f /q "SettingsForm.cs" >nul 2>&1
if exist "TaskInputForm.cs" del /f /q "TaskInputForm.cs" >nul 2>&1
if exist "FormTimeLogInput.cs" del /f /q "FormTimeLogInput.cs" >nul 2>&1
if exist "MainForm.cs" del /f /q "MainForm.cs" >nul 2>&1
if exist "RecurringRule.cs" del /f /q "RecurringRule.cs" >nul 2>&1
if exist "TaskItem.cs" del /f /q "TaskItem.cs" >nul 2>&1
if exist "TimeLog.cs" del /f /q "TimeLog.cs" >nul 2>&1
if exist "WorkFile.cs" del /f /q "WorkFile.cs" >nul 2>&1
if exist "EventItem.cs" del /f /q "EventItem.cs" >nul 2>&1
if exist "ProjectItem.cs" del /f /q "ProjectItem.cs" >nul 2>&1
if exist "CsvRepository.cs" del /f /q "CsvRepository.cs" >nul 2>&1
if exist "JsonRepository.cs" del /f /q "JsonRepository.cs" >nul 2>&1

echo.
echo ===================================================
echo クリーンアップが完了しました。フォルダ内は綺麗です！
echo ===================================================
pause
