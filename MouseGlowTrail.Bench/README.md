# MouseGlowTrail.Bench

离线开发工具，不随产品发布。它直接编译 `..\MouseGlowTrail` 里的源码（渲染、轨迹模型、粒子与点击效果、GPU 渲染器、设置，以及驱动真实叠加窗口的 TrailEngine），另带一份冻结的 v2.0 光栅器 `ReferenceCanvas.cs` 作对照。控制面板（Direct2D 界面）不在这里编译，用下面的真机脚本测试。

```
dotnet build -c Release
dotnet bin\Release\net10.0-windows\MouseGlowTrail.Bench.dll all out
```

| 模式 | 作用 |
| --- | --- |
| `icon` | 生成 `out\app.ico`（复制到 `..\MouseGlowTrail\app.ico` 后再发布）和图标预览图 `icons.png` |
| `preview` | 用模拟手势渲染 v1/v2 对比图、五种样式（深色 / 浅色背景）、五档浓淡（`opacity.png`）、点击涟漪逐帧图、甩动后停下的收尾效果、8 种粒子（`particles.png`）、每种点击与滚轮效果的逐帧图（`effects.png`） |
| `verify` | 不变量检查（11 项）：分块清屏无残影、调色板无色缝、断笔不连线、场景会自动清空、休眠后重新起笔不预先变淡、AVX2 与标量逐位一致、GPU 与 CPU 逐像素一致（含全部粒子、点击 / 滚轮效果和自定义配色）、无重叠丝带与 v2.0 圆角拼接的覆盖一致（alpha 差 ≤ 1 级）、按行收窄不改变任何像素、2.x 设置文件迁移后外观不变、3.0 默认设置与 2.1 画面逐像素相同；失败时退出码为 1 |
| `bench` | 每帧绘制耗时：v1 GDI+（64 Hz）、v2.0 与 v2.1 光栅器（180 Hz）对比 |
| `all` | 以上全部 |
| `profile` | 把一帧拆成模型 / 边界 / 清屏 / 丝带 / 粒子各阶段计时 |
| `probe` | 诊断：列出与 v2.0 渲染不同的像素，以及覆盖它们的每一段丝带 |
| `gpumem` | GPU 渲染器各阶段（设备、DirectComposition、首帧、释放）的内存 |
| `live [cpu\|gpu\|both][-heavy]` | 在真实桌面上用脚本指针驱动真正的叠加窗口约 12 秒 / 场景（不移动鼠标）：每帧耗时、进程与各线程的 CPU 周期（QueryThreadCycleTime，精确计数）、dwm.exe 周期、内存，以及长时间停顿后重新移动是否掉帧（原"停顿后闪烁"问题的回归测试）。`-heavy` 为醒目 + 长轨迹 + 大幅划动 |

真机脚本（都会在桌面上启动程序或 live 模式，结束时正常关闭）：

- `smoke.ps1 -Exe <exe> -Out <dir> [-Shots]`：用 `--demo` 启动，检查单实例、正常退出、空闲 CPU 周期与内存；`-Shots` 才截取指针附近的屏幕（会拍到桌面上的内容，默认不截）。
- `gpu-smoke.ps1 -Exe <exe>`：临时开启"GPU 加速"运行一次，确认使用 DirectComposition、在深色衬底上截取演示流光、记录内存，结束后逐字节还原 `settings.json`。
- `screen.ps1`：在深色衬底上分别截取 CPU / GPU 渲染的 live 画面（`screen-cpu.png`、`screen-gpu.png`），并在 40 个点上确认叠加窗口从不挡住鼠标点击。
- `idle.ps1 -Exe <exe>`：分别统计叠加窗口隐藏时和显示时的 CPU 占用（按系统计时粒度，较粗）。
- `menu.ps1 -Exe <exe> -Out <dir> [-Attach] [-Downs N]`：用定向消息打开托盘右键菜单和第 N 个子菜单并截屏。截图会带上菜单后面的桌面内容，看完即删。
- `cycles.ps1 -Ids "<pid> <pid>" [-Seconds N]`：同一时段内几个进程的 CPU 周期（QueryProcessCycleTime），用来对比新旧版本。

控制面板（开发版与已安装的程序互不干扰）：

- `dev.sh`：重新编译并以 `--isolated --panel` 启动开发版。`--isolated` 使用单独的实例名、`%TEMP%\MouseGlowTrail-dev\settings.json`，开机自启只在内存里模拟，不写注册表。设置环境变量 `MGT_THEME=dark|light` 可强制深色 / 浅色主题。
- `panel-drive.ps1 -Steps "..."`：用窗口消息驱动面板，不移动真实鼠标。步骤包括 `move / click / down / up / drag / wheel / key / char / wait / size / front / shot`，坐标为面板客户区的 DIP。`shot` 只拍面板本身：拍摄时把面板置顶，并在 81 个点上确认看到的确实是面板，被遮挡就不保存。有人同时在用鼠标时，实际的鼠标移动可能打断模拟点击（捕获期间指针在别处）。
- `memtest.sh`（可用 `EXE=...` 指定发布版）：打开面板前、面板打开时（预览运行中）和关闭后的 CPU 与内存。

修改 `..\MouseGlowTrail\Shaders\Trail.hlsl` 后运行 `..\MouseGlowTrail\Shaders\build-shaders.ps1`（需要 Windows SDK 的 fxc.exe），它会重新生成 `..\MouseGlowTrail\Shaders.cs`；产品运行时从不编译着色器。
