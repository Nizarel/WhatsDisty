using FluentValidation;
using Whats.Hook.Constants;
using Whats.Hook.Models;

namespace Whats.Hook.Services
{
    public class MediaRequestValidator : AbstractValidator<WhatsEventType>
    {
        public MediaRequestValidator()
        {
            RuleFor(x => x.media)
                .NotNull()
                .WithMessage("Media data is required");
                
            RuleFor(x => x.media!.id)
                .NotEmpty()
                .When(x => x.media != null)
                .WithMessage("Media ID is required");
                
            RuleFor(x => x.media!.mimeType)
                .Must(BeValidMediaType)
                .When(x => x.media != null)
                .WithMessage("Unsupported media type for AI processing");
        }
        
        private bool BeValidMediaType(string? mimeType)
        {
            return mimeType != null && 
                   (MediaTypes.ImageMimeTypes.Contains(mimeType) || 
                    MediaTypes.VoiceMimeTypes.Contains(mimeType));
        }
    }

    public class MediaProcessingConfiguration
    {
        public AIAgentMode AgentMode { get; set; } = AIAgentMode.Standard;
        public bool EnableBatchProcessing { get; set; } = true;
        public bool UseSmartModelSelection { get; set; } = true;
        public int MaxConcurrentProcessing { get; set; } = 3;
        public TimeSpan ProcessingTimeout { get; set; } = TimeSpan.FromSeconds(30);
        
        // AI-specific settings
        public bool EnableContextualAnalysis { get; set; } = true;
        public bool PreservePreviousContext { get; set; } = true;
        public int ContextWindowSize { get; set; } = 5;
        public bool EnableCaching { get; set; } = true;
        public bool OptimizeForAI { get; set; } = true;
    }

    public enum AIAgentMode
    {
        Standard,
        HighAccuracy,
        FastResponse,
        Multimodal
    }

    public record MediaProcessingResult(
        string? Result,
        bool IsSuccess,
        string? ErrorMessage,
        TimeSpan ProcessingTime,
        string MediaType
    );

    public record ImageProcessingConfig(
        bool IsDetailedAnalysis,
        bool EnhanceContrast,
        bool OptimizeForText
    );
}
