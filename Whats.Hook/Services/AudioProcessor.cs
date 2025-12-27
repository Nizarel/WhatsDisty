using NAudio.Wave;

namespace Whats.Hook.Services
{
    public record AudioMetadata(
        TimeSpan Duration,
        int SampleRate,
        int Channels,
        int BitsPerSample,
        int EstimatedSize,
        bool IsOptimizedForAI
    );

    public static class AudioProcessor
    {
        public static async Task<byte[]> OptimizeAudioForAIAsync(Stream audioStream, string originalFormat)
        {
            try
            {
                // Convert various audio formats to AI-optimized format
                using var reader = CreateAudioReader(audioStream, originalFormat);
                
                // AI-optimized audio: 16kHz, 16-bit, mono (standard for speech recognition)
                var targetFormat = new WaveFormat(16000, 16, 1);
                
                using var resampler = new MediaFoundationResampler(reader, targetFormat);
                using var outputStream = new MemoryStream();
                
                // Convert to optimized format
                WaveFileWriter.WriteWavFileToStream(outputStream, resampler);
                return outputStream.ToArray();
            }
            catch (Exception)
            {
                // Fallback: return original stream as bytes if conversion fails
                audioStream.Position = 0;
                using var ms = new MemoryStream();
                await audioStream.CopyToAsync(ms);
                return ms.ToArray();
            }
        }
        
        public static Task<AudioMetadata> AnalyzeAudioAsync(Stream audioStream)
        {
            try
            {
                using var reader = new WaveFileReader(audioStream);
                
                return Task.FromResult(new AudioMetadata(
                    Duration: reader.TotalTime,
                    SampleRate: reader.WaveFormat.SampleRate,
                    Channels: reader.WaveFormat.Channels,
                    BitsPerSample: reader.WaveFormat.BitsPerSample,
                    EstimatedSize: (int)reader.Length,
                    IsOptimizedForAI: reader.WaveFormat.SampleRate == 16000 && reader.WaveFormat.Channels == 1
                ));
            }
            catch (Exception)
            {
                // Return default metadata if analysis fails
                return Task.FromResult(new AudioMetadata(
                    Duration: TimeSpan.Zero,
                    SampleRate: 0,
                    Channels: 0,
                    BitsPerSample: 0,
                    EstimatedSize: 0,
                    IsOptimizedForAI: false
                ));
            }
        }

        private static WaveStream CreateAudioReader(Stream audioStream, string originalFormat)
        {
            return originalFormat.ToLower() switch
            {
                var f when f.Contains("mp3") => new Mp3FileReader(audioStream),
                var f when f.Contains("wav") => new WaveFileReader(audioStream),
                _ => new WaveFileReader(audioStream) // Default fallback
            };
        }
    }
}
