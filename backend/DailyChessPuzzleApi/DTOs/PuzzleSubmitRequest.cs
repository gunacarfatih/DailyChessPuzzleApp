
using System.ComponentModel.DataAnnotations;

namespace DailyChessPuzzleApi.DTOs
{
    public class PuzzleSubmitRequest
    {
        // Misafir kullanıcılar için gereklidir.
        // Kayıtlı kullanıcılar ClientId göndermeyebilir.
        [StringLength(100, MinimumLength = 1)]
        public string? ClientId { get; set; }

        [Range(1, int.MaxValue)]
        public int PuzzleId { get; set; }

        [Range(0, int.MaxValue)]
        public int Seconds { get; set; }

        [Range(0, int.MaxValue)]
        public int WrongMoves { get; set; }

        public bool UsedHint { get; set; }

        [StringLength(2, MinimumLength = 2)]
        public string? CountryCode { get; set; }
    }
}
