using System.Diagnostics;

namespace Whats.Hook.Services
{
    /// <summary>
    /// Converts audio between formats using FFmpeg.
    /// Required for WhatsApp voice messages (OGG Opus) to WAV conversion for STT API.
    /// </summary>
    public static class AudioConverter
    {
        /// <summary>
        /// Converts OGG Opus audio to WAV format using FFmpeg.
        /// </summary>
        /// <param name="oggData">The OGG audio data</param>
        /// <param name="logger">Logger for diagnostics</param>
        /// <returns>WAV audio data, or null if conversion fails</returns>
        public static async Task<byte[]?> ConvertOggToWavAsync(byte[] oggData, ILogger logger)
        {
            var tempInput = Path.Combine(Path.GetTempPath(), $"input_{Guid.NewGuid():N}.ogg");
            var tempOutput = Path.Combine(Path.GetTempPath(), $"output_{Guid.NewGuid():N}.wav");

            try
            {
                // Write input file
                await File.WriteAllBytesAsync(tempInput, oggData);
                logger.LogDebug("🔄 Written {Size} bytes to temp input: {Path}", oggData.Length, tempInput);

                // Run FFmpeg conversion
                // -y: overwrite output
                // -i: input file
                // -ar 16000: sample rate 16kHz (good for speech)
                // -ac 1: mono channel
                // -c:a pcm_s16le: 16-bit PCM WAV
                var processInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = $"-y -i \"{tempInput}\" -ar 16000 -ac 1 -c:a pcm_s16le \"{tempOutput}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                logger.LogInformation("🔄 Starting FFmpeg conversion: OGG ({Size} bytes) -> WAV", oggData.Length);

                using var process = Process.Start(processInfo);
                if (process == null)
                {
                    logger.LogError("❌ Failed to start FFmpeg process");
                    return null;
                }

                var stderr = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    logger.LogError("❌ FFmpeg conversion failed. Exit code: {ExitCode}, Error: {Error}", 
                        process.ExitCode, stderr);
                    return null;
                }

                if (!File.Exists(tempOutput))
                {
                    logger.LogError("❌ FFmpeg output file not found: {Path}", tempOutput);
                    return null;
                }

                var wavData = await File.ReadAllBytesAsync(tempOutput);
                logger.LogInformation("✅ FFmpeg conversion successful: {InputSize} bytes OGG -> {OutputSize} bytes WAV", 
                    oggData.Length, wavData.Length);

                return wavData;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "❌ Audio conversion error");
                return null;
            }
            finally
            {
                // Cleanup temp files
                try
                {
                    if (File.Exists(tempInput)) File.Delete(tempInput);
                    if (File.Exists(tempOutput)) File.Delete(tempOutput);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to cleanup temp audio files");
                }
            }
        }

        /// <summary>
        /// Converts WAV audio to OGG Opus format for WhatsApp.
        /// WhatsApp doesn't support WAV - it needs MP3, OGG, AAC, or AMR.
        /// </summary>
        public static async Task<byte[]?> ConvertWavToOggAsync(byte[] wavData, ILogger logger)
        {
            var tempInput = Path.Combine(Path.GetTempPath(), $"input_{Guid.NewGuid():N}.wav");
            var tempOutput = Path.Combine(Path.GetTempPath(), $"output_{Guid.NewGuid():N}.ogg");

            try
            {
                await File.WriteAllBytesAsync(tempInput, wavData);

                // Convert WAV to OGG Opus (WhatsApp compatible)
                // -c:a libopus: use Opus codec
                // -b:a 32k: bitrate for voice
                var processInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = $"-y -i \"{tempInput}\" -c:a libopus -b:a 32k \"{tempOutput}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                logger.LogInformation("🔄 Starting FFmpeg conversion: WAV ({Size} bytes) -> OGG Opus", wavData.Length);

                using var process = Process.Start(processInfo);
                if (process == null)
                {
                    logger.LogError("❌ Failed to start FFmpeg process for WAV->OGG");
                    return null;
                }

                var stderr = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    logger.LogError("❌ FFmpeg WAV->OGG conversion failed. Exit code: {ExitCode}, Error: {Error}", 
                        process.ExitCode, stderr);
                    return null;
                }

                if (!File.Exists(tempOutput))
                {
                    logger.LogError("❌ FFmpeg OGG output file not found: {Path}", tempOutput);
                    return null;
                }

                var oggData = await File.ReadAllBytesAsync(tempOutput);
                logger.LogInformation("✅ FFmpeg conversion successful: {InputSize} bytes WAV -> {OutputSize} bytes OGG", 
                    wavData.Length, oggData.Length);

                return oggData;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "❌ WAV to OGG conversion error");
                return null;
            }
            finally
            {
                try
                {
                    if (File.Exists(tempInput)) File.Delete(tempInput);
                    if (File.Exists(tempOutput)) File.Delete(tempOutput);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to cleanup temp audio files");
                }
            }
        }

        /// <summary>
        /// Checks if FFmpeg is available in the system.
        /// </summary>
        public static bool IsFFmpegAvailable()
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = "-version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(processInfo);
                process?.WaitForExit(5000);
                return process?.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
