
using DailyChessPuzzleApi.Models;

namespace DailyChessPuzzleApi.Services.Interfaces
{
    public interface IScoringService
    {
        ScoringResult Calculate(ScoringInput input);
    }
}
