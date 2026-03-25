using PluginAbstractions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace NcmDumpPlugin
{
    public class NcmConverterPlugin : IConverterPlugin
    {
        private const string ToolName = "ncmdump";
        private const string ToolVersion = "1.3.0";
        private const string ToolExecutable = "ncmdump.exe";
        private string _localToolPath;

        public PluginManifest GetManifest()
        {
            return new PluginManifest(
                PluginId: "ncmdump-converter",
                Version: "1.0.0",
                SupportedInputExtensions: new[] { "ncm" },
                SupportedTargetFormats: new[]
                {
                    new TargetFormat("mp3", "plugin/ncmdump/target/mp3"),
                    new TargetFormat("flac", "plugin/ncmdump/target/flac")
                },
                ConfigSchema: new ConfigSchema(
                    Sections: new[]
                    {
                        new ConfigSection(
                            Id: "general",
                            TitleKey: "plugin/ncmdump/section/general",
                            DescriptionKey: null,
                            CollapsedByDefault: false,
                            Fields: new[]
                            {
                                new ConfigField(
                                    Key: "deleteSource",
                                    Type: ConfigFieldType.Checkbox,
                                    LabelKey: "plugin/ncmdump/field/deleteSource",
                                    HelpKey: "plugin/ncmdump/field/deleteSource/help",
                                    DefaultValue: false
                                ),
                                new ConfigField(
                                    Key: "preserveMetadata",
                                    Type: ConfigFieldType.Checkbox,
                                    LabelKey: "plugin/ncmdump/field/preserveMetadata",
                                    HelpKey: "plugin/ncmdump/field/preserveMetadata/help",
                                    DefaultValue: true
                                )
                            }
                        )
                    }
                ),
                SupportedLocales: new[] { "zh-CN", "en-US" },
                I18n: new I18nDescriptor("locales")
            );
        }

        public async Task ExecuteAsync(ExecuteContext context, IProgressReporter reporter, CancellationToken cancellationToken = default)
        {
            try
            {
                reporter.OnLog("正在检查 ncmdump 工具...");

                await EnsureToolDownloadedAsync(context, reporter, cancellationToken);

                var ncmdumpPath = GetNcmdumpPath();
                var outputDir = context.TempJobDir;

                reporter.OnLog($"开始转换: {Path.GetFileName(context.InputPath)}");
                reporter.OnProgress(new ProgressInfo(ProgressStage.Running, 0));

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = ncmdumpPath,
                        Arguments = $"\"{context.InputPath}\" -o \"{outputDir}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                await process.WaitForExitAsync(cancellationToken);

                if (process.ExitCode == 0)
                {
                    var outputFile = FindConvertedFile(context.InputPath, outputDir, context.TargetFormatId);
                    if (!string.IsNullOrEmpty(outputFile) && File.Exists(outputFile))
                    {
                        reporter.OnProgress(new ProgressInfo(ProgressStage.Finalizing, 100));

                        var deleteSource = context.SelectedConfig.TryGetValue("deleteSource", out var delSrc) && 
                                          delSrc is bool b && b;

                        if (deleteSource)
                        {
                            try
                            {
                                File.Delete(context.InputPath);
                                reporter.OnLog("已删除源文件");
                            }
                            catch (Exception ex)
                            {
                                reporter.OnLog($"删除源文件失败: {ex.Message}");
                            }
                        }

                        reporter.OnCompleted(new CompletedInfo(
                            OutputRelativePath: Path.GetFileName(outputFile),
                            OutputSuggestedExt: context.TargetFormatId
                        ));
                    }
                    else
                    {
                        reporter.OnFailed(new FailedInfo("转换完成但未找到输出文件"));
                    }
                }
                else
                {
                    var error = await process.StandardError.ReadToEndAsync(cancellationToken);
                    reporter.OnFailed(new FailedInfo($"转换失败: {error}"));
                }
            }
            catch (OperationCanceledException)
            {
                reporter.OnFailed(new FailedInfo("操作已取消"));
            }
            catch (Exception ex)
            {
                reporter.OnFailed(new FailedInfo($"转换过程中发生错误: {ex.Message}"));
            }
        }

        private async Task EnsureToolDownloadedAsync(ExecuteContext context, IProgressReporter reporter, CancellationToken cancellationToken)
        {
            // 尝试多个本地路径
            var possiblePaths = new List<string>();
            
            // 1. 插件程序集所在目录的 tools 文件夹（full 版自带工具）
            var assemblyPath = typeof(NcmConverterPlugin).Assembly.Location;
            if (!string.IsNullOrEmpty(assemblyPath))
            {
                var assemblyDir = Path.GetDirectoryName(assemblyPath);
                possiblePaths.Add(Path.Combine(assemblyDir, "tools", "ncmdump.exe"));
                possiblePaths.Add(Path.Combine(assemblyDir, "ncmdump.exe"));
            }
            
            // 2. 当前工作目录
            possiblePaths.Add(Path.Combine(Directory.GetCurrentDirectory(), "ncmdump.exe"));
            
            // 3. 根目录
            possiblePaths.Add(Path.Combine(Path.GetDirectoryName(Directory.GetCurrentDirectory()), "ncmdump.exe"));
            
            // 4. 测试目录
            possiblePaths.Add(Path.Combine("D:/Projects/MusicUnlocker", "ncmdump.exe"));
            
            // 显示所有检查的路径
            reporter.OnLog("检查本地 ncmdump 工具路径:");
            foreach (var path in possiblePaths)
            {
                reporter.OnLog($"- {path} (存在: {File.Exists(path)})");
            }
            
            // 检查本地工具
            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    reporter.OnLog($"使用本地 ncmdump 工具: {path}");
                    _localToolPath = path;
                    return;
                }
            }

            // 检查共享缓存
            if (PluginAbstractions.SharedToolCache.IsToolCached(ToolName, ToolVersion))
            {
                reporter.OnLog($"ncmdump 已缓存");
                return;
            }

            reporter.OnLog("正在下载 ncmdump 工具...");

            // 尝试多个下载链接
            var downloadUrls = new List<string>
            {
                "https://github.com/taurusxin/ncmdump/releases/download/1.5.1/ncmdump-1.5.1-windows-amd64.zip",
                "https://github.com/taurusxin/ncmdump/releases/download/1.5.1/libncmdump-1.5.1-windows-amd64.zip",
                "https://github.com/taurusxin/ncmdump/releases/download/1.3.0/ncmdump-win-x64.exe",
                "https://github.com/taurusxin/ncmdump/releases/download/v1.3.0/ncmdump-win-x64.exe"
            };

            var toolDir = PluginAbstractions.SharedToolCache.GetToolDir(ToolName, ToolVersion);
            var cachedToolPath = PluginAbstractions.SharedToolCache.GetToolPath(ToolName, ToolExecutable, ToolVersion);

            Directory.CreateDirectory(toolDir);

            foreach (var downloadUrl in downloadUrls)
            {
                try
                {
                    reporter.OnLog($"[{ToolName}] downloading from {downloadUrl}...");

                    using var hc = new HttpClient();
                    hc.Timeout = TimeSpan.FromMinutes(30);
                    var data = await hc.GetByteArrayAsync(downloadUrl, cancellationToken).ConfigureAwait(false);
                    
                    // 下载到临时文件
                    var tempPath = Path.Combine(toolDir, Path.GetFileName(downloadUrl));
                    await File.WriteAllBytesAsync(tempPath, data, cancellationToken).ConfigureAwait(false);
                    
                    // 如果是 zip 文件，解压并提取 ncmdump.exe
                    if (downloadUrl.EndsWith(".zip"))
                    {
                        reporter.OnLog($"[{ToolName}] extracting from zip file...");
                        
                        // 创建临时解压目录
                        var extractDir = Path.Combine(toolDir, "extract");
                        if (Directory.Exists(extractDir))
                        {
                            Directory.Delete(extractDir, true);
                        }
                        Directory.CreateDirectory(extractDir);
                        
                        // 解压 zip 文件
                        ZipFile.ExtractToDirectory(tempPath, extractDir);
                        
                        // 查找 ncmdump.exe 文件
                        var exeFiles = Directory.GetFiles(extractDir, "ncmdump.exe", SearchOption.AllDirectories);
                        if (exeFiles.Length > 0)
                        {
                            // 复制找到的 ncmdump.exe 到目标位置
                            if (File.Exists(cachedToolPath))
                            {
                                File.Delete(cachedToolPath);
                            }
                            File.Copy(exeFiles[0], cachedToolPath);
                            
                            // 设置可执行权限
                            var fileInfo = new FileInfo(cachedToolPath);
                            fileInfo.IsReadOnly = false;
                            
                            // 删除临时文件
                            File.Delete(tempPath);
                            Directory.Delete(extractDir, true);
                            
                            reporter.OnLog($"[{ToolName}] installed to {toolDir}");
                            return;
                        }
                        else
                        {
                            reporter.OnLog($"[{ToolName}] ncmdump.exe not found in zip file");
                            // 删除临时文件
                            File.Delete(tempPath);
                            Directory.Delete(extractDir, true);
                            continue;
                        }
                    }
                    else
                    {
                        // 重命名为 ncmdump.exe
                        if (File.Exists(cachedToolPath))
                        {
                            File.Delete(cachedToolPath);
                        }
                        File.Move(tempPath, cachedToolPath);

                        // 设置可执行权限
                        var fileInfo = new FileInfo(cachedToolPath);
                        fileInfo.IsReadOnly = false;

                        reporter.OnLog($"[{ToolName}] installed to {toolDir}");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    reporter.OnLog($"[{ToolName}] download failed from {downloadUrl}: {ex.Message}");
                    // 继续尝试下一个链接
                }
            }

            // 所有下载链接都失败了，尝试使用本地工具
            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    reporter.OnLog($"使用本地 ncmdump 工具: {path}");
                    _localToolPath = path;
                    return;
                }
            }
            
            // 所有尝试都失败了，抛出异常
            throw new Exception("无法下载或找到 ncmdump 工具");
        }

        private string GetNcmdumpPath()
        {
            if (!string.IsNullOrEmpty(_localToolPath))
            {
                return _localToolPath;
            }
            return PluginAbstractions.SharedToolCache.GetToolPath(ToolName, ToolExecutable, ToolVersion);
        }

        private string GetDownloadUrl()
        {
            return "https://github.com/taurusxin/ncmdump/releases/download/1.3.0/ncmdump-win-x64.exe";
        }

        private string FindConvertedFile(string inputFile, string outputDir, string targetFormat)
        {
            var baseName = Path.GetFileNameWithoutExtension(inputFile);
            var possibleFiles = Directory.GetFiles(outputDir, $"{baseName}.{targetFormat}");

            if (possibleFiles.Length > 0)
            {
                return possibleFiles[0];
            }

            return null;
        }
    }
}