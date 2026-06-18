# GOGH Stream Overlay v35

在 GOGH（Focus with Your Avatar）多人模式下，为数位屏物品添加实时摄像头串流覆盖层。

---

## 安装方法

### Full 版（解压即用，推荐新用户）

下载 `GOGH_StreamOverlay_v35_Full.zip`（33.8 MB），解压到游戏根目录（`gogh.exe` 所在目录），覆盖同名文件。

> 已包含 BepInEx 6 IL2CPP 框架和 .NET 6 运行时，无需额外安装。

### Lite 版（已有 BepInEx 的用户）

下载 `GOGH_StreamOverlay_v35.zip`（13 KB），解压到游戏根目录。

> 需要已安装 BepInEx 6 IL2CPP（build #764+），详见 https://builds.bepinex.dev/projects/bepinex_be

---

## 操作方法

| 按键 | 功能 |
|------|------|
| **F9** | 打开/关闭控制面板 |
| **F10** | 刷新屏幕列表 |

### 控制面板

- **Refresh** — 重新扫描房间内的数位屏
- **All ON** — 全部开启串流
- **All OFF** — 全部关闭串流（恢复原图）
- **Seq Test** — 序列测试：自动轮切，每次只在一台屏幕上串流，方便定位
- **Close** — 关闭面板
- **Toggle 开关** — 每个屏幕独立开关

---

## 摄像头配置

编辑 `BepInEx/config/com.gogh.webviewunlocker.cfg`：

```ini
[General]
CameraName = OBS Virtual Camera
```

将 `CameraName` 改为你的摄像头名称，重启游戏生效。

---

## 原理

### 问题分析

GOGH 在多人模式下，数位屏的串流功能被禁用：
- 点击物品时，不显示"Picture/Stream"选项气泡
- `StreamingTextureController` 的三个依赖接口（`ICameraPermissionRequester`、`IRoomItemStickerPaster`、`IToastRequester`）在多人场景中完全不存在
- `SelectWebCameraScene` 从未被加载

经过 Harmony 反射 patch、Zenject DI 注入、Native 内存 patch 等多种方案的尝试，确认游戏在多人模式下刻意剥离了串流基础设施。

### 解决方案：材质直接覆盖

绕过游戏串流系统的全部依赖，直接操作 3D 物体的材质：

1. **定位显示面**：数位屏有一个额外的 `MeshRenderer`（第二个 Renderer），这是串流画面的载体
2. **创建 WebCamTexture**：通过 IL2CPP 反射创建 `WebCamTexture`，直接调用 `Play()` 启动摄像头捕获
3. **材质替换**：将 `WebCamTexture` 赋给 `material.mainTexture`，清除 `_ChangeTex` 避免冲突
4. **等比缩放**：检测摄像头与屏幕宽高，自动 pillarbox/letterbox 防止裁剪
5. **共享单例**：一个 `WebCamTexture` 实例被所有屏幕共享

---

## 编译

需要 .NET 8 SDK 和已安装 BepInEx 的游戏目录。

```bash
cd src
dotnet build -c Release
```

产物在 `bin/Release/net6.0/GOGHWebViewUnlocker.dll`。

---

## 卸载

删除游戏根目录下的：

- `BepInEx/`
- `dotnet/`
- `doorstop_config.ini`
- `winhttp.dll`

---

## 版本历史

- **v35** — 等比缩放（pillarbox/letterbox）、尺寸日志
- **v34** — 配置文件摄像头名称、自动刷新面板
- **v31** — WebCamTexture 全局单例、过滤非屏幕物品
- **v30** — Camera+RT 方案（后被 WebCamTexture 替代）
- **v12** — Native 字段 lock（isStreaming 等）
- **v1** — 初始 Harmony 反射扫描方案

## 许可

仅供个人学习与研究使用。