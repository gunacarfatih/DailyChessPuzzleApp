
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace DailyChessPuzzleApi.Hubs;

[Authorize]
public class LeaderboardHub : Hub
{
}
