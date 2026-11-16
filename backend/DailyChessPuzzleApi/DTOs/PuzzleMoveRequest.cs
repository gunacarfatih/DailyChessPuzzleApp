using System.ComponentModel.DataAnnotations;

namespace DailyChessPuzzleApi.DTOs
{
    public class PuzzleMoveRequest
    {
        [StringLength(100, MinimumLength = 1)]
        public string? ClientId { get; set; }

        [Required]
        [StringLength(10, MinimumLength = 4)]
        public string Uci { get; set; } = string.Empty;
    }
}