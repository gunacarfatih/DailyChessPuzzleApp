
import 'leaderboard_entry.dart';

class LeaderboardPeriod {
  final String period;
  final DateTime? startDate;
  final DateTime? endDate;
  final List<LeaderboardEntry> rankings;
  final LeaderboardEntry? currentUser;

  const LeaderboardPeriod({
    required this.period,
    this.startDate,
    this.endDate,
    this.rankings = const [],
    this.currentUser,
  });

  factory LeaderboardPeriod.fromJson(Map<String, dynamic> json) {
    final rawRankings = json['rankings'];

    final rankings = rawRankings is List
        ? rawRankings
        .whereType<Map>()
        .map(
          (item) => LeaderboardEntry.fromJson(
        Map<String, dynamic>.from(item),
      ),
    )
        .toList()
        : <LeaderboardEntry>[];

    final rawCurrentUser = json['currentUser'];

    return LeaderboardPeriod(
      period: json['period']?.toString() ?? '',
      startDate: DateTime.tryParse(
        json['startDate']?.toString() ?? '',
      ),
      endDate: DateTime.tryParse(
        json['endDate']?.toString() ?? '',
      ),
      rankings: rankings,
      currentUser: rawCurrentUser is Map
          ? LeaderboardEntry.fromJson(
        Map<String, dynamic>.from(rawCurrentUser),
      )
          : null,
    );
  }
}
