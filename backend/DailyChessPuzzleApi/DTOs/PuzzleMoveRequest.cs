using System.ComponentModel.DataAnnotations;

namespace DailyChessPuzzleApi.DTOs
{
    public class PuzzleMoveRequest
    {
        // Günlük puzzle oturumu için zorunludur.
        // Bonus puzzle akışında kullanılmaz.
        [Range(1, int.MaxValue)]
        public int? SessionId { get; set; }

        [StringLength(100, MinimumLength = 1)]
        public string? ClientId { get; set; }

        [Required]
        [StringLength(10, MinimumLength = 4)]
        public string Uci { get; set; } = string.Empty;

        // İstemcinin hamleyi gönderdiği sıradaki hamle indeksi.
        [Range(0, int.MaxValue)]
        public int? ExpectedMoveIndex { get; set; }
    }
}