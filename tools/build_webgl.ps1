# しゃくとりの森: ローカルで WebGL をビルドして docs/ に配置する（GitHub Pages 用）
#   powershell -ExecutionPolicy Bypass -File tools/build_webgl.ps1 [-Setup] [-Assets]
#     -Assets : Blender のモデル・アイコン・フォント・音を作り直す
#     -Setup  : Unity のセットアップ（マテリアル・シーン生成）をやり直す
param(
    [switch]$Setup,
    [switch]$Assets,
    [string]$Unity = "C:\Program Files\Unity\Hub\Editor\6000.5.4f1\Editor\Unity.exe",
    [string]$Blender = "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"
)
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $root "unity"
$logDir = Join-Path $root "build-tmp"
New-Item -ItemType Directory -Force $logDir | Out-Null

function Run-Unity([string]$method, [string]$log, [string[]]$extra = @()) {
    $unityArgs = @("-batchmode", "-projectPath", $project, "-executeMethod", $method, "-logFile", $log) + $extra
    $p = Start-Process -FilePath $Unity -ArgumentList $unityArgs -Wait -PassThru -NoNewWindow
    if ($p.ExitCode -ne 0) {
        Get-Content $log -Tail 60
        throw "Unity が失敗しました ($method)。ログ: $log"
    }
}

if ($Assets) {
    Write-Host "== Blender でモデルを生成"
    & $Blender -b --factory-startup --python (Join-Path $root "blender/scripts/build_forest_kit.py")
    & $Blender -b --factory-startup --python (Join-Path $root "blender/scripts/build_creatures.py")
    & $Blender -b --factory-startup --python (Join-Path $root "blender/scripts/build_park_kit.py")
    Write-Host "== アイコン・フォント・音を生成"
    python (Join-Path $root "tools/make_icons.py")
    python (Join-Path $root "tools/make_fonts.py")
    python (Join-Path $root "tools/make_audio.py")
}

if ($Setup -or $Assets) {
    Write-Host "== Unity セットアップ"
    Run-Unity "Shakutori.EditorTools.ProjectSetup.RunBatch" (Join-Path $logDir "setup.log")
}

Write-Host "== WebGL ビルド"
$out = Join-Path $root "WebGLBuild"
Run-Unity "Shakutori.EditorTools.BuildScript.BuildWebGL" (Join-Path $logDir "build.log") @("-customBuildPath", $out)

Write-Host "== docs/ に配置"
$docs = Join-Path $root "docs"
if (Test-Path $docs) { Remove-Item -Recurse -Force $docs }
Copy-Item -Recurse $out $docs
Get-ChildItem $docs -Directory | Where-Object { $_.Name -like "*DoNotShip" } | Remove-Item -Recurse -Force
New-Item -ItemType File -Force (Join-Path $docs ".nojekyll") | Out-Null
python (Join-Path $root "tools/check_build.py") $docs
if ($LASTEXITCODE -ne 0) { throw "ビルドの検査に失敗しました" }
$size = (Get-ChildItem -Recurse $docs | Measure-Object -Property Length -Sum).Sum / 1MB
Write-Host ("完了: docs/ ({0:N1} MB)" -f $size)
