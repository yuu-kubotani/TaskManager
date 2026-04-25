@echo off
chcp 65001 >nul
setlocal

cd /d "%~dp0"

echo ファイル構成の整理を開始します...

rem 1. 命名規則に合わせたリネーム
if exist "Src\Forms\TimeLogEntryForm.cs" (
    echo TimeLogEntryForm.cs を FormTimeLogEntry.cs にリネームします...
    move /Y "Src\Forms\TimeLogEntryForm.cs" "Src\Forms\FormTimeLogEntry.cs"
)

rem 2. ルートフォルダにある古い画面ファイルの削除
set OLD_FORMS=ArchiveViewForm.cs EventInputForm.cs MemoInputForm.cs ReportForm.cs SettingsForm.cs TaskInputForm.cs FormTimeLogInput.cs MainForm.cs
for %%f in (%OLD_FORMS%) do (
    if exist "%%f" (
        echo 古い画面ファイル %%f を削除しています...
        del /f /q "%%f"
    )
)

rem 3. 古いデータモデルファイルの削除
set OLD_MODELS=RecurringRule.cs TaskItem.cs TimeLog.cs WorkFile.cs EventItem.cs ProjectItem.cs CsvRepository.cs JsonRepository.cs
for %%f in (%OLD_MODELS%) do (
    if exist "%%f" (
        echo 古いモデルファイル %%f を削除しています...
        del /f /q "%%f"
    )
)

rem 4. ゴミファイルや古いバッチ、不要になった旧ビルドツールの削除
set MISC_FILES=0 file_list.txt start.bat UndoCommands.cs NativeMethods.cs Run.bat run.bat nuget.exe compiled_files_list.txt
for %%f in (%MISC_FILES%) do (
    if exist "%%f" (
        echo 不要ファイル %%f を削除しています...
        del /f /q "%%f"
    )
)
if exist "packages" (
    echo 古いpackagesフォルダを削除しています...
    rmdir /s /q "packages"
)

echo.
echo 整理が完了しました！
pause