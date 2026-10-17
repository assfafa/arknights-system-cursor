# 给 Codex / Claude / 豆包等 AI 助手的操作说明

目标：在用户自己的明日方舟 PC 安装目录中切换系统指针，并保留可恢复的原始资源。

本文件既可在仓库中读取，也可单独发给具备本机文件访问和命令执行能力的 AI 助手。普通网页助手没有本机权限时，应指导用户运行 GUI，不能声称已经修改用户电脑。

## 推荐入口

下载本项目 [最新 Release 的便携 ZIP](https://github.com/assfafa/arknights-system-cursor/releases/latest)，解压后使用 `ArknightsSystemCursor.Cli.exe`。不要自动下载并执行来源不明的同名文件。用户也可以从本仓库源码构建。

询问或使用用户已经提供的游戏路径。目标目录应同时包含 `Arknights.exe` 和 `Arknights_Data`。工具的 `locate` 是有限的候选目录检测，不能保证找到所有自定义安装位置。

```powershell
$Tool = 'C:\Tools\ArknightsSystemCursor\ArknightsSystemCursor.Cli.exe'
$GameDir = 'D:\Games\Hypergryph Launcher\games\Arknights Game'
& $Tool locate --json
& $Tool status --game-dir $GameDir --json
```

`status` 读取游戏文件，不改名资源。`Status.Mode` 含义：

| 值 | 含义 |
| --- | --- |
| `game` | 原始鼠标资源存在，可启用系统指针 |
| `system` | 已识别资源全部处于停用状态 |
| `missing` | 没找到资源，但本地有备份记录，可尝试恢复 |
| `unsupported` | 未识别到支持的鼠标资源，停止修改 |

`Mode` 描述磁盘资源状态，不能替代游戏内显示效果的验证。

## 启用

如果用户已明确要求启用系统指针，这已授权对应的备份和改名操作，不必重复确认。先确保用户已经正常退出游戏；不要直接结束可能正在战斗的客户端进程。

```powershell
$raw = & $Tool enable --game-dir $GameDir --json
$exitCode = $LASTEXITCODE
$result = $raw | ConvertFrom-Json
if ($exitCode -eq 0 -and $result.Success) {
    $result.Message
    $result.Status
}
else {
    throw $result.Error
}
```

成功后告诉用户：资源已备份并停用，请正常启动游戏，检查 loading、主界面和战斗时的指针。只有用户实测或你实际观察到结果后，才能报告游戏内已经生效。

## 恢复

用户要求恢复，或启用后遇到加载失败/指针消失且用户已授权修复时，退出游戏后执行：

```powershell
& $Tool restore --game-dir $GameDir --json
& $Tool status --game-dir $GameDir --json
```

工具优先使用改名保留的文件，也支持从独立备份恢复。原文件已存在时不会覆盖，避免把游戏更新后的资源替换成旧版本。SHA-256 校验失败、多个历史版本无法确定、未知资源格式等情况应保留所有文件并报告原因。

## 备份与边界

- 默认备份记录：`%LOCALAPPDATA%\ArknightsSystemCursor\installations\<标识>\manifest.json`。
- 独立备份：同目录 `backups\<SHA-256>.bin`。
- 原文件改名后缀：`.system-cursor-disabled-<SHA-256前12位>`。
- 兼容此前手动添加的 `.disabled-by-codex` 后缀。
- `--state-dir <目录>` 可指定备份根目录；启用和恢复必须使用同一位置。通常保留默认值即可。
- 不删除鼠标包、修改 DLL/EXE、修改注册表、改资源索引或尝试绕过反作弊。
- 不把备份、游戏资源、账号配置或私人路径提交到公共仓库。
- 不凭旧版本硬编码文件名直接删除资源；调用工具的内容识别和校验逻辑。

## 可直接复制给助手的提示

> 请阅读 https://github.com/assfafa/arknights-system-cursor/blob/main/docs/AI-ASSISTANTS.md ，帮我在本机的明日方舟 PC 客户端启用系统指针。游戏目录是「在这里填写」。先检查状态，确认游戏已退出，再使用项目 CLI 备份并停用已验证的鼠标资源。保留恢复办法，最后告诉我实际做了什么，不要修改 DLL 或删除资源。
