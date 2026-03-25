# 网易云音乐 NCM 转换插件

## 功能说明

本插件是基于ncmdump开发的用于ConverTool的插件，用于将网易云音乐 NCM 格式文件转换为 MP3 或 FLAC 格式。
ConverTool仓库https://github.com/kckc7887/ConverTool
ncmdump仓库https://github.com/taurusxin/ncmdump

## 支持的格式

- **输入格式**: NCM (网易云音乐缓存格式)
- **输出格式**: MP3, FLAC

## 安装步骤

1. **构建插件**
   ```bash
   dotnet build NcmDumpPlugin.csproj -c Release
   ```

2. **部署插件**
   - 将 `bin\Release\net8.0` 目录下的所有文件复制到 ConverTool 的 plugins 目录

## 使用方法

1. 打开 ConverTool
2. 拖入 NCM 文件
3. 选择输出格式 (MP3 或 FLAC)
4. 点击开始转换

## 自动工具管理

插件采用 ConverTool 标准的工具管理机制：

- **自动检测**: 首次使用时会自动检测 ncmdump 工具是否存在
- **自动下载**: 如果工具不存在，会从 GitHub Releases 自动下载最新版本
- **共享缓存**: 工具会被缓存到 ConverTool 的共享工具目录，供其他插件使用
- **版本管理**: 支持多版本工具并存

## 配置选项

插件支持以下配置选项：

- **删除源文件**: 转换成功后是否删除原始 NCM 文件
- **保留元数据**: 是否保留歌曲的元数据信息

## 技术实现

插件通过调用 ncmdump 命令行工具进行转换，原仓库地址https://github.com/taurusxin/ncmdump

## 注意事项

1. 首次使用时会自动下载 ncmdump 工具（约 5-10MB）
2. 转换过程中请勿关闭 ConverTool
3. 转换后的文件会保留原始文件名，仅更改扩展名
4. 工具会被缓存到 `%LOCALAPPDATA%\ConverTool\tools` 目录

## 开源许可

本插件基于 MIT 许可证开源。
ncmdump 工具的许可信息请参考其 GitHub 仓库。