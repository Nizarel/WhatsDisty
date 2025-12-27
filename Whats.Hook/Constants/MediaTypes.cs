using System.Collections.Generic;

namespace Whats.Hook.Constants
{
    public static class MediaTypes
    {
        public static readonly HashSet<string> ImageMimeTypes = new()
        {
            "image/jpeg",
            "image/jpg",
            "image/png",
            "image/gif",
            "image/webp",
            "image/bmp",
            "image/tiff"
        };

        public static readonly HashSet<string> VoiceMimeTypes = new()
        {
            "audio/ogg; codecs=opus",
            "audio/ogg",
            "video/mp4",
            "audio/mpeg",
            "audio/wav",
            "audio/mp3",
            "audio/aac",
            "audio/m4a"
        };

        public const string DefaultImageMimeType = "image/jpeg";
        public const string DefaultVoiceMimeType = "audio/wav";
        public const string OctetStreamMimeType = "application/octet-stream";
    }
}
