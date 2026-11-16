# MCHOSE V7 托盘电量

不启动 **MCHOSE HUB**，直接读取迈从 V7 鼠标（2.4G 接收器 / 有线）的电量，并显示在系统托盘。

## 功能

- **托盘图标**：直接显示电量数字（如 `75`），按电量变色（绿 / 橙 / 红）。
- **悬停提示**：鼠标悬停显示设备名、电量、连接方式、充电状态、更新时间。
- **右键菜单**：立即刷新、重新识别设备、刷新间隔（10 秒 ~ 5 分钟）、开机自启、关于我们、退出。
- **自动识别 + 缓存**：首次运行自动探测设备并把身份写入缓存，之后优先使用缓存，无需写死型号。
- **离线容错**：鼠标休眠导致单次查询失败时保留上次读数，连续 3 次失败才显示离线。
- **低电量提醒**：≤15% 且未充电时弹一次气泡通知。

## 原理

MCHOSE HUB 的 V7 协议在 HID 上走 **report ID `0x4D`**（64 字节报文）。

查询设备信息（含电量）的指令为 `0x0900`：

```
发: 4D 01 01 00 00 09 00 00 08 00 ... 00        (64 字节，含 report ID)
      ^  ^  ^  ^  ^^^^^  ^  ^
      |  |  |  |    |    |  └ 校验：XOR(byte2..7+dataLen)
      |  |  |  |    |    └ 序号
      |  |  |  |    └ 业务码
      |  |  └──┴ 命令 0x0900（小端）
      |  └ 数据长度
      └ flags=1（带校验的请求）

收: 4D 01 01 10 00 09 00 00 37 38 26 40 03 00 00 50 00 00 01 00 4B ...
```

payload（去掉 report ID 后从索引 7 开始）字段：

| 偏移 | 含义 |
|------|------|
| 0..1 | VID（小端，`0x3837`） |
| 2..3 | 内部 PID（小端） |
| 4 | 最大配置数 |
| 5 | 宏保存类型 |
| 6..7 | 共享宏数据大小 |
| 8..9 | 宏单元大小 |
| 10 | 连接状态（1 = 2.4G，0 = 有线） |
| 11 | 充电状态（1 = 充电中） |
| **12** | **电量百分比** |
| 13 | 游戏模式 |

以包含 report ID 的 64 字节读缓冲区计算：**电量 = `buf[20]`**，连接状态 = `buf[18]`，充电状态 = `buf[19]`。

> 该协议来自对 `MCHOSE HUB` 的网络资源与 HID 通信的逆向分析，并通过与官方驱动界面显示的 75% 交叉验证。

## 设备识别与缓存

没有写死 PID，首次运行会自己找设备：

1. **优先缓存**：从 `settings.json` 的 `KnownDevices` 里取设备身份，按 `VID + PID + usagePage + usage` 匹配 HID 接口，命中就直接查询。
2. **自动发现**：缓存未命中时，只扫描**厂商自定义集合**（usage page ≥ `0xFF00`），并且要求产品名含 `MCHOSE` / `V7` / `2.4G` 或 VID 为 `0x3837`，避免骚扰键盘、鼠标这类标准集合和其它厂商设备。
3. **校验**：只有响应里的 VID 回显、命令号、报文长度都对得上，才认为识别成功。
4. **写入缓存**：识别成功后保存 VID/PID/usagePage/usage/reportId/command/产品名/最后识别时间。

缓存文件：`%APPDATA%\MchoseBattery\settings.json`

```json
"KnownDevices": [
  {
    "Vid": 14391,          // 0x3837
    "Pid": 4118,           // 0x1016
    "UsagePage": 65281,    // 0xFF01
    "Usage": 1,
    "Product": "MCHOSE V7",
    "ReportId": 77,        // 0x4D
    "Command": 2304,       // 0x0900
    "LastSeenUtc": "..."
  }
]
```

说明：

- 缓存键**不包含设备路径**：USB 换个口路径就变了，而 VID/PID/usage 不变。
- 设备识别不到、或换了新型号时，右键菜单点 **“重新识别设备”** 会清空缓存并重新扫描。
- 缓存是普通 JSON，必要时可以手改（比如手动填入某个已知设备的 VID/PID/usagePage）。
- 缓存按最近识别时间排序，最多保留 8 个（有线/接收器/多只鼠标都能分别记住）。

## 运行

已发布版本（双击根目录 `build.bat` 生成）：

```
build\MchoseBattery.exe
```

双击运行即可，无主窗口，只出现托盘图标。Windows 11 可能把它放进“隐藏的图标”里，拖到任务栏上即可常驻。

- 右键托盘图标 → **开机自动启动**
- 右键托盘图标 → **退出**

需要 .NET 10 Desktop Runtime（本机已安装 SDK，可直接运行）。若要在没有运行时的机器上使用：

```powershell
dotnet publish src/MchoseBattery -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:DebugType=none -o build/MchoseBattery-standalone
```

## 构建

项目根目录提供两个双击即可运行的脚本：

| 脚本 | 作用 |
|------|------|
| `build.bat` | 编译并发布单文件 exe，输出到 `build\MchoseBattery.exe` |
| `clean.bat` | 清理 `build\`、`dist\` 以及各级 `bin\`、`obj\` |

也可以手动执行：

```powershell
dotnet build src/MchoseBattery -c Debug
dotnet publish src/MchoseBattery -c Release -r win-x64 --self-contained false `
  -p:PublishSingleFile=true -p:DebugType=none -o build
```

产物为 `build\MchoseBattery.exe`（单文件，约 195 KB，依赖 .NET 10 Desktop Runtime）。

## 开发环境

| 项目 | 版本 / 说明 |
|------|-------------|
| 操作系统 | Windows 11 x64（内部版本 `10.0.26200`） |
| .NET SDK | `10.0.403` |
| 目标框架 | `net10.0-windows`（WinForms，PerMonitorV2 DPI） |
| 目标平台 | `x64` |
| 构建/发布 | `dotnet` CLI，单文件（framework-dependent） |
| 运行时要求 | .NET 10 Desktop Runtime |
| 第三方依赖 | 无（仅 BCL + WinForms + `System.Text.Json`） |

开发与逆向分析时用到的工具：

| 工具 | 用途 |
|------|------|
| `probe/`（本项目自带） | HID 探针：枚举接口、读取 usage caps、监听/重放 64 字节报文 |
| Windows PowerShell 5.1 | 注册表、进程、窗口几何、截图等辅助脚本 |
| Node.js `v24.5.0` | 通过 CDP（`--remote-debugging-port`）连接 MCHOSE HUB 渲染进程，Hook `sendReport` / `sendFeatureReport` 抓真实报文 |
| Python `3.13.6` | 字符串提取、缓存文件与协议数据分析脚本 |
| Git Bash | 日常命令行 |

调试入口（无需启动托盘界面）：

```powershell
# 查看自动识别结果和查询耗时（--force 强制重新扫描）
build/MchoseBattery.exe --probe probe-result.txt
build/MchoseBattery.exe --probe probe-result.txt --force

# 导出托盘图标预览（32px / 24px / 16px）
build/MchoseBattery.exe --dump-icon icon-preview.png 75
```

## 项目结构

```
src/MchoseBattery/
  Program.cs              入口（单实例互斥，含 --probe 诊断）
  HidBatteryReader.cs     HID 枚举 + 自动发现 + 0x0900 电量查询
  Native.cs               Win32 P/Invoke
  BatteryIconRenderer.cs  托盘数字图标绘制
  TrayAppContext.cs       托盘图标、菜单、轮询、缓存维护
  AppSettings.cs          设置 + 设备缓存持久化
  CachedDevice.cs         缓存中的设备身份（VID/PID/usage/reportId/command）

probe/                    开发期 HID 探针（枚举接口、监听报文、重放指令）
build.bat                 编译发布脚本（输出 build\MchoseBattery.exe）
clean.bat                 清理编译产物脚本
```

## 说明与限制

- 目前针对 **MCHOSE V7**（`VID 0x3837 / PID 0x1016`）实测通过；其它使用 `0x0900` 设备信息指令的迈从鼠标理论上通用，识别后会各自写入缓存。
- 电量由鼠标固件返回，与本机是否运行 MCHOSE HUB **无关**。
- 托盘图标是 16×16 缩放下仍可读的大号数字；更详细的信息在悬停提示里。
