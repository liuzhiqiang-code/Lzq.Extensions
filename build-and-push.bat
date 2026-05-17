@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

:: 配置参数
set Configuration=Release
set RootDir=%~dp0
set OutputDir=%RootDir%packages

:: NuGet 推送目标（默认官方源，可修改为自定义源）
set NuGetSource=https://api.nuget.org/v3/index.json

:: API Key 读取方式：优先从环境变量 NUGET_API_KEY 获取，否则提示输入
if defined NUGET_API_KEY (
    set ApiKey=%NUGET_API_KEY%
) else (
    set /p ApiKey="请输入 NuGet API Key: "
    if "!ApiKey!"=="" (
        echo [错误] 未提供 API Key，操作终止。
        exit /b 1
    )
)

:: 是否跳过重复推送（若包已存在则报错退出，设为 1 则跳过）
set SkipDuplicate=1

:: 定义要打包的项目列表（相对于根目录的路径）
set Projects=^
    src\Lzq.Core\Lzq.Core.csproj ^
    src\Lzq.Extensions.AI\Lzq.Extensions.AI.csproj ^
    src\Lzq.Extensions.EventBus\Lzq.Extensions.EventBus.csproj ^
    src\Lzq.Extensions.EventBus.RabbitMq\Lzq.Extensions.EventBus.RabbitMq.csproj ^
    src\Lzq.Extensions.ExternalHttpApi\Lzq.Extensions.ExternalHttpApi.csproj ^
    src\Lzq.Extensions.Jwt\Lzq.Extensions.Jwt.csproj ^
    src\Lzq.Extensions.NSwag\Lzq.Extensions.NSwag.csproj ^
    src\Lzq.Extensions.Redis\Lzq.Extensions.Redis.csproj ^
    src\Lzq.Extensions.Serilog\Lzq.Extensions.Serilog.csproj ^
    src\Lzq.Extensions.SqlSugar\Lzq.Extensions.SqlSugar.csproj ^

:: 创建输出目录
if not exist "%OutputDir%" mkdir "%OutputDir%"

:: 清理旧包
echo 清理旧包...
del /q "%OutputDir%\*.nupkg" 2>nul

echo ========================================
echo 开始批量打包
echo 配置: %Configuration%
echo 输出目录: %OutputDir%
echo ========================================

set SuccessCount=0
set FailCount=0

:: 遍历打包每个项目
for %%p in (%Projects%) do (
    echo.
    echo ========================================
    echo 正在打包: %%~np
    echo ========================================
    
    set "ProjectPath=%RootDir%%%p"
    
    if not exist "!ProjectPath!" (
        echo [错误] 项目文件不存在: !ProjectPath!
        set /a FailCount+=1
    ) else (
        :: 执行打包
        dotnet pack "!ProjectPath!" ^
            --configuration %Configuration% ^
            --output "%OutputDir%" ^
            --no-restore
            
        if !errorlevel! equ 0 (
            echo [成功] %%~np 打包成功
            set /a SuccessCount+=1
        ) else (
            echo [失败] %%~np 打包失败
            set /a FailCount+=1
        )
    )
)

echo.
echo ========================================
echo  打包完成！
echo  成功: %PackSuccessCount% 个
echo  失败: %PackFailCount% 个
echo ========================================

:: 若打包全部失败，不进行推送
if %PackSuccessCount% equ 0 (
    echo 没有成功生成的包，跳过推送步骤。
    exit /b 1
)

:: ================= 推送所有 .nupkg =================
echo.
echo ========================================
echo  开始推送至 NuGet 源: %NuGetSource%
echo ========================================

set PushSuccessCount=0
set PushFailCount=0

for %%f in ("%OutputDir%\*.nupkg") do (
    echo.
    echo 正在推送: %%~nxf
    set "PushArgs=dotnet nuget push "%%f" --source "%NuGetSource%" --api-key "!ApiKey!""
    if "%SkipDuplicate%"=="1" (
        set "PushArgs=!PushArgs! --skip-duplicate"
    )
    !PushArgs!
    if !errorlevel! equ 0 (
        echo [成功] %%~nxf 推送成功
        set /a PushSuccessCount+=1
    ) else (
        echo [失败] %%~nxf 推送失败
        set /a PushFailCount+=1
    )
)

echo.
echo ========================================
echo  推送完成！
echo  成功: %PushSuccessCount% 个
echo  失败: %PushFailCount% 个
echo ========================================

:: 若有推送失败则返回错误码
if %PushFailCount% gtr 0 (
    exit /b 1
)

exit /b 0
::pause