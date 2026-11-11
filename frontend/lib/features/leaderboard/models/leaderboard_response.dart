
import 'leaderboard_period.dart';

class LeaderboardResponse {
  final LeaderboardPeriod daily;
  final LeaderboardPeriod weekly;
  final LeaderboardPeriod monthly;
  final LeaderboardPeriod allTime;
  final LeaderboardPeriod? customRange;

  const LeaderboardResponse({
    required this.daily,
    required this.weekly,
    required this.monthly,
    required this.allTime,
    this.customRange,
  });

  factory LeaderboardResponse.fromJson(Map<String, dynamic> json) {
    LeaderboardPeriod parsePeriod(dynamic value) {
      if (value is Map) {
        return LeaderboardPeriod.fromJson(
          Map<String, dynamic>.from(value),
        );
      }

      return const LeaderboardPeriod(period: '');
    }

    final rawCustomRange = json['customRange'];

    return LeaderboardResponse(
      daily: parsePeriod(json['daily']),
      weekly: parsePeriod(json['weekly']),
      monthly: parsePeriod(json['monthly']),
      allTime: parsePeriod(json['allTime']),
      customRange: rawCustomRange is Map
          ? LeaderboardPeriod.fromJson(
        Map<String, dynamic>.from(rawCustomRange),
      )
          : null,
    );
  }
}
