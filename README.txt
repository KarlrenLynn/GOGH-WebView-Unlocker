GOGH WebView Overlay v30
========================
在多人模式下激活数位屏的实时画面叠加（OBS虚拟摄像头）。

安装方法:
1. 先安装 BepInEx 6 IL2CPP:
   从 https://builds.bepinex.dev/projects/bepinex_be 下载
   BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.764+5f39645.zip
   解压到游戏根目录 (gogh.exe 所在目录)

2. 解压本压缩包到游戏根目录
   BepInEx/plugins/GOGHWebViewUnlocker.dll 会被放入正确位置

3. 启动游戏 (Steam)

使用方法:
- F9:  列出房间内所有物品
- F10: 在下一个物品上启动画面叠加
  (只有带 2 个 Renderer 的物品会生效 - 即数位屏)
- 默认使用 OBS Virtual Camera
  (确保 OBS 已启动并开启了虚拟摄像头)

修改摄像头名称:
  编辑 src/Plugin.cs 中 "OBS Virtual Camera" 字符串
  重新编译: dotnet build -c Release
  将 bin/Release/net6.0/GOGHWebViewUnlocker.dll 复制到 BepInEx/plugins/

问题排查:
  检查 BepInEx/LogOutput.log