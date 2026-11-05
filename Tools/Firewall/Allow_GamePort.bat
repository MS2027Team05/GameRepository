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
echo  MS2027 展示・検証用 ゲーム通信ポート開放 (UDP 7777)
echo ======================================================
echo.

set RULE_NAME="MS2027_GamePort_7777"
set PORT=7777

:: 二重登録防止のため既存ルールがあれば削除
netsh advfirewall firewall show rule name=%RULE_NAME% >nul 2>&1
if %errorlevel% equ 0 (
    echo 既存のファイアウォール規則を更新します...
    netsh advfirewall firewall delete rule name=%RULE_NAME% >nul 2>&1
)

:: UDPポート7777の受信規則を追加 (すべてのネットワークプロファイルに適用)
echo ファイアウォール受信規則を登録中 (ポート: %PORT% / UDP)...
netsh advfirewall firewall add rule name=%RULE_NAME% dir=in action=allow protocol=UDP localport=%PORT% profile=any >nul

if %errorlevel% equ 0 (
    echo.
    echo [成功] ファイアウォール規則の登録が正常に完了しました。
    echo 対象ポート: UDP %PORT% (ドメイン / プライベート / パブリック すべて許可)
    echo Unity Editorおよびビルド済みexeでの通信が可能です。
) else (
    echo.
    echo [エラー] ファイアウォール規則の登録に失敗しました。
)

echo.
echo 終了するには何かキーを押してください...
pause >nul

