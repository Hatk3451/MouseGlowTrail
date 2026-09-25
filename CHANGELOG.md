# Changelog · 更新记录

## 3.0.0

- **Control panel.** A Windows 11 style window (Mica, light and dark themes, accent color) with a live preview that uses the real renderer and your own pointer. Click the tray icon to open it; right-click the icon for the quick menu.
  新增控制面板：Windows 11 风格（云母背景、深浅色、强调色），实时预览使用真正的渲染器和你自己的指针。左键托盘图标打开，右键为快捷菜单。
- **Custom colors.** Build a gradient from up to six colors of your own, with a color picker. New fine-tuning options: color flow, glow, core highlight, speed-dependent width, tapered tail and smoothing.
  自定义配色（最多 6 种颜色，带取色器）。新增色彩流动、光晕、中心高光、随速度变化粗细、尾部收窄、平滑度等细调选项。
- **Particles.** Seven new particle kinds, with adjustable amount, size, drift and color.
  新增 7 种粒子，可调数量、大小、飘散速度和颜色。
- **Click and wheel effects.** Each mouse button gets its own click effect and color (six effects to choose from). The wheel gets three optional scroll effects.
  左键、右键、中键可以分别设置点击效果和颜色（6 种效果可选）；新增 3 种可选的滚轮效果。
- **Trail origin.** The trail can start at the pointer tip, behind the pointer, at its center, or at a custom offset.
  拖尾发生点：指针尖端、指针后方、指针中心或自定义偏移。
- **Languages.** English and Chinese, following the Windows display language by default.
  中英双语，默认跟随 Windows 显示语言。
- **Left-handed mice.** With the buttons swapped, "left button" means the primary button.
  支持左手鼠标：交换左右键后，“左键”指主按键。
- **Compatibility.** With the default settings the trail looks exactly as in 2.1, pixel for pixel, at the same CPU cost. 2.x settings files are migrated automatically.
  默认设置下画面与 2.1 逐像素相同，CPU 开销不变；2.x 的设置会自动迁移。

## 2.1.0

- Fixed a flicker when the mouse moved again after a pause of several seconds.
  修复：停顿数秒后再移动鼠标时流光会闪烁。
- Drawing is 1.5–1.8 times faster, the upload volume is about 45% smaller, and CPU use at rest is close to zero.
  绘制速度提高 1.5–1.8 倍，上传数据量减少约 45%，静止时几乎不占 CPU。
- Optional GPU rendering (Direct3D 11 + DirectComposition).
  新增可选的 GPU 绘制（Direct3D 11 + DirectComposition）。

## 2.0.0

- Rewritten on pure Win32 with an AVX2 software renderer. The trail is drawn at the display refresh rate, the tray menu is native, and there are five styles and five opacity levels.
  改用纯 Win32 与 AVX2 软件渲染器重写；按显示器刷新率绘制；原生托盘菜单；5 种样式与 5 档浓淡。

## 1.x

- The first WinForms / GDI+ version.
  最初的 WinForms / GDI+ 版本。
