@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

:: 管理者権限の確認 (net sessionコマンドを使用)
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo 管理者権限で再実行しています。UACプロンプトが表示されたら「はい」を選択してください...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

echo ======================================================
echo  MS2027 ゲーム通信ポート開放設定の解除 (UDP 7777)
echo ======================================================
echo.

set RULE_NAME="MS2027_GamePort_7777"

:: 既存ルールの存在確認と削除
netsh advfirewall firewall show rule name=%RULE_NAME% >nul 2>&1
if %errorlevel% equ 0 (
    echo ファイアウォール規則を削除しています...
    netsh advfirewall firewall delete rule name=%RULE_NAME% >nul
    if %errorlevel% equ 0 (
        echo.
        echo [成功] ファイアウォール規則 (%RULE_NAME%) を正常に削除しました。
    ) else (
        echo.
        echo [エラー] 規則の削除中にエラーが発生しました。
    )
) else (
    echo.
    echo [情報] 該当する規則 (%RULE_NAME%) は登録されていません。
)

echo.
echo 終了するには何かキーを押してください...
pause >nul

